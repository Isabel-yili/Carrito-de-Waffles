using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// RADIO CONTROLLER — objeto interactivo de escena.
///
/// ── POR QUÉ NO SE DETECTABA EL CLICK ────────────────────────────
/// El log "[DragManager] → Sin objeto interactivo" con hits:0 confirma
/// que Physics2D.OverlapPoint() no detecta el collider de la radio.
/// Cuando OverlapPoint falla, OnMouseDown tampoco se dispara porque
/// Unity usa el mismo pipeline de física 2D para ambos.
///
/// Causa más común en este proyecto:
///   HandleWorldClick() construye el worldPos así:
///     mouseScreen.z = Mathf.Abs(cam.transform.position.z);   ← profundidad de la cámara
///     Vector2 worldPos = cam.ScreenToWorldPoint(mouseScreen); ← descarta Z al castear a Vector2
///   Esto es correcto para objetos en Z=0. Si el GameObject de la radio
///   (o algún padre suyo) tiene Z ≠ 0, el Collider2D queda en un plano
///   distinto y OverlapPoint no lo alcanza.
///   → Verificar que Transform.position.z == 0 en la radio y todos sus padres.
///
/// Otras causas posibles:
///   • Layer Collision Matrix: la capa del collider no colisiona con ninguna.
///     (ContactFilter2D.NoFilter() sí detecta todas las capas, así que esto
///     solo importa si se usa un filter personalizado en otro lugar.)
///   • Collider2D demasiado pequeño o desplazado respecto al sprite.
///   • El objeto está desactivado o el componente Collider2D está disabled.
///
/// ── SOLUCIÓN: TRIPLE FALLBACK DE INPUT ──────────────────────────
///   1. Button de Unity UI (Canvas World Space) — más fiable, independiente
///      de Physics2D. RECOMENDADO.
///   2. HandleWorldClick del DragManager — ver archivo DragManager_Patch.cs
///      adjunto: añade soporte para RadioController igual que para Oven.
///   3. OnMouseDown — sigue presente; funciona si Physics2D detecta el collider.
///
/// ── SETUP EN UNITY (opción recomendada) ─────────────────────────
///   Radio  (este script + Collider2D en capa Default, Z = 0 obligatorio)
///   └── RadioCanvas  (Canvas — Render Mode: World Space, Sort Order alto)
///       └── RadioButton  (Button que cubre el sprite; Image alpha = 0)
///   → Arrastra RadioButton al campo "radioButton" en el Inspector.
///
/// ── INTEGRACIÓN CON AUDIO MANAGER ───────────────────────────────
///   Usa el musicSource del AudioManager directamente para mantener
///   ducking, faders y control de volumen sincronizados.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RadioController : MonoBehaviour
{
    // ─── Inspector ──────────────────────────────────────────────────

    [Header("══ Botón UI (recomendado) ══")]
    [Tooltip("Button de un Canvas World Space superpuesto al sprite.\n" +
             "Es el método más fiable: no depende de Physics2D.\n" +
             "Si se deja null, el script usa OnMouseDown + parche del DragManager.")]
    [SerializeField] private Button radioButton;

    [Header("══ Visual ══")]
    [Tooltip("SpriteRenderer del objeto radio en el mundo 2D.")]
    [SerializeField] private SpriteRenderer radioSpriteRenderer;
    [Tooltip("Image de UI — alternativa si el sprite está en un Canvas.")]
    [SerializeField] private Image radioImage;
    [SerializeField] private Sprite radioOnSprite;
    [SerializeField] private Sprite radioOffSprite;

    [Header("══ Canciones ══")]
    [Tooltip("Clips en orden. Cada click avanza una pista.\n" +
             "Al pasar el último → mutea.\n" +
             "Click sobre radio muteada → reanuda desde el inicio de la misma pista.")]
    [SerializeField] private AudioClip[] musicTracks;

    [Header("══ Comportamiento ══")]
    [Tooltip("Duración del fade al cambiar de canción o mutear (segundos). 0 = instantáneo.")]
    [SerializeField][Range(0f, 1f)] private float trackFadeDuration = 0.25f;

    // ─── Estado ─────────────────────────────────────────────────────

    // -1  = ninguna canción activa (radio muteada o recién iniciada)
    // 0+  = índice de la canción en musicTracks[]
    private int _currentTrackIndex = -1;
    private bool _isMuted = false;
    private bool _isChangingTrack = false;

    private AudioSource MusicSource =>
        AudioManager.Instance != null ? AudioManager.Instance.musicSource : null;

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    private void Start()
    {
        // Fallback 1: Unity UI Button
        if (radioButton != null)
            radioButton.onClick.AddListener(HandleClick);

        UpdateVisual();
    }

    // ═══════════════════════════════════════════════════════════════
    // FALLBACK 3: OnMouseDown — solo activo si no hay Button UI
    // ═══════════════════════════════════════════════════════════════

    private void OnMouseDown()
    {
        // Si hay Button UI, ese maneja el click. Evitar duplicado.
        if (radioButton != null) return;

        // No interrumpir arrastre de ítems
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;

        // Marcar para que HandleWorldClick del DragManager no lo procese también
        DragManager.Instance?.MarkClickHandled();

        HandleClick();
    }

    // ═══════════════════════════════════════════════════════════════
    // PUNTO DE ENTRADA PÚBLICO
    // Llamado por: radioButton.onClick  |  OnMouseDown  |  DragManager (parche)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Procesa un click sobre la radio. Público para que el DragManager
    /// lo llame directamente desde HandleWorldClick (ver DragManager_Patch.cs).
    /// </summary>
    public void HandleClick()
    {
        if (_isChangingTrack) return;

        if (musicTracks == null || musicTracks.Length == 0)
        {
            Debug.LogWarning("[RadioController] musicTracks[] vacío — asignar clips en el Inspector.");
            return;
        }

        if (AudioManager.Instance == null)
        {
            Debug.LogWarning("[RadioController] AudioManager no encontrado en escena.");
            return;
        }

        AudioManager.Instance.PlaySound(SoundType.ButtonClick);

        // ── Ciclo de estados ────────────────────────────────────────
        //
        //  [Muteada]  →  reanudar misma pista desde el inicio
        //  [Sonando]  →  avanzar a la siguiente pista
        //    si era la última pista → mutear
        //    si no → reproducir la nueva pista

        if (_isMuted)
        {
            // Si _currentTrackIndex es -1, la radio nunca llegó a sonar
            // (edge-case: se puso en mute antes del primer play). En ese
            // caso arrancamos desde la primera pista en lugar de reanudar.
            if (_currentTrackIndex < 0)
                _currentTrackIndex = 0;

            _isMuted = false;
            PlayCurrentTrack();
            Debug.Log($"[RadioController] Reanudando: {musicTracks[_currentTrackIndex].name}");
            return;
        }

        _currentTrackIndex++;

        if (_currentTrackIndex >= musicTracks.Length)
        {
            // Pasamos la última pista: mutear
            _currentTrackIndex = -1;
            _isMuted = true;
            MuteRadio();
            Debug.Log("[RadioController] Radio muteada.");
        }
        else
        {
            PlayCurrentTrack();
            Debug.Log($"[RadioController] Pista {_currentTrackIndex + 1}/{musicTracks.Length}: " +
                      $"{musicTracks[_currentTrackIndex].name}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // REPRODUCCIÓN
    // ═══════════════════════════════════════════════════════════════

    private void PlayCurrentTrack()
    {
        if (_currentTrackIndex < 0 || _currentTrackIndex >= musicTracks.Length) return;

        AudioSource source = MusicSource;
        if (source == null)
        {
            Debug.LogWarning("[RadioController] musicSource del AudioManager es null.");
            return;
        }

        AudioClip clip = musicTracks[_currentTrackIndex];
        if (clip == null)
        {
            Debug.LogWarning($"[RadioController] Clip [{_currentTrackIndex}] es null.");
            return;
        }

        if (trackFadeDuration > 0f)
            StartCoroutine(CrossfadeToTrack(source, clip));
        else
        {
            source.clip = clip;
            source.loop = true;
            source.time = 0f;
            source.Play();
        }

        UpdateVisual();
    }

    private void MuteRadio()
    {
        AudioSource source = MusicSource;

        if (trackFadeDuration > 0f && source != null)
            StartCoroutine(FadeOutAndMute(source));
        else
            source?.Stop();

        UpdateVisual();
    }

    // ═══════════════════════════════════════════════════════════════
    // COROUTINES — unscaledDeltaTime para funcionar durante la pausa
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator CrossfadeToTrack(AudioSource source, AudioClip newClip)
    {
        _isChangingTrack = true;

        // Fade out del clip actual
        if (source.isPlaying)
        {
            float startVol = source.volume;
            float elapsed = 0f;
            while (elapsed < trackFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(startVol, 0f, elapsed / trackFadeDuration);
                yield return null;
            }
        }

        // Cambiar clip y fade in
        source.clip = newClip;
        source.loop = true;
        source.time = 0f;
        source.Play();

        float target = AudioManager.Instance != null ? AudioManager.Instance.GetMusicVolume() : 0.7f;
        float elapsed2 = 0f;
        while (elapsed2 < trackFadeDuration)
        {
            elapsed2 += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(0f, target, elapsed2 / trackFadeDuration);
            yield return null;
        }
        source.volume = target;

        _isChangingTrack = false;
    }

    private IEnumerator FadeOutAndMute(AudioSource source)
    {
        _isChangingTrack = true;

        float startVol = source.volume;
        float elapsed = 0f;
        while (elapsed < trackFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVol, 0f, elapsed / trackFadeDuration);
            yield return null;
        }

        source.Stop();
        _isChangingTrack = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // VISUAL
    // ═══════════════════════════════════════════════════════════════

    private void UpdateVisual()
    {
        bool isOn = !_isMuted && _currentTrackIndex >= 0;
        Sprite s = isOn ? radioOnSprite : radioOffSprite;

        if (radioSpriteRenderer != null) radioSpriteRenderer.sprite = s;
        if (radioImage != null) radioImage.sprite = s;
    }

    // ═══════════════════════════════════════════════════════════════
    // API PÚBLICA
    // ═══════════════════════════════════════════════════════════════

    public bool IsPlaying => !_isMuted && _currentTrackIndex >= 0;
    public AudioClip CurrentTrack => IsPlaying ? musicTracks[_currentTrackIndex] : null;

    /// <summary>Silencia temporalmente durante pausa del juego.</summary>
    public void PauseForGamePause() { if (IsPlaying) MusicSource?.Pause(); }

    /// <summary>Reanuda tras salir de pausa, si la radio estaba activa.</summary>
    public void ResumeAfterGamePause() { if (IsPlaying) MusicSource?.UnPause(); }
}