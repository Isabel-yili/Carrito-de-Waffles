using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// DÍA → NOCHE — Background animado sincronizado con el timer de partida.
///
/// Cómo funciona:
///   El progreso de la partida (0 = inicio, 1 = fin) se mapea sobre un
///   gradiente de cielo configurable. En lugar de usar un Animator,
///   el script mezcla colores y sprites en tiempo real, lo que permite
///   pausar/reanudar correctamente y sincronizarlo con cualquier duración.
///
/// SETUP EN UNITY — jerarquía recomendada:
///   [BACKGROUND]
///   ├── Sky                  → SpriteRenderer (plano que cubre toda la pantalla)
///   │                          Sorting: Background · Order: 0
///   ├── Buildings_Day        → SpriteRenderer (fondo de día de Procreate)
///   │                          Sorting: Background · Order: 1
///   ├── Buildings_Sunset     → SpriteRenderer (variante atardecer, alpha 0 al inicio)
///   │                          Sorting: Background · Order: 2
///   ├── Buildings_Night      → SpriteRenderer (variante noche, alpha 0 al inicio)
///   │                          Sorting: Background · Order: 3
///   ├── Stars                → SpriteRenderer o ParticleSystem (oculto al inicio)
///   │                          Sorting: Background · Order: 4
///   └── SunMoon              → SpriteRenderer (el sol se mueve hacia abajo al atardecer)
///                              Sorting: Background · Order: 5
///
/// EXPORTAR DESDE PROCREATE:
///   Crear 3 versiones del fondo de calle: día, atardecer, noche.
///   Exportar cada una como PNG 1920×1080 con fondo transparente donde aplique.
///   El script hace el crossfade entre ellas automáticamente.
/// </summary>
public class DayNightBackground : MonoBehaviour
{
    // ─── Fases del día ────────────────────────────────────────────
    [System.Serializable]
    public class DayPhase
    {
        [Tooltip("En qué punto de la partida comienza esta fase (0=inicio, 1=fin)")]
        [Range(0f, 1f)] public float startProgress;
        public Color skyColor;
        [Tooltip("Velocidad de las nubes o elementos ambientales en esta fase")]
        public float ambientSpeed = 1f;
    }

    [Header("══ Fases del cielo ══")]
    [Tooltip("Definir al menos 3 fases: día, atardecer, noche")]
    public List<DayPhase> phases = new List<DayPhase>
    {
        new DayPhase { startProgress = 0f,   skyColor = new Color(0.53f, 0.81f, 0.98f), ambientSpeed = 1f },   // Día — azul cielo
        new DayPhase { startProgress = 0.5f, skyColor = new Color(0.98f, 0.60f, 0.30f), ambientSpeed = 0.8f }, // Atardecer — naranja
        new DayPhase { startProgress = 0.75f,skyColor = new Color(0.10f, 0.10f, 0.25f), ambientSpeed = 0.5f }, // Crepúsculo — azul oscuro
        new DayPhase { startProgress = 0.88f,skyColor = new Color(0.04f, 0.04f, 0.12f), ambientSpeed = 0.3f }, // Noche — negro azulado
    };

    [Header("══ Referencias de sprites ══")]
    public SpriteRenderer skyRenderer;         // Plano de color sólido del cielo
    public SpriteRenderer buildingsDay;        // Fondo de día (Procreate)
    public SpriteRenderer buildingsSunset;     // Fondo atardecer (Procreate)
    public SpriteRenderer buildingsNight;      // Fondo noche (Procreate)

    [Header("══ Elementos adicionales ══")]
    public SpriteRenderer sunMoonRenderer;     // Sol → Luna (mismo sprite o dos)
    public Sprite          spriteSun;
    public Sprite          spriteMoon;
    [Tooltip("Posición del sol al inicio (arriba del frame)")]
    public Vector3 sunStartPosition = new Vector3(3f, 4f, 0f);
    [Tooltip("Posición del sol al atardecer (bajo, esquina)")]
    public Vector3 sunSetPosition   = new Vector3(-4f, -1f, 0f);
    [Tooltip("Posición de la luna (aparece por el otro lado)")]
    public Vector3 moonRisePosition = new Vector3(4f, 3f, 0f);

    public GameObject starsObject;             // Estrellas (partículas o sprite)

    [Header("══ Lámparas del carrito ══")]
    [Tooltip("Las lámparas de la ilustración se encienden al anochecer")]
    public List<SpriteRenderer> cartLamps;
    public Sprite lampOff;
    public Sprite lampOn;
    [Tooltip("En qué progreso de partida se encienden las lámparas")]
    [Range(0f, 1f)] public float lampsOnAtProgress = 0.7f;
    private bool _lampsOn = false;

    // ─── Estado interno ───────────────────────────────────────────
    private GameManager _gm;
    private float _lastProgress = -1f;

    void Start()
    {
        _gm = GameManager.Instance;
        ApplyProgress(0f); // Estado inicial: pleno día
    }

    void Update()
    {
        if (_gm == null || !_gm.IsGameRunning) return;

        float progress = 1f - (_gm.TimeLeft / _gm.GameDuration);
        progress = Mathf.Clamp01(progress);

        // Solo actualizar si cambió lo suficiente (optimización)
        if (Mathf.Abs(progress - _lastProgress) < 0.001f) return;
        _lastProgress = progress;

        ApplyProgress(progress);
    }

    // ─────────────────────────────────────────────────────────────
    // NÚCLEO — aplica el estado visual para un progreso dado
    // ─────────────────────────────────────────────────────────────

