using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// WAFFLERA v3 — Flujo de waffle como objeto independiente arrastrable.
///
/// ═══════════════════════════════════════════════════════════════════
/// CAMBIOS RESPECTO A v2
/// ═══════════════════════════════════════════════════════════════════
///
///   ExtractWaffle():
///     - Spawna el waffle como DraggableItem en la escena.
///     - Registra este Oven como "_originOven" del waffle via SetOriginOven().
///     - El horno queda en estado "WaffleExtracted" (visual abierto, en espera).
///
///   ReturnWaffle(DraggableItem):
///     - Llamado por DraggableItem.ReturnToOrigin() si el drop falla.
///     - Restaura el sprite del waffle en el WaffleDisplay del horno.
///     - Destruye el GameObject del waffle extraído.
///     - Vuelve al estado Ready/Overcooked/Burned correspondiente y
///       reactiva el timer si hay ventana de tiempo restante.
///
/// ═══════════════════════════════════════════════════════════════════
/// FLUJO DE ESTADOS DEL ANIMATOR CONTROLLER
/// ═══════════════════════════════════════════════════════════════════
///
///   [Any State] ──(Trigger: DoClose)──► WaffleraClose
///                                              │  (Exit Time)
///                                              ▼
///                                       WaffleraCooking  ◄─── loop
///                                              │
///                                    (Trigger: DoShake)
///                                              ▼
///                                        WaffleraShake
///                                              │  (Exit Time)
///                                              ▼
///                                        WaffleraOpen
///                                              │
///                                   [Animation Event → OnWaffleReveal]
///                                    (Trigger: DoIdle)
///                                              ▼
///                                        WaffleraIdle  ◄─── loop (estado por defecto)
///
/// ═══════════════════════════════════════════════════════════════════
/// PARÁMETROS DEL ANIMATOR CONTROLLER
/// ═══════════════════════════════════════════════════════════════════
///
///   Nombre        Tipo      Descripción
///   ────────────  ────────  ──────────────────────────────────────────
///   DoClose       Trigger   Inicia cierre al recibir WaffleMix.
///   DoShake       Trigger   Sacudida cuando el waffle está listo.
///   DoIdle        Trigger   Regresa a idle tras revelar el waffle.
///   IsCooking     Bool      true mientras _state == Cooking.
///
/// ═══════════════════════════════════════════════════════════════════
/// ANIMATION EVENT — WaffleraOpen.anim
/// ═══════════════════════════════════════════════════════════════════
///
///   Función: OnWaffleReveal
///   Frame: cuando la tapa está completamente abierta.
///   Si el Animator está en un hijo (AnimatedLayer), añadir
///   OvenAnimatorEventRelay en ese hijo y asignarlo en el Inspector.
///
/// JERARQUÍA DEL PREFAB:
///   Oven  (este script + Collider2D)
///   ├── Body              → SpriteRenderer — wafflera estática
///   ├── AnimatedLayer     → Animator — recibe los triggers
///   │   └── [OvenAnimatorEventRelay adjunto aquí]
///   ├── WaffleDisplay     → SpriteRenderer — DESACTIVADO al inicio
///   ├── SteamEffect       → ParticleSystem
///   ├── SmokeEffect       → ParticleSystem
///   ├── ReadyGlow         → SpriteRenderer / Light2D
///   └── CookingBarCanvas  → Canvas (World Space)
///       └── Slider
/// </summary>
public class Oven : MonoBehaviour, IItemReceiver
{
    // ─── Estados internos ─────────────────────────────────────────
    public enum OvenState
    {
        Empty,
        Closing,
        Cooking,
        Shaking,
        Opening,
        Ready,
        Overcooked,
        Burned,
        WaffleExtracted   // ← NUEVO: waffle en la escena, horno en espera
    }

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
    [Tooltip("Animator del hijo 'AnimatedLayer'.")]
    public Animator waffleraAnimator;
    [Tooltip("Relay para Animation Events desde un hijo separado. Dejar null si el Animator está en el mismo GO.")]
    public OvenAnimatorEventRelay animatorEventRelay;
    [Tooltip("Duración del clip WaffleraClose — fallback si Exit Time falla")]
    public float closeAnimDuration = 0.5f;
    [Tooltip("Duración del clip WaffleraOpen — referencia para el fallback")]
    public float openAnimDuration = 0.6f;

