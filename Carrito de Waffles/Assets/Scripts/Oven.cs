using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// WAFFLERA v2 — AnimatorController con parámetros y Animation Events
///
/// ═══════════════════════════════════════════════════════════════════
/// FLUJO DE ESTADOS DEL ANIMATOR CONTROLLER
/// ═══════════════════════════════════════════════════════════════════
///
///   [Any State] ──(Trigger: DoClose)──► WaffleraClose
///                                              │
///                                    (Has Exit Time = true)
///                                              │
///                                              ▼
///                                       WaffleraCooking  ◄─── loop
///                                              │
///                                    (Trigger: DoShake)
///                                              │
///                                              ▼
///                                        WaffleraShake
///                                              │
///                                    (Has Exit Time = true)
///                                              │
///                                              ▼
///                                        WaffleraOpen
///                                              │
///                                    (Has Exit Time = true)
///                                              │
///                                              ▼
///                                   [Animation Event en frame X]
///                                   → llama OnWaffleReveal()
///                                              │
///                                    (Trigger: DoIdle)
///                                              │
///                                              ▼
///                                        WaffleraIdle  ◄─── loop (estado por defecto)
///
/// ═══════════════════════════════════════════════════════════════════
/// PARÁMETROS DEL ANIMATOR CONTROLLER
/// ═══════════════════════════════════════════════════════════════════
///
///   Nombre          Tipo      Descripción
///   ──────────────  ────────  ───────────────────────────────────────
///   DoClose         Trigger   Inicia cierre. Solo activable desde código
///                             cuando _state == Empty Y se recibe WaffleMix.
///   DoShake         Trigger   Inicia sacudida. Solo activable cuando
///                             _state == Cooking Y _timer >= cookingTime.
///   DoIdle          Trigger   Regresa a idle. Activado al extraer el waffle
///                             o al resetear el horno.
///   IsCooking       Bool      true mientras _state == Cooking.
///                             La transición WaffleraCooking → sí misma usa
///                             este bool para mantener el loop.
///
/// ═══════════════════════════════════════════════════════════════════
/// TRANSICIONES — configuración exacta en Unity
/// ═══════════════════════════════════════════════════════════════════
///
///   WaffleraIdle → WaffleraClose
///     Condition : DoClose (Trigger)
///     Has Exit Time : FALSE
///     Transition Duration : 0
///
///   WaffleraClose → WaffleraCooking
///     Condition : (ninguna)
///     Has Exit Time : TRUE  → Exit Time = 1.0 (al terminar el clip)
///     Transition Duration : 0
///
///   WaffleraCooking → WaffleraShake
///     Condition : DoShake (Trigger)
///     Has Exit Time : FALSE
///     Transition Duration : 0
///
///   WaffleraShake → WaffleraOpen
///     Condition : (ninguna)
///     Has Exit Time : TRUE  → Exit Time = 1.0
///     Transition Duration : 0
///
///   WaffleraOpen → WaffleraIdle
///     Condition : DoIdle (Trigger)
///     Has Exit Time : FALSE   ← DoIdle se dispara desde OnWaffleReveal()
///     Transition Duration : 0.1
///     (Alternativa: Has Exit Time TRUE al final del clip si no se usa Animation Event para la transición)
///
/// ═══════════════════════════════════════════════════════════════════
/// ANIMATION EVENT — WaffleraOpen.anim
/// ═══════════════════════════════════════════════════════════════════
///
///   1. Seleccionar WaffleraOpen.anim en el Project.
///   2. Abrir la ventana Animation (Window → Animation → Animation).
///   3. Con el clip seleccionado, buscar el frame donde la tapa
///      está completamente abierta (ej. frame 8 de 12).
///   4. Hacer click derecho sobre la línea de tiempo → Add Animation Event.
///   5. En el campo "Function" escribir exactamente: OnWaffleReveal
///      (debe coincidir con el método público de abajo).
///   6. No agregar parámetros adicionales al evento.
///   7. El frame puede moverse libremente sin tocar el código.
///
///   IMPORTANTE: el GameObject que contiene este Animator debe tener
///   una referencia al Oven padre, O el Animator debe estar en el
///   mismo GameObject que Oven. Si el Animator está en un hijo
///   (AnimatedLayer), usar el campo animatorEventRelay abajo.
///
/// JERARQUÍA DEL PREFAB (actualizada):
///   Oven  (este script + Collider2D)
///   ├── Body              → SpriteRenderer — wafflera estática
///   ├── AnimatedLayer     → Animator — recibe los triggers
///   │   └── [OvenAnimatorEventRelay adjunto aquí]
///   ├── WaffleDisplay     → SpriteRenderer — OCULTO al inicio
///   ├── SteamEffect       → ParticleSystem
///   ├── SmokeEffect       → ParticleSystem
///   ├── ReadyGlow         → SpriteRenderer / Light2D
///   └── CookingBarCanvas  → Canvas (World Space)
///       └── Slider
/// </summary>
public class Oven : MonoBehaviour, IItemReceiver
{
    // ─── Estados internos ─────────────────────────────────────────
    public enum OvenState { Empty, Closing, Cooking, Shaking, Opening, Ready, Overcooked, Burned }

