using UnityEngine;
using System.Collections;

// ═══════════════════════════════════════════════════════════════════
// TIPOS DE SONIDO — GDD sección 12.2
// Un enum por cada evento de audio del juego.
// ═══════════════════════════════════════════════════════════════════
public enum SoundType
{
    // Interacción con ítems
    ItemPickup,      // Pop suave, metálico-plástico — al tomar cualquier ítem
    ItemPlaced,      // Sonido seco de objeto depositado — colocar en receptor válido
    InvalidAction,   // Click vacío / golpe sordo — receptor no acepta el ítem

    // Horno
    OvenStart,       // Chisporroteo breve de plancha caliente — waffle entra al horno
    OvenReady,       // Timbre de cocina suave "ding" — waffle listo para sacar
    WaffleBurned,    // Alarma suave + chirrido — urgente pero no irritante

    // Recetas y entrega
    RecipeComplete,  // Jingle de 2 notas ascendentes — plato listo
    DeliverySuccess, // Sonido de moneda + jingle — pedido correcto
    DeliveryError,   // Buzzer suave, 1 nota descendente — pedido incorrecto

    // Clientes — GDD sección 12.2
    CustomerHappy,   // "¡Mmm!" aprobación corta — cliente satisfecho
    CustomerLeave,   // Puerta que se cierra + nota de desaprobación — cliente se va

    // Estado del juego
    GameOver,        // Stinger descendente (2-3 notas) — GDD sección 12.1
    TimeUp,          // Jingle alegre (3-4 segundos) — fin de partida exitoso
    ButtonClick,     // Click UI genérico — menús y botones
}