    private const string PARAM_DO_CLOSE = "DoClose";
    private const string PARAM_DO_SHAKE = "DoShake";
    private const string PARAM_DO_IDLE = "DoIdle";
    private const string PARAM_IS_COOKING = "IsCooking";

    [Header("══ Efectos visuales ══")]
    [Tooltip("SpriteRenderer del waffle sobre la wafflera — DESACTIVAR en el Inspector al inicio")]
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

    [Header("══ Configuración de extracción ══")]
    [Tooltip("Sorting Order del waffle mientras está siendo arrastrado")]
    public int waffleCarrySortingOrder = 50;

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
    private bool _shakeDispatched = false;

    // Estado del waffle en el momento de la extracción (para poder restaurarlo)
    private OvenState _extractedWaffleState = OvenState.Ready;

    // Posición local y padre originales del WaffleDisplay para re-parentizar correctamente
    private Vector3 _waffleDisplayLocalPos;
    private Transform _waffleDisplayOriginalParent;
    private int _waffleDisplayOriginalSortingOrder;

    // Referencia al waffle actualmente extraído (para evitar doble extracción)
    private DraggableItem _extractedWaffle;

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

        if (animatorEventRelay != null)
            animatorEventRelay.SetOwner(this);

        SetState(OvenState.Empty);

        if (waffleDisplay != null) waffleDisplay.enabled = false;
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
        if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timer += Time.deltaTime;
        UpdateCookingBar();

