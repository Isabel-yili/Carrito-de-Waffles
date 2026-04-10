using UnityEngine;
using System.Collections;

// ═══════════════════════════════════════════════════════════════════
// FEEDBACK MANAGER — GDD sección 6.2
// "Destello verde, texto flotante, animación de cliente feliz, etc."
// ═══════════════════════════════════════════════════════════════════

public class FeedbackManager : MonoBehaviour
{
    public static FeedbackManager Instance { get; private set; }

    [Header("Prefabs de Feedback")]
    public GameObject floatingTextPrefab;  // Texto que sube y desaparece
    public GameObject successFlashPrefab;  // Destello verde
    public GameObject errorFlashPrefab;    // Destello rojo
    public GameObject burnEffectPrefab;    // Humo
    public GameObject readyGlowPrefab;     // Destello dorado del horno listo

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─── Entregas ─────────────────────────────────────────────────

    /// <summary>GDD: "destello verde + texto flotante '+$XX'"</summary>
    public void ShowSuccessDelivery(Vector3 position, int amount)
    {
        SpawnFloatingText(position, $"+${amount}", Color.green);
        SpawnEffect(successFlashPrefab, position);
    }

    /// <summary>GDD: "destello rojo + texto flotante 'ERROR'"</summary>
    public void ShowErrorDelivery(Vector3 position)
    {
        SpawnFloatingText(position, "ERROR", Color.red);
        SpawnEffect(errorFlashPrefab, position);
        StartCoroutine(ScreenShake(0.2f, 0.1f));
    }

    // ─── Horno ────────────────────────────────────────────────────

    public void ShowBurnEffect(Vector3 position)
    {
        SpawnEffect(burnEffectPrefab, position);
        StartCoroutine(ScreenShake(0.3f, 0.15f));
    }

    public void ShowReadyGlow(Vector3 position)
    {
        SpawnEffect(readyGlowPrefab, position);
    }

    // ─── Clientes ─────────────────────────────────────────────────

    public void ShowCustomerHappy(Vector3 position)
    {
        SpawnFloatingText(position, "¡Gracias!", Color.yellow);
    }

    public void ShowCustomerLeave(Vector3 position)
    {
        SpawnFloatingText(position, "¡Me voy!", Color.red);
    }

    public void ShowRecipeComplete(Vector3 position)
    {
        SpawnFloatingText(position, "¡Listo!", new Color(0.5f, 1f, 0.5f));
    }

    public void ShowMoneyGained(int amount)
    {
        // Texto flotante en la posición del HUD de dinero
        // En prototipo: usar posición fija en pantalla
    }

    public void ShowInvalidAction(Vector3 position)
    {
        StartCoroutine(ShakeObject(null, 0.1f, 0.05f)); // Shake del objeto inválido
    }

    public void ShakeErrorCounter()
    {
        // Shake del indicador de errores en el HUD
        StartCoroutine(ScreenShake(0.15f, 0.08f));
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private void SpawnFloatingText(Vector3 position, string text, Color color)
    {
        if (floatingTextPrefab == null) { Debug.Log($"[FB] {text}"); return; }

        GameObject go = Instantiate(floatingTextPrefab, position, Quaternion.identity);
        FloatingText ft = go.GetComponent<FloatingText>();
        if (ft != null) ft.Setup(text, color);
    }

    private void SpawnEffect(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;
        GameObject go = Instantiate(prefab, position, Quaternion.identity);
        Destroy(go, 2f); // Auto-destruir tras 2 segundos
    }

    private IEnumerator ScreenShake(float duration, float magnitude)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 originalPos = cam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            cam.transform.localPosition = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.transform.localPosition = originalPos;
    }

    private IEnumerator ShakeObject(Transform target, float duration, float magnitude)
    {
        if (target == null) yield break;

        Vector3 originalPos = target.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            target.localPosition = new Vector3(originalPos.x + x, originalPos.y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        target.localPosition = originalPos;
    }
}

// ═══════════════════════════════════════════════════════════════════
// FLOATING TEXT — Texto que asciende y desaparece
// ═══════════════════════════════════════════════════════════════════

public class FloatingText : MonoBehaviour
{
    public TMPro.TextMeshPro textMesh;
    public float riseSpeed = 1.5f;
    public float lifetime  = 1.2f;

    private float _elapsed = 0f;

    public void Setup(string text, Color color)
    {
        if (textMesh != null)
        {
            textMesh.text  = text;
            textMesh.color = color;
        }
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        // Fade out
        if (textMesh != null)
        {
            Color c = textMesh.color;
            c.a = 1f - (_elapsed / lifetime);
            textMesh.color = c;
        }

        if (_elapsed >= lifetime)
            Destroy(gameObject);
    }
}

// ═══════════════════════════════════════════════════════════════════
// AUDIO MANAGER — GDD sección 12
// ═══════════════════════════════════════════════════════════════════

public enum SoundType
{
    ItemPickup,     // Pop suave al tomar ítem
    ItemPlaced,     // Colocar en receptor válido
    OvenStart,      // Plancha caliente
    OvenReady,      // Timbre suave "ding"
    WaffleBurned,   // Alarma suave
    RecipeComplete, // Jingle de 2 notas
    DeliverySuccess,// Moneda + jingle ascendente
    DeliveryError,  // Buzzer suave
    CustomerHappy,  // "¡Mmm!" aprobación
    CustomerLeave,  // Puerta cerrándose
    InvalidAction,  // Click vacío
    GameOver,       // Stinger descendente
    TimeUp          // Jingle alegre
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [System.Serializable]
    public class SoundEntry
    {
        public SoundType type;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Efectos de Sonido")]
    public SoundEntry[] sounds;

    [Header("Música de Fondo")]
    public AudioSource musicSource;
    public AudioClip   musicGameplay;
    public AudioClip   musicMenu;

    private AudioSource _sfxSource;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _sfxSource = gameObject.AddComponent<AudioSource>();
    }

    public void PlaySound(SoundType type)
    {
        foreach (var entry in sounds)
        {
            if (entry.type == type && entry.clip != null)
            {
                _sfxSource.PlayOneShot(entry.clip, entry.volume);
                return;
            }
        }
        // Si no hay clip asignado, log silencioso (no rompe el prototipo)
        Debug.Log($"[Audio] Sound '{type}' — clip no asignado aún");
    }

    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;
        if (musicSource.clip == clip) return;

        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void SetMusicVolume(float v)  => musicSource.volume = v;
    public void SetSfxVolume(float v)    => _sfxSource.volume  = v;
}
