using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// WAFFLERA — GDD sección 4.3 y 4.4
///
/// Flujo visual completo mapeado sobre la ilustración:
///   1. Vacía  → wafflera cerrada, sin nada encima
///   2. Recibe mezcla → animación "cierre" (tapa baja), vapor sale por los lados
///   3. Cocinando → vapor continuo, barra de calor verde→amarillo→rojo
///   4. Lista → animación "apertura", destello dorado, waffle aparece encima
///   5. Quemada → humo negro, wafflera tiembla
///
/// ANIMACIONES PROCREATE — cómo exportar e integrar:
///   Cada estado necesita una hoja de sprites (sprite sheet) exportada desde Procreate:
///   - Exportar como PNG con fondo transparente
///   - Formato recomendado: 512×512 px por frame, todos en una fila horizontal
///   - En Unity: Texture Type = Sprite, Sprite Mode = Multiple, luego Sprite Editor para cortar
///   - Los nombres de los Animation Clips deben coincidir EXACTAMENTE con los
///     strings de los triggers definidos abajo (WaffleraClose, WaffleraOpen, etc.)
///
/// JERARQUÍA DEL PREFAB en Unity:
///   Wafflera (este script + Collider2D)
///   ├── Body          → SpriteRenderer — la wafflera base (ilustración estática)
///   ├── AnimatedLayer → Animator — capas de animación superpuestas
///   ├── SteamEffect   → ParticleSystem o SpriteRenderer animado — vapor
///   ├── SmokeEffect   → ParticleSystem — humo negro (quemado)
///   ├── ReadyGlow     → SpriteRenderer — destello dorado cuando está lista
///   └── CookingBarCanvas → Canvas (World Space)
///       └── Slider    → barra de progreso de calor
/// </summary>
public class Oven : MonoBehaviour, IItemReceiver
{
    // ─── Estados ──────────────────────────────────────────────────
    public enum OvenState { Empty, Closing, Cooking, Opening, Ready, Burned }

    // ─── Inspector: Configuración ─────────────────────────────────
    [Header("══ Configuración de cocción (GDD 4.3) ══")]
    [Tooltip("Segundos hasta que el waffle está listo — GDD base: 6s")]
    public float cookingTime = 6f;
    [Tooltip("Ventana antes de quemarse tras estar listo — GDD: ~3s")]
    public float burnWindow  = 3f;

    [Header("══ Animaciones Procreate ══")]
    [Tooltip("Animator del GameObject hijo 'AnimatedLayer'")]
    public Animator waffleraAnimator;
    // Triggers que deben existir en el Animator Controller:
    // - WaffleraClose  → animación de tapa bajando (al recibir mezcla)
    // - WaffleraOpen   → animación de tapa subiendo (waffle listo)
    // - WaffleraShake  → sacudida breve (quemado o error)
    // - WaffleraIdle   → loop de idle vacía
    [Tooltip("Duración en segundos de la animación de cierre — sincronizar con Procreate")]
    public float closeAnimDuration = 0.5f;
    [Tooltip("Duración en segundos de la animación de apertura")]
    public float openAnimDuration  = 0.6f;

    [Header("══ Animaciones Helados (IceCreamSource) ══")]
    // Nota: Los IceCreamSource tienen su propio Animator.
    // Trigger esperado: "IceCreamSelect" — animación de sacar la bola del sabor.
    // Esta sección es solo documentación; la lógica está en IceCreamSource.cs

    [Header("══ Efectos visuales ══")]
    public GameObject steamEffect;      // Vapor durante cocción — ParticleSystem o animación
    public GameObject smokeEffect;      // Humo negro al quemarse
    public GameObject readyGlow;        // Destello dorado cuando listo
    [Tooltip("SpriteRenderer encima de la wafflera que muestra el waffle resultante")]
    public SpriteRenderer waffleDisplay;
    public Sprite spriteWaffleReady;    // Waffle dorado listo (exportado de Procreate)
    public Sprite spriteWaffleBurned;   // Waffle negro quemado

    [Header("══ Barra de calor ══")]
    public Slider    cookingBar;
    public Image     cookingBarFill;
    public Color     colorCooking = new Color(0.2f, 0.8f, 0.2f);
    public Color     colorWarning = new Color(1f,   0.7f, 0f  );
    public Color     colorDanger  = new Color(0.9f, 0.2f, 0.1f);

    [Header("══ Slot de mejoras ══")]
    [Tooltip("Índice de esta wafflera (0 = primera, 1 = segunda comprada, etc.)")]
    public int ovenIndex = 0;
    [Tooltip("Desactivar en el Inspector — se activa cuando se compra la mejora")]
    public bool isUnlocked = true;
    public GameObject lockedOverlay;   // Visual de candado cuando no está comprada

    // ─── Estado interno ───────────────────────────────────────────
    private OvenState _state = OvenState.Empty;
    private float     _timer = 0f;
    private bool      _timerRunning = false;

    public OvenState State      => _state;
    public bool      IsEmpty    => _state == OvenState.Empty;
    public bool      IsUnlocked => isUnlocked;

    // ─────────────────────────────────────────────────────────────
    // CICLO DE VIDA
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        SetState(OvenState.Empty);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
        if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timer += Time.deltaTime;
        UpdateCookingBar();