    // ═══════════════════════════════════════════════════════════════
    // INSPECTOR
    // ═══════════════════════════════════════════════════════════════

    [Header("══ Configuración de cocción (GDD 4.3) ══")]
    [Tooltip("Segundos hasta que el waffle está listo — GDD base: 6s")]
    public float cookingTime = 6f;
    [Tooltip("Ventana perfecta en segundos — waffle en su punto óptimo")]
    public float perfectWindow = 3f;
    [Tooltip("Ventana pasado en segundos — waffle aceptable pero no ideal")]
    public float overcookedWindow = 3f;

    [Header("══ Animator ══")]
    [Tooltip("Animator del hijo 'AnimatedLayer'. Debe tener los parámetros definidos en este script.")]
    public Animator waffleraAnimator;

    [Tooltip(
        "Si el Animator está en un hijo separado (AnimatedLayer), arrastra aquí ese hijo.\n" +
        "El OvenAnimatorEventRelay adjunto en ese hijo reenviará el Animation Event a este Oven.\n" +
        "Si el Animator está en el mismo GameObject que Oven, dejar en null.")]
    public OvenAnimatorEventRelay animatorEventRelay;

    [Tooltip("Duración del clip WaffleraClose en segundos — solo se usa como fallback si Exit Time falla")]
    public float closeAnimDuration = 0.5f;
    [Tooltip("Duración del clip WaffleraOpen en segundos — referencia para depuración")]
    public float openAnimDuration = 0.6f;

    // ─────────────────────────────────────────────────────────────
    // Nombres exactos de los parámetros del Animator Controller.
    // Si los cambias aquí, cámbialos también en el Animator Controller.
    // ─────────────────────────────────────────────────────────────
    private const string PARAM_DO_CLOSE = "DoClose";   // Trigger
    private const string PARAM_DO_SHAKE = "DoShake";   // Trigger
    private const string PARAM_DO_IDLE = "DoIdle";    // Trigger
    private const string PARAM_IS_COOKING = "IsCooking"; // Bool

    [Header("══ Efectos visuales ══")]
    [Tooltip("SpriteRenderer encima de la wafflera — DESACTIVAR en el Inspector al inicio")]
    public SpriteRenderer waffleDisplay;
    public Sprite spriteWaffleReady;
    public Sprite spriteWaffleOvercooked;
    public Sprite spriteWaffleBurned;


    [Header("══ FX Prefabs ══")]
    public GameObject steamFXPrefab;
    public GameObject smokeFXPrefab;

    private GameObject _steamInstance;
    private GameObject _smokeInstance;
    public GameObject readyGlow;

    [Header("══ Barra de calor ══")]
    public Slider cookingBar;
    public Image cookingBarFill;
    public Color colorCooking = new Color(0.2f, 0.8f, 0.2f);
    public Color colorWarning = new Color(1.0f, 0.7f, 0.0f);
    public Color colorOvercooked = new Color(0.9f, 0.45f, 0.0f);
    public Color colorDanger = new Color(0.9f, 0.2f, 0.1f);