/// <summary>
/// AUDIO MANAGER — GDD sección 12
/// Gestiona todos los efectos de sonido y la música de fondo.
///
/// CONFIGURACIÓN EN UNITY:
///   1. Crear un GameObject vacío llamado "AudioManager" dentro de [Managers]
///   2. Añadir este script como componente
///   3. En el Inspector, asignar un AudioClip por cada SoundType
///   4. Añadir un AudioSource hijo para música (arrastrar al campo musicSource)
///
/// PAUTAS DE DISEÑO (GDD sección 12.3):
///   - Todos los SFX deben durar menos de 1 segundo
///   - La música baja automáticamente al reproducir SFX importantes (ducking)
///   - El jugador puede ajustar volumen de música y SFX por separado
///   - Priorizar sonidos agradables; los de error son claros pero no punitivos
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // ─── Entrada de sonido en el Inspector ───────────────────────────
    [System.Serializable]
    public class SoundEntry
    {
        public SoundType type;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Variación aleatoria de pitch ±X para evitar monotonía")]
        [Range(0f, 0.2f)] public float pitchVariance = 0.05f;
    }

    // ─── Inspector ────────────────────────────────────────────────────
    [Header("═══ Efectos de Sonido (SFX) ═══")]
    [Tooltip("Asignar un AudioClip por cada SoundType del enum")]
    public SoundEntry[] sounds;

    [Header("═══ Música de Fondo ═══")]
    [Tooltip("AudioSource dedicado a la música (Loop = true, Play On Awake = false)")]
    public AudioSource musicSource;
    public AudioClip musicMenu;
    public AudioClip musicGameplay;
    public AudioClip musicGameplayIntense; // GDD: variante más intensa en los últimos 30s

    [Header("═══ Volúmenes Base ═══")]
    [Range(0f, 1f)] public float sfxVolumeMultiplier  = 1f;
    [Range(0f, 1f)] public float musicVolumeBase      = 0.7f;

    [Header("═══ Ducking (GDD 12.3) ═══")]
    [Tooltip("Bajar música al reproducir SFX importantes")]
    public bool enableDucking = true;
    [Range(0f, 1f)] public float duckingLevel    = 0.3f;
    public float duckingFadeTime                 = 0.1f;
    public float duckingRestoreTime              = 0.5f;

    // ─── Estado interno ───────────────────────────────────────────────
    private AudioSource _sfxSource;
    private bool _isDucking = false;
    private Coroutine _duckCoroutine;

    // Tipos de SFX que activan el ducking de música
    private static readonly SoundType[] DuckingSounds =
    {
        SoundType.DeliverySuccess,
        SoundType.DeliveryError,
        SoundType.WaffleBurned,
        SoundType.OvenReady,
        SoundType.GameOver,
        SoundType.TimeUp,
    };

    // ═════════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═════════════════════════════════════════════════════════════════

    void Awake()
    {
        // Singleton persistente entre escenas — GDD: no hay guardado entre partidas,
        // pero la música del menú debe continuar si se reinicia
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Crear AudioSource para SFX en el mismo GameObject
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;

        // Configurar música si no tiene AudioSource asignado
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop       = true;
            musicSource.playOnAwake = false;
            musicSource.volume     = musicVolumeBase;
        }
    }

    void Start()
    {
        // Arrancar música según la escena activa
        PlayMusic(musicMenu);
    }

    // ═════════════════════════════════════════════════════════════════
    // API PÚBLICA — EFECTOS DE SONIDO
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reproduce un efecto de sonido por tipo.
    /// Si no hay clip asignado, loguea silenciosamente (no rompe el prototipo).
    /// </summary>
    public void PlaySound(SoundType type)
    {
        SoundEntry entry = FindEntry(type);

        if (entry == null || entry.clip == null)
        {
            Debug.Log($"[AudioManager] '{type}' — clip no asignado aún");
            return;
        }

        // Pitch con variación aleatoria para que los sonidos no sean monótonos
        float pitch = 1f + Random.Range(-entry.pitchVariance, entry.pitchVariance);
        _sfxSource.pitch = pitch;
        _sfxSource.PlayOneShot(entry.clip, entry.volume * sfxVolumeMultiplier);

        // Ducking automático para SFX importantes
        if (enableDucking && ShouldDuck(type))
            TriggerDucking();
    }

    /// <summary>
    /// Reproduce un sonido en una posición 3D del mundo (para clientes, hornos, etc.)
    /// </summary>
    public void PlaySoundAtPosition(SoundType type, Vector3 worldPosition)
    {
        SoundEntry entry = FindEntry(type);
        if (entry == null || entry.clip == null) return;

        AudioSource.PlayClipAtPoint(
            entry.clip,
            worldPosition,
            entry.volume * sfxVolumeMultiplier
        );
    }

    // ═════════════════════════════════════════════════════════════════
    // API PÚBLICA — MÚSICA
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cambia la música con crossfade suave.
    /// Si el clip ya está sonando, no hace nada.
    /// </summary>
    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        StopAllCoroutines();
        StartCoroutine(CrossfadeMusic(clip));
    }

    public void PlayGameplayMusic()    => PlayMusic(musicGameplay);
    public void PlayMenuMusic()        => PlayMusic(musicMenu);
    public void PlayIntenseMusic()     => PlayMusic(musicGameplayIntense);

    public void StopMusic()
    {
        if (musicSource != null)
            StartCoroutine(FadeOutMusic(0.5f));
    }

    // ═════════════════════════════════════════════════════════════════
    // API PÚBLICA — VOLUMEN (llamado desde menú de Configuración)
    // GDD sección 6.3: ajuste independiente de música y SFX
    // ═════════════════════════════════════════════════════════════════

    public void SetMusicVolume(float v)
    {
        musicVolumeBase = Mathf.Clamp01(v);
        if (musicSource != null && !_isDucking)
            musicSource.volume = musicVolumeBase;
    }

    public void SetSfxVolume(float v)
    {
        sfxVolumeMultiplier = Mathf.Clamp01(v);
    }

    public float GetMusicVolume() => musicVolumeBase;
    public float GetSfxVolume()   => sfxVolumeMultiplier;

    // ═════════════════════════════════════════════════════════════════
    // DUCKING — GDD 12.3: "la música baja de volumen automáticamente"
    // ═════════════════════════════════════════════════════════════════

    private void TriggerDucking()
    {
        if (_duckCoroutine != null)
            StopCoroutine(_duckCoroutine);
        _duckCoroutine = StartCoroutine(DuckingRoutine());
    }

    private IEnumerator DuckingRoutine()
    {
        _isDucking = true;

        // Bajar volumen
        yield return StartCoroutine(FadeMusicTo(duckingLevel * musicVolumeBase, duckingFadeTime));

        // Esperar a que el SFX termine (aprox.)
        yield return new WaitForSeconds(0.8f);

        // Restaurar volumen
        yield return StartCoroutine(FadeMusicTo(musicVolumeBase, duckingRestoreTime));
        _isDucking = false;
    }

    private IEnumerator FadeMusicTo(float targetVolume, float duration)
    {
        if (musicSource == null) yield break;

        float startVolume = musicSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
            yield return null;
        }

        musicSource.volume = targetVolume;
    }

    private IEnumerator CrossfadeMusic(AudioClip newClip)
    {
        // Fade out del clip actual
        if (musicSource.isPlaying)
            yield return StartCoroutine(FadeMusicTo(0f, 0.4f));

        // Cambiar clip y fade in
        musicSource.clip = newClip;
        musicSource.Play();
        yield return StartCoroutine(FadeMusicTo(musicVolumeBase, 0.6f));
    }

    private IEnumerator FadeOutMusic(float duration)
    {
        yield return StartCoroutine(FadeMusicTo(0f, duration));
        musicSource.Stop();
    }

    // ═════════════════════════════════════════════════════════════════
    // HELPERS PRIVADOS
    // ═════════════════════════════════════════════════════════════════

    private SoundEntry FindEntry(SoundType type)
    {
        if (sounds == null) return null;
        foreach (var entry in sounds)
            if (entry.type == type) return entry;
        return null;
    }

    private bool ShouldDuck(SoundType type)
    {
        foreach (var t in DuckingSounds)
            if (t == type) return true;
        return false;
    }

    // ═════════════════════════════════════════════════════════════════
    // CONFIGURACIÓN EN INSPECTOR — guía rápida
    // ═════════════════════════════════════════════════════════════════
    // 
    // Array "sounds" — asignar en este orden recomendado:
    //
    //  [0]  ItemPickup       → SFX_ItemPickup.wav      ~0.1s  vol 0.8
    //  [1]  ItemPlaced       → SFX_ItemPlace.wav        ~0.15s vol 0.7
    //  [2]  InvalidAction    → SFX_Invalid.wav          ~0.2s  vol 0.5
    //  [3]  OvenStart        → SFX_Sizzle.wav           ~0.3s  vol 0.9
    //  [4]  OvenReady        → SFX_Ding.wav             ~0.5s  vol 1.0
    //  [5]  WaffleBurned     → SFX_Burn.wav             ~0.8s  vol 0.9
    //  [6]  RecipeComplete   → SFX_RecipeReady.wav      ~0.4s  vol 0.9
    //  [7]  DeliverySuccess  → SFX_CoinJingle.wav       ~0.6s  vol 1.0
    //  [8]  DeliveryError    → SFX_Buzzer.wav           ~0.4s  vol 0.8
    //  [9]  CustomerHappy    → SFX_Mmm.wav              ~0.5s  vol 0.7
    //  [10] CustomerLeave    → SFX_DoorClose.wav        ~0.6s  vol 0.8
    //  [11] GameOver         → SFX_GameOver.wav         ~1.0s  vol 1.0
    //  [12] TimeUp           → SFX_TimeUp.wav           ~0.8s  vol 1.0
    //  [13] ButtonClick      → SFX_UIClick.wav          ~0.1s  vol 0.6
}