        if (_state == OvenState.Cooking && _timer >= cookingTime)
            StartCoroutine(WaffleReadySequence());

        else if (_state == OvenState.Ready && _timer >= cookingTime + burnWindow)
            WaffleBurned();
    }

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        return isUnlocked
            && _state == OvenState.Empty
            && item.itemType == ItemType.WaffleMix;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;
        Destroy(item.gameObject);         // La mezcla "entra" a la wafflera
        StartCoroutine(StartCookingSequence());
    }

    // ─────────────────────────────────────────────────────────────
    // SECUENCIAS DE ANIMACIÓN
    // Las coroutines permiten sincronizar animación Procreate + lógica
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Al recibir la mezcla: animación de cierre → comienza cocción
    /// </summary>
    private IEnumerator StartCookingSequence()
    {
        SetState(OvenState.Closing);

        // Disparar animación de cierre (tapa baja)
        waffleraAnimator?.SetTrigger("WaffleraClose");
        AudioManager.Instance?.PlaySound(SoundType.OvenStart);

        // Esperar a que termine la animación de cierre
        yield return new WaitForSeconds(closeAnimDuration);

        // Ahora comienza la cocción real
        _timer = 0f;
        _timerRunning = true;
        SetState(OvenState.Cooking);

        if (cookingBar != null)
        {
            cookingBar.gameObject.SetActive(true);
            cookingBar.value = 0f;
        }

        if (steamEffect != null) steamEffect.SetActive(true);
    }

    /// <summary>
    /// Waffle listo: animación de apertura → waffle aparece encima
    /// </summary>
    private IEnumerator WaffleReadySequence()
    {
        // Evitar que se llame varias veces
        _timerRunning = false;
        SetState(OvenState.Opening);

        if (steamEffect != null) steamEffect.SetActive(false);

        // Animación de apertura (tapa sube)
        waffleraAnimator?.SetTrigger("WaffleraOpen");
        yield return new WaitForSeconds(openAnimDuration);

        // Waffle aparece encima de la wafflera
        if (waffleDisplay != null)
        {
            waffleDisplay.sprite  = spriteWaffleReady;
            waffleDisplay.enabled = true;
        }

        SetState(OvenState.Ready);
        // Reiniciar timer para la ventana de quemado
        _timerRunning = true;

        AudioManager.Instance?.PlaySound(SoundType.OvenReady);
        FeedbackManager.Instance?.ShowReadyGlow(transform.position);
        if (readyGlow != null) readyGlow.SetActive(true);
    }

    private void WaffleBurned()
    {
        _timerRunning = false;
        SetState(OvenState.Burned);

        if (waffleDisplay != null) waffleDisplay.sprite = spriteWaffleBurned;
        if (smokeEffect  != null) smokeEffect.SetActive(true);
        if (readyGlow    != null) readyGlow.SetActive(false);

        waffleraAnimator?.SetTrigger("WaffleraShake");

        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        FeedbackManager.Instance?.ShowBurnEffect(transform.position);
    }

    // ─────────────────────────────────────────────────────────────
    // EXTRACCIÓN — click sobre la wafflera cuando está lista/quemada
    // GDD 4.3: "hacer clic sobre el horno → waffle listo"
    // ─────────────────────────────────────────────────────────────

    void OnMouseDown()
    {
        if (_state == OvenState.Ready)  ExtractWaffle(ItemType.WaffleReady);
        else if (_state == OvenState.Burned) ExtractWaffle(ItemType.WaffleBurned);
    }

    private void ExtractWaffle(ItemType type)
    {
        _timerRunning = false;

        // Ocultar display del waffle sobre la wafflera
        if (waffleDisplay != null) waffleDisplay.enabled = false;
        if (smokeEffect   != null) smokeEffect.SetActive(false);
        if (readyGlow     != null) readyGlow.SetActive(false);

        // Instanciar el ítem draggable encima de la wafflera
        DraggableItem waffle = ItemSpawner.Instance.SpawnItem(type, transform.position + Vector3.up * 0.5f);
        if (waffle != null)
            DragManager.Instance?.OnItemPickedUp(waffle);

        // Restaurar wafflera a estado vacío
        SetState(OvenState.Empty);
        waffleraAnimator?.SetTrigger("WaffleraIdle");

        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // MEJORAS — llamado desde UpgradeManager al comprar horno extra
    // ─────────────────────────────────────────────────────────────

    public void Unlock()
    {
        isUnlocked = true;
        if (lockedOverlay != null) lockedOverlay.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // VISUAL — barra de calor y estados
    // ─────────────────────────────────────────────────────────────

    private void UpdateCookingBar()
    {
        if (cookingBar == null) return;

        float progress;
        Color  barColor;

        if (_state == OvenState.Cooking)
        {
            progress = _timer / cookingTime;
            barColor = Color.Lerp(colorCooking, colorWarning, progress);
        }
        else // Ready — barra decrece hasta quemarse
        {
            float burnRatio = (_timer - cookingTime) / burnWindow;
            progress = 1f - burnRatio;
            barColor = Color.Lerp(colorWarning, colorDanger, burnRatio);
        }

        cookingBar.value = Mathf.Clamp01(progress);
        if (cookingBarFill != null) cookingBarFill.color = barColor;
    }

    private void SetState(OvenState newState)
    {
        _state = newState;
    }
}