    [Header("══ Slot de mejoras ══")]
    public int ovenIndex = 0;
    public bool isUnlocked = true;
    public GameObject lockedOverlay;

    // ═══════════════════════════════════════════════════════════════
    // ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════

    private OvenState _state = OvenState.Empty;
    private float _timer = 0f;
    private bool _timerRunning = false;

    // Flag que evita disparar DoShake más de una vez por ciclo
    private bool _shakeDispatched = false;

    public OvenState State => _state;
    public bool IsEmpty => _state == OvenState.Empty;
    public bool IsUnlocked => isUnlocked;

    private float OvercookedStart => cookingTime + perfectWindow;
    private float BurnedStart => cookingTime + perfectWindow + overcookedWindow;

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        // Instanciar y dejar inactivos desde el inicio
        if (steamFXPrefab != null)
        {
            _steamInstance = Instantiate(steamFXPrefab, transform.position, Quaternion.identity, transform);
            _steamInstance.SetActive(false);
        }
        if (smokeFXPrefab != null)
        {
            _smokeInstance = Instantiate(smokeFXPrefab, transform.position, Quaternion.identity, transform);
            _smokeInstance.SetActive(false);
        }

        // Registrar este Oven en el relay si está en un hijo separado
        if (animatorEventRelay != null)
            animatorEventRelay.SetOwner(this);

        SetState(OvenState.Empty);

        // Asegurarse de que el display del waffle está oculto al inicio
        if (waffleDisplay != null) waffleDisplay.enabled = false;
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
        if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timer += Time.deltaTime;
        UpdateCookingBar();

        // ── Cooking → disparar Shake cuando el waffle está listo ──
        if (_state == OvenState.Cooking && _timer >= cookingTime && !_shakeDispatched)
        {
            _shakeDispatched = true;
            StartCoroutine(WaffleReadySequence());
        }

        // ── Ready → Overcooked ────────────────────────────────────
        else if (_state == OvenState.Ready && _timer >= OvercookedStart)
        {
            WaffleOvercooked();
        }