    private void ApplyProgress(float progress)
    {
        // 1. Color del cielo (interpolación entre fases)
        if (skyRenderer != null)
            skyRenderer.color = EvaluateSkyColor(progress);

        // 2. Crossfade entre versiones del fondo de edificios
        UpdateBuildingsFade(progress);

        // 3. Movimiento del sol / aparición de la luna
        UpdateSunMoon(progress);

        // 4. Estrellas
        UpdateStars(progress);

        // 5. Lámparas del carrito
        UpdateLamps(progress);
    }

    // ─────────────────────────────────────────────────────────────
    // GRADIENTE DE CIELO
    // ─────────────────────────────────────────────────────────────

    private Color EvaluateSkyColor(float progress)
    {
        if (phases == null || phases.Count == 0) return Color.white;
        if (phases.Count == 1) return phases[0].skyColor;

        // Encontrar entre qué dos fases estamos
        for (int i = 0; i < phases.Count - 1; i++)
        {
            float a = phases[i].startProgress;
            float b = phases[i + 1].startProgress;

            if (progress >= a && progress < b)
            {
                float t = Mathf.InverseLerp(a, b, progress);
                t = Mathf.SmoothStep(0, 1, t); // Suavizar la transición
                return Color.Lerp(phases[i].skyColor, phases[i + 1].skyColor, t);
            }
        }

        return phases[phases.Count - 1].skyColor;
    }

    // ─────────────────────────────────────────────────────────────
    // CROSSFADE DE FONDOS — día → atardecer → noche
    // ─────────────────────────────────────────────────────────────

    private void UpdateBuildingsFade(float progress)
    {
        // Día:      0.0 → 0.5   alpha: day=1, sunset=0, night=0
        // Atardecer:0.5 → 0.75  alpha: day↓,  sunset↑,  night=0
        // Noche:    0.75→ 1.0   alpha: day=0, sunset↓,  night↑

        float dayAlpha    = 1f;
        float sunsetAlpha = 0f;
        float nightAlpha  = 0f;

        if (progress < 0.5f)
        {
            dayAlpha = 1f;
        }
        else if (progress < 0.75f)
        {
            float t = Mathf.InverseLerp(0.5f, 0.75f, progress);
            t = Mathf.SmoothStep(0, 1, t);
            dayAlpha    = 1f - t;
            sunsetAlpha = t;
        }
        else
        {
            float t = Mathf.InverseLerp(0.75f, 1f, progress);
            t = Mathf.SmoothStep(0, 1, t);
            sunsetAlpha = 1f - t;
            nightAlpha  = t;
        }

        SetAlpha(buildingsDay,    dayAlpha);
        SetAlpha(buildingsSunset, sunsetAlpha);
        SetAlpha(buildingsNight,  nightAlpha);
    }

    // ─────────────────────────────────────────────────────────────
    // SOL Y LUNA
    // ─────────────────────────────────────────────────────────────

    private void UpdateSunMoon(float progress)
    {
        if (sunMoonRenderer == null) return;

        if (progress < 0.6f)
        {
            // Sol moviéndose hacia abajo
            float t = Mathf.InverseLerp(0f, 0.6f, progress);
            sunMoonRenderer.transform.position = Vector3.Lerp(sunStartPosition, sunSetPosition, Mathf.SmoothStep(0, 1, t));
            sunMoonRenderer.sprite = spriteSun;
            SetAlpha(sunMoonRenderer, 1f);
        }
        else if (progress < 0.7f)
        {
            // Transición sol → luna (fade out del sol)
            float t = Mathf.InverseLerp(0.6f, 0.7f, progress);
            SetAlpha(sunMoonRenderer, 1f - t);
        }
        else
        {
            // Luna emergiendo
            float t = Mathf.InverseLerp(0.7f, 0.85f, progress);
            sunMoonRenderer.transform.position = Vector3.Lerp(sunSetPosition, moonRisePosition, Mathf.SmoothStep(0, 1, t));
            if (spriteMoon != null) sunMoonRenderer.sprite = spriteMoon;
            SetAlpha(sunMoonRenderer, t);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ESTRELLAS
    // ─────────────────────────────────────────────────────────────

    private void UpdateStars(float progress)
    {
        if (starsObject == null) return;

        bool shouldShow = progress > 0.75f;

        if (!starsObject.activeSelf && shouldShow)
            starsObject.SetActive(true);

        // Fade in de las estrellas
        SpriteRenderer sr = starsObject.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            float starAlpha = Mathf.InverseLerp(0.75f, 0.95f, progress);
            SetAlpha(sr, starAlpha);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LÁMPARAS DEL CARRITO
    // ─────────────────────────────────────────────────────────────

    private void UpdateLamps(float progress)
    {
        if (_lampsOn || cartLamps == null) return;
        if (progress < lampsOnAtProgress) return;

        _lampsOn = true;
        foreach (var lamp in cartLamps)
        {
            if (lamp != null && lampOn != null)
            {
                lamp.sprite = lampOn;
                // Pequeño "parpadeo" al encenderse
                StartCoroutine(LampFlicker(lamp));
            }
        }

        AudioManager.Instance?.PlaySound(SoundType.ButtonClick); // Sonido de clic eléctrico
    }

    private System.Collections.IEnumerator LampFlicker(SpriteRenderer lamp)
    {
        for (int i = 0; i < 3; i++)
        {
            SetAlpha(lamp, 0.2f);
            yield return new WaitForSeconds(0.08f);
            SetAlpha(lamp, 1f);
            yield return new WaitForSeconds(0.06f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // HELPER
    // ─────────────────────────────────────────────────────────────

    private void SetAlpha(SpriteRenderer sr, float alpha)
    {
        if (sr == null) return;
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }
}