        // Cooking → Ready
        if (_state == OvenState.Cooking && _timer >= cookingTime && !_shakeDispatched)
        {
            _shakeDispatched = true;
            StartCoroutine(WaffleReadySequence());
        }
        // Ready → Overcooked
        else if (_state == OvenState.Ready && _timer >= OvercookedStart)
        {
            WaffleOvercooked();
        }
        // Overcooked → Burned
        else if (_state == OvenState.Overcooked && _timer >= BurnedStart)
        {
            WaffleBurned();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // IItemReceiver — recibe WaffleMix
    // ═══════════════════════════════════════════════════════════════

    public bool CanReceive(DraggableItem item)
    {
        return isUnlocked
            && _state == OvenState.Empty
            && item.itemType == ItemType.WaffleMix;
    }

    public void ReceiveItem(DraggableItem item)
    {
        Debug.Log($"[Oven] ReceiveItem — item: {item?.itemType} | CanReceive: {CanReceive(item)} | State: {_state}");
        if (!CanReceive(item)) return;
        Destroy(item.gameObject);
        StartCoroutine(StartCookingSequence());
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIAS DE ANIMACIÓN
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator StartCookingSequence()
    {
        SetState(OvenState.Closing);
        _shakeDispatched = false;

        SetAnimatorBool(PARAM_IS_COOKING, false);
        SetAnimatorTrigger(PARAM_DO_CLOSE);

        AudioManager.Instance?.PlaySound(SoundType.OvenStart);

        yield return new WaitForSeconds(closeAnimDuration);

        _timer = 0f;
        _timerRunning = true;
        SetState(OvenState.Cooking);
        SetAnimatorBool(PARAM_IS_COOKING, true);

        if (cookingBar != null) { cookingBar.gameObject.SetActive(true); cookingBar.value = 0f; }
        if (_steamInstance != null) _steamInstance.SetActive(true);
    }

    private IEnumerator WaffleReadySequence()
    {
        _timerRunning = false;
        SetState(OvenState.Shaking);
        SetAnimatorBool(PARAM_IS_COOKING, false);

        if (_steamInstance != null) _steamInstance.SetActive(false);

        SetAnimatorTrigger(PARAM_DO_SHAKE);
        AudioManager.Instance?.PlaySound(SoundType.OvenReady);
        FeedbackManager.Instance?.ShowReadyGlow(transform.position);

        SetState(OvenState.Opening);

        float shakeOpenDuration = openAnimDuration + 0.5f;
        yield return new WaitForSeconds(shakeOpenDuration);

        if (_state == OvenState.Opening)
            OnWaffleReveal();
    }

    // ═══════════════════════════════════════════════════════════════
    // ANIMATION EVENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Llamado por el Animation Event en WaffleraOpen.anim, o por el relay
    /// si el Animator vive en un hijo.
    /// </summary>
    public void OnWaffleReveal()
    {
        if (_state != OvenState.Opening) return;

        if (waffleDisplay != null)
        {
            waffleDisplay.sprite = spriteWaffleReady;
            waffleDisplay.enabled = true;
        }

        if (readyGlow != null) readyGlow.SetActive(true);

        SetAnimatorTrigger(PARAM_DO_IDLE);
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

        SetAnimatorTrigger(PARAM_DO_SHAKE);
        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        FeedbackManager.Instance?.ShowBurnEffect(transform.position);
    }

    // ═══════════════════════════════════════════════════════════════
    // EXTRACCIÓN — click sobre la wafflera cuando está lista
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        // Si el jugador lleva un ítem en el cursor, intentar recibir WaffleMix
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
        {
            DraggableItem held = DragManager.Instance.SelectedItem;
            if (held != null && CanReceive(held))
            {
                ReceiveItem(held);
                DragManager.Instance.OnItemReleased(held);
                return;
            }

            FeedbackManager.Instance?.ShowInvalidAction(transform.position);
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
            return;
        }

        // Sin ítem en cursor: extraer el waffle según el estado actual
        switch (_state)
        {
            case OvenState.Ready: ExtractWaffle(ItemType.WaffleReady); break;
            case OvenState.Overcooked: ExtractWaffle(ItemType.WaffleOvercooked); break;
            case OvenState.Burned: ExtractWaffle(ItemType.WaffleBurned); break;
        }
    }

    /// <summary>
    /// Extrae el waffle convirtiendo el WaffleDisplay existente en un DraggableItem.
    ///
    /// NO se instancia ningún prefab. El propio GameObject del WaffleDisplay
    /// recibe DraggableItem + Collider2D en runtime, se desparentiza del horno
    /// y pasa a seguir el cursor del jugador.
    ///
    /// Si el jugador suelta el waffle en un lugar inválido, DraggableItem.ReturnToOrigin()
    /// llama Oven.ReturnWaffle(), que re-parentiza el WaffleDisplay, quita los
    /// componentes añadidos y restaura el estado del horno.
    /// </summary>
    private void ExtractWaffle(ItemType waffleType)
    {
        if (_state == OvenState.WaffleExtracted) return;
        if (waffleDisplay == null)
        {
            Debug.LogError("[Oven] waffleDisplay no asignado en el Inspector.");
            return;
        }

        _timerRunning = false;
        _extractedWaffleState = _state;

        // Guardar jerarquia original del WaffleDisplay para restaurarla al volver
        _waffleDisplayOriginalParent = waffleDisplay.transform.parent;
        _waffleDisplayLocalPos = waffleDisplay.transform.localPosition;
        _waffleDisplayOriginalSortingOrder = waffleDisplay.sortingOrder;

        // Apagar efectos visuales mientras el waffle esta fuera
        if (_smokeInstance != null) _smokeInstance.SetActive(false);
        if (readyGlow != null) readyGlow.SetActive(false);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);

        // Desparentizar: el WaffleDisplay pasa a ser un objeto libre en la escena
        waffleDisplay.transform.SetParent(null, worldPositionStays: true);

        // Garantizar que tiene Collider2D trigger para la deteccion de receptores
        Collider2D col = waffleDisplay.GetComponent<Collider2D>();
        if (col == null)
        {
            var box = waffleDisplay.gameObject.AddComponent<BoxCollider2D>();
            box.size = Vector2.one * 0.8f;
            col = box;
        }
        col.isTrigger = true;

        // Añadir DraggableItem en runtime si el WaffleDisplay no lo tenia
        _extractedWaffle = waffleDisplay.GetComponent<DraggableItem>();
        if (_extractedWaffle == null)
            _extractedWaffle = waffleDisplay.gameObject.AddComponent<DraggableItem>();

        _extractedWaffle.itemType = waffleType;
        _extractedWaffle.isDraggable = true;
        _extractedWaffle.carrySortingOrder = waffleCarrySortingOrder;

        // Posicionar en el cursor del jugador
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 mouseWorld = cam.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y,
                            Mathf.Abs(cam.transform.position.z)));
            mouseWorld.z = 0f;
            waffleDisplay.transform.position = mouseWorld;
        }

        // Registrar este horno como origen para que ReturnToOrigin() nos llame de vuelta
        _extractedWaffle.SetOriginOven(this);

        // Entregar al cursor del jugador
        DragManager.Instance?.OnItemPickedUp(_extractedWaffle);

        SetState(OvenState.WaffleExtracted);
        Debug.Log($"[Oven] WaffleDisplay desparentizado y arrastrable: {waffleType}");
    }

    // ═══════════════════════════════════════════════════════════════
    // RETORNO DEL WAFFLE AL HORNO
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Llamado por DraggableItem.ReturnToOrigin() cuando el waffle no pudo
    /// ser entregado a ningún Plate válido.
    ///
    /// Re-parentiza el WaffleDisplay al horno, elimina los componentes añadidos
    /// en runtime (DraggableItem y Collider2D), restaura posición/sprite y
    /// reactiva el timer si todavía hay ventana de tiempo.
    /// </summary>
    public void ReturnWaffle(DraggableItem waffle)
    {
        if (waffle == null) return;
        if (waffleDisplay == null) return;

        Debug.Log($"[Oven] WaffleDisplay devuelto. Estado restaurado: {_extractedWaffleState}");

        // Restaurar sorting order visual
        waffleDisplay.sortingOrder = _waffleDisplayOriginalSortingOrder;
        waffleDisplay.transform.localScale = Vector3.one;

        // Re-parentizar el WaffleDisplay de vuelta al horno
        waffleDisplay.transform.SetParent(_waffleDisplayOriginalParent, worldPositionStays: false);
        waffleDisplay.transform.localPosition = _waffleDisplayLocalPos;

        // Restaurar sprite segun el estado en que estaba el waffle
        waffleDisplay.sprite = _extractedWaffleState switch
        {
            OvenState.Overcooked => spriteWaffleOvercooked,
            OvenState.Burned => spriteWaffleBurned,
            _ => spriteWaffleReady
        };
        waffleDisplay.enabled = true;

        // Restaurar efectos visuales
        if (_extractedWaffleState == OvenState.Burned)
        {
            if (_smokeInstance != null) _smokeInstance.SetActive(true);
        }
        else
        {
            if (readyGlow != null) readyGlow.SetActive(true);
        }

        SetState(_extractedWaffleState);

        // Reactivar timer si el waffle aun puede degradarse mas
        if (_state == OvenState.Ready || _state == OvenState.Overcooked)
        {
            _timerRunning = true;
            if (cookingBar != null) cookingBar.gameObject.SetActive(true);
        }

        _extractedWaffle = null;

        // Quitar el DraggableItem añadido en runtime un frame despues
        // (para no destruirlo mientras aun esta en su propia pila de llamadas)
        StartCoroutine(RemoveDraggableNextFrame(waffle));
    }

    private System.Collections.IEnumerator RemoveDraggableNextFrame(DraggableItem di)
    {
        yield return null;
        if (di != null)
            Destroy(di);
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
    // HELPERS
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
/// (AnimatedLayer) y los Animation Events no alcanzan el Oven del padre.
///
/// Setup:
///   1. Añadir al mismo GameObject que tiene el Animator (hijo AnimatedLayer).
///   2. En el Oven (padre), arrastrar ese hijo al campo "Animator Event Relay".
///   3. El Animation Event apunta a la función "OnWaffleReveal".
/// </summary>
public class OvenAnimatorEventRelay : MonoBehaviour
{
    private Oven _owner;

    public void SetOwner(Oven owner) => _owner = owner;

    /// <summary>Animation Event — configurar en WaffleraOpen.anim.</summary>
    public void OnWaffleReveal() => _owner?.OnWaffleReveal();
}