        // ── Overcooked → Burned ───────────────────────────────────
        else if (_state == OvenState.Overcooked && _timer >= BurnedStart)
        {
            WaffleBurned();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // IItemReceiver
    // ═══════════════════════════════════════════════════════════════

    public bool CanReceive(DraggableItem item)
    {
        // Solo acepta mezcla cuando está vacía y desbloqueada
        return isUnlocked
            && _state == OvenState.Empty
            && item.itemType == ItemType.WaffleMix;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;
        Destroy(item.gameObject);
        StartCoroutine(StartCookingSequence());
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIAS DE ANIMACIÓN
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Recibe mezcla → dispara DoClose → espera a que el AnimatorController
    /// transite automáticamente a WaffleraCooking (via Exit Time).
    /// </summary>
    private IEnumerator StartCookingSequence()
    {
        SetState(OvenState.Closing);
        _shakeDispatched = false;

        // ── Disparar animación de cierre ──────────────────────────
        // El AnimatorController transita WaffleraClose → WaffleraCooking
        // automáticamente por Exit Time = 1.0. No hace falta otro trigger.
        SetAnimatorBool(PARAM_IS_COOKING, false);
        SetAnimatorTrigger(PARAM_DO_CLOSE);

        AudioManager.Instance?.PlaySound(SoundType.OvenStart);

        // Esperar a que termine WaffleraClose (Exit Time maneja la transición,
        // pero necesitamos saber cuándo empieza realmente la cocción).
        yield return new WaitForSeconds(closeAnimDuration);

        // ── Comienza cocción ──────────────────────────────────────
        _timer = 0f;
        _timerRunning = true;
        SetState(OvenState.Cooking);
        SetAnimatorBool(PARAM_IS_COOKING, true);

        if (cookingBar != null) { cookingBar.gameObject.SetActive(true); cookingBar.value = 0f; }
        if (_steamInstance != null) _steamInstance.SetActive(true);
    }

    /// <summary>
    /// Waffle listo → dispara DoShake → el AnimatorController transita
    /// automáticamente Shake → Open (Exit Time). El Animation Event en
    /// WaffleraOpen llama a OnWaffleReveal() en el frame exacto configurado.
    /// </summary>
    private IEnumerator WaffleReadySequence()
    {
        _timerRunning = false;
        SetState(OvenState.Shaking);
        SetAnimatorBool(PARAM_IS_COOKING, false);

        if (_steamInstance != null) _steamInstance.SetActive(false);

        // Shake → Open ocurre automáticamente por Exit Time en el Animator
        SetAnimatorTrigger(PARAM_DO_SHAKE);

        AudioManager.Instance?.PlaySound(SoundType.OvenReady);
        FeedbackManager.Instance?.ShowReadyGlow(transform.position);

        // Actualizar estado de juego a Opening mientras la animación corre.
        // OnWaffleReveal() completará la transición a Ready.
        yield return null;
        SetState(OvenState.Opening);
    }

    // ═══════════════════════════════════════════════════════════════
    // ANIMATION EVENT — llamado desde WaffleraOpen.anim
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Este método es llamado por el Animation Event configurado en
    /// WaffleraOpen.anim, en el frame exacto donde la tapa está completamente
    /// abierta.
    ///
    /// Si el Animator está en un hijo (AnimatedLayer), el OvenAnimatorEventRelay
    /// recibe el evento y lo reenvía aquí.
    ///
    /// Si el Animator está en el mismo GameObject que Oven, este método
    /// recibe el evento directamente.
    ///
    /// ─── Cómo configurar el Animation Event en Unity ───────────
    ///   1. Seleccionar WaffleraOpen.anim en el Project.
    ///   2. Abrir Animation window (Window → Animation → Animation).
    ///   3. Con el clip seleccionado, ir al frame donde la tapa está abierta.
    ///   4. Click derecho en la línea de eventos (barra superior) →
    ///      Add Animation Event.
    ///   5. En el Inspector del evento, campo "Function": OnWaffleReveal
    ///   6. Sin parámetros adicionales.
    ///   7. Para mover el frame del evento: arrastrar el marcador blanco
    ///      en la línea de tiempo de eventos.
    /// </summary>
    public void OnWaffleReveal()
    {
        if (_state != OvenState.Opening) return;

        // Mostrar sprite del waffle cocinado
        if (waffleDisplay != null)
        {
            waffleDisplay.sprite = spriteWaffleReady;
            waffleDisplay.enabled = true;
        }

        if (readyGlow != null) readyGlow.SetActive(true);

        // Transitar a idle visual (el estado lógico pasa a Ready)
        SetAnimatorTrigger(PARAM_DO_IDLE);

        // Ahora el timer corre para detectar si pasa a Overcooked
        SetState(OvenState.Ready);
        _timerRunning = true;
    }

    // ═══════════════════════════════════════════════════════════════
    // ESTADOS DE DEGRADACIÓN
    // ═══════════════════════════════════════════════════════════════

    private void WaffleOvercooked()
    {
        SetState(OvenState.Overcooked);

        if (waffleDisplay != null) waffleDisplay.sprite = spriteWaffleOvercooked;
        if (readyGlow != null) readyGlow.SetActive(false);

        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
    }

    private void WaffleBurned()
    {
        _timerRunning = false;
        SetState(OvenState.Burned);

        if (waffleDisplay != null) waffleDisplay.sprite = spriteWaffleBurned;
        if (_smokeInstance != null) _smokeInstance.SetActive(true);
        if (readyGlow != null) readyGlow.SetActive(false);

        // WaffleraShake en quemado es cosmético — no afecta la lógica
        // de transición del Animator (ya está en Idle desde DoIdle anterior)
        SetAnimatorTrigger(PARAM_DO_SHAKE);

        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        FeedbackManager.Instance?.ShowBurnEffect(transform.position);
    }

    // ═══════════════════════════════════════════════════════════════
    // EXTRACCIÓN — click sobre la wafflera cuando está lista
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        switch (_state)
        {
            case OvenState.Ready: ExtractWaffle(ItemType.WaffleReady); break;
            case OvenState.Overcooked: ExtractWaffle(ItemType.WaffleOvercooked); break;
            case OvenState.Burned: ExtractWaffle(ItemType.WaffleBurned); break;
        }
    }

    private void ExtractWaffle(ItemType type)
    {
        _timerRunning = false;

        if (waffleDisplay != null) waffleDisplay.enabled = false;
        if (_smokeInstance != null) _smokeInstance.SetActive(false);
        if (readyGlow != null) readyGlow.SetActive(false);

        DraggableItem waffle = ItemSpawner.Instance.SpawnItem(
            type, transform.position + Vector3.up * 0.5f);
        if (waffle != null)
            DragManager.Instance?.OnItemPickedUp(waffle);

        SetState(OvenState.Empty);
        SetAnimatorTrigger(PARAM_DO_IDLE);
        SetAnimatorBool(PARAM_IS_COOKING, false);

        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════════════
    // MEJORAS
    // ═══════════════════════════════════════════════════════════════

    public void Unlock()
    {
        isUnlocked = true;
        if (lockedOverlay != null) lockedOverlay.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════════════
    // BARRA DE CALOR
    // ═══════════════════════════════════════════════════════════════

    private void UpdateCookingBar()
    {
        if (cookingBar == null) return;

        float progress;
        Color barColor;

        if (_state == OvenState.Cooking)
        {
            progress = _timer / cookingTime;
            barColor = Color.Lerp(colorCooking, colorWarning, progress);
        }
        else if (_state == OvenState.Ready)
        {
            float t = (_timer - cookingTime) / perfectWindow;
            progress = 1f;
            barColor = Color.Lerp(colorWarning, colorOvercooked, t);
        }
        else
        {
            float t = (_timer - OvercookedStart) / overcookedWindow;
            progress = 1f - t;
            barColor = Color.Lerp(colorOvercooked, colorDanger, t);
        }

        cookingBar.value = Mathf.Clamp01(progress);
        if (cookingBarFill != null) cookingBarFill.color = barColor;
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS — wrappers seguros para el Animator
    // ═══════════════════════════════════════════════════════════════

    private void SetAnimatorTrigger(string paramName)
    {
        if (waffleraAnimator != null)
            waffleraAnimator.SetTrigger(paramName);
    }

    private void SetAnimatorBool(string paramName, bool value)
    {
        if (waffleraAnimator != null)
            waffleraAnimator.SetBool(paramName, value);
    }

    private void SetState(OvenState newState)
    {
        _state = newState;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// OVEN ANIMATOR EVENT RELAY
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Componente puente para cuando el Animator vive en un hijo separado
/// (AnimatedLayer) y los Animation Events no pueden alcanzar directamente
/// el Oven del GameObject padre.
///
/// ─── Setup ─────────────────────────────────────────────────────────
///   1. Añadir este componente al mismo GameObject que tiene el Animator
///      (el hijo "AnimatedLayer").
///   2. En el Oven (padre), arrastrar ese hijo al campo "Animator Event Relay".
///      El Oven llamará SetOwner(this) automáticamente en Awake.
///   3. El Animation Event en WaffleraOpen.anim apunta a la función
///      "OnWaffleReveal" — Unity la encontrará en este componente,
///      que la reenvía al Oven padre.
/// </summary>
public class OvenAnimatorEventRelay : MonoBehaviour
{
    private Oven _owner;

    /// <summary>Llamado por Oven.Awake() para registrar la referencia al padre.</summary>
    public void SetOwner(Oven owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Animation Event — configurar en WaffleraOpen.anim.
    /// Unity llama este método en el frame exacto del evento.
    /// </summary>
    public void OnWaffleReveal()
    {
        _owner?.OnWaffleReveal();
    }
}