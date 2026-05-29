using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// WAFFLERA v4 — Arquitectura con nodo visual explícito.
///
/// PROBLEMA RESUELTO:
///   Las versiones anteriores usaban SetActiveWaffleChild() (busca hijos por nombre)
///   y GetComponentInChildren (devuelve cualquier SR, incluso ocultos), lo que
///   causaba que los sprites no cambiaran y la escala se corrompiera al cambiar
///   de parent entre el Oven local-space y el world-space.
///
/// NUEVA ARQUITECTURA — dos campos explícitos en Inspector:
///
///   waffleDisplay   → el GameObject raíz que se desparentiza y arrastra.
///                     Tiene Collider2D propio. NO necesita SpriteRenderer.
///
///   waffleRenderer  → el SpriteRenderer exacto que muestra el sprite del waffle.
///                     Puede estar en waffleDisplay o en un hijo directo.
///                     Se asigna manualmente en Inspector — nunca se busca dinámicamente.
///
/// TRANSFORM:
///   Al extraer: se guarda localScale/localPos/localRot DENTRO de la jerarquía,
///   luego se preserva lossyScale en world-space al desparentizar.
///   Al devolver: SetParent primero, luego se restauran los valores locales guardados.
///
/// SETUP EN UNITY:
///   1. waffleDisplay  → arrastrar el GameObject "WaffleDisplay" (el padre).
///   2. waffleRenderer → arrastrar el SpriteRenderer exacto que quieres que cambie.
///      (puede ser un hijo de waffleDisplay, p.ej. "Waffle_Ready")
///   3. Asignar spriteWaffleReady / spriteWaffleOvercooked / spriteWaffleBurned.
///   4. Los demás hijos de waffleDisplay que sean solo decorativos: quitar Collider2D.
///
/// JERARQUÍA DEL PREFAB:
///   Oven  (este script + Collider2D)
///   ├── Body              → SpriteRenderer — wafflera estática
///   ├── AnimatedLayer     → Animator
///   │   └── OvenAnimatorEventRelay
///   ├── WaffleDisplay     → Collider2D (trigger) — objeto raíz del drag
///   │   └── WaffleSprite  → SpriteRenderer — ← asignar a waffleRenderer
///   ├── SteamEffect       → ParticleSystem
///   ├── SmokeEffect       → ParticleSystem
///   ├── ReadyGlow         → SpriteRenderer / Light2D
///   └── CookingBarCanvas  → Canvas (World Space)
///       └── Slider
/// </summary>
public class Oven : MonoBehaviour, IItemReceiver
{
    // ─── Estados ──────────────────────────────────────────────────
    public enum OvenState
    {
        Empty, Closing, Cooking, Shaking, Opening,
        Ready, Overcooked, Burned, WaffleExtracted
    }

    // ═══════════════════════════════════════════════════════════
    // INSPECTOR
    // ═══════════════════════════════════════════════════════════

    [Header("══ Cocción ══")]
    public float cookingTime = 6f;
    public float perfectWindow = 3f;
    public float overcookedWindow = 3f;

    [Header("══ Animator ══")]
    public Animator waffleraAnimator;
    public OvenAnimatorEventRelay animatorEventRelay;
    public float closeAnimDuration = 0.5f;
    public float openAnimDuration = 0.6f;

    private const string PARAM_DO_CLOSE = "DoClose";
    private const string PARAM_DO_SHAKE = "DoShake";
    private const string PARAM_DO_IDLE = "DoIdle";
    private const string PARAM_IS_COOKING = "IsCooking";

    [Header("══ Waffle — objetos explícitos ══")]
    [Tooltip("GameObject raíz del waffle. Se desparentiza al extraer. Necesita Collider2D propio.")]
    public GameObject waffleDisplay;

    [Tooltip("SpriteRenderer EXACTO que muestra el sprite del waffle. Asignar manualmente — nunca se busca con GetComponent.")]
    public SpriteRenderer waffleRenderer;

    [Tooltip("Sprite cuando el waffle está listo (Ready)")]
    public Sprite spriteWaffleReady;
    [Tooltip("Sprite cuando el waffle está pasado (Overcooked)")]
    public Sprite spriteWaffleOvercooked;
    [Tooltip("Sprite cuando el waffle está quemado (Burned)")]
    public Sprite spriteWaffleBurned;

    [Header("══ FX ══")]
    public GameObject steamFXPrefab;
    public GameObject smokeFXPrefab;
    public GameObject readyGlow;

    [Header("══ Barra de calor ══")]
    public Slider cookingBar;
    public Image cookingBarFill;
    public Color colorCooking = new Color(0.2f, 0.8f, 0.2f);
    public Color colorWarning = new Color(1.0f, 0.7f, 0.0f);
    public Color colorOvercooked = new Color(0.9f, 0.45f, 0.0f);
    public Color colorDanger = new Color(0.9f, 0.2f, 0.1f);

    [Header("══ Extracción ══")]
    public int waffleCarrySortingOrder = 50;

    [Header("══ Mejoras ══")]
    public int ovenIndex = 0;
    public bool isUnlocked = true;
    public GameObject lockedOverlay;

    // ═══════════════════════════════════════════════════════════
    // ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════

    private OvenState _state = OvenState.Empty;
    private float _timer = 0f;
    private bool _timerRunning = false;
    private bool _shakeDispatched = false;
    private OvenState _extractedWaffleState = OvenState.Ready;

    private GameObject _steamInstance;
    private GameObject _smokeInstance;

    // Transform guardado DENTRO de la jerarquía original (antes de desparentizar)
    private Transform _waffleOriginalParent;
    private Vector3 _waffleLocalPos;
    private Vector3 _waffleLocalScale;
    private Quaternion _waffleLocalRot;

    private DraggableItem _extractedWaffle;

    public OvenState State => _state;
    public bool IsEmpty => _state == OvenState.Empty;
    public bool IsUnlocked => isUnlocked;

    private float OvercookedStart => cookingTime + perfectWindow;
    private float BurnedStart => cookingTime + perfectWindow + overcookedWindow;

    // ═══════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════

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

        // Validar referencias críticas
        if (waffleDisplay == null)
            Debug.LogError("[Oven] waffleDisplay no asignado en el Inspector.");
        if (waffleRenderer == null)
            Debug.LogError("[Oven] waffleRenderer no asignado en el Inspector. " +
                           "Arrastra el SpriteRenderer exacto del waffle.");

        // Garantizar que el Collider2D del WaffleDisplay es trigger
        if (waffleDisplay != null)
        {
            Collider2D col = waffleDisplay.GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
            else
                Debug.LogWarning("[Oven] WaffleDisplay no tiene Collider2D. Añade uno en el prefab.");

            // Los hijos de waffleDisplay NO deben tener Collider activo
            // (el único collider interactivo es el del raíz WaffleDisplay)
            foreach (Transform child in waffleDisplay.transform)
            {
                Collider2D childCol = child.GetComponent<Collider2D>();
                if (childCol != null) childCol.enabled = false;
            }
        }

        SetState(OvenState.Empty);
        if (waffleDisplay != null) waffleDisplay.SetActive(false);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
        if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timer += Time.deltaTime;
        UpdateCookingBar();

        if (_state == OvenState.Cooking && _timer >= cookingTime && !_shakeDispatched)
        {
            _shakeDispatched = true;
            StartCoroutine(WaffleReadySequence());
        }
        else if (_state == OvenState.Ready && _timer >= OvercookedStart)
        {
            WaffleOvercooked();
        }
        else if (_state == OvenState.Overcooked && _timer >= BurnedStart)
        {
            WaffleBurned();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // IItemReceiver
    // ═══════════════════════════════════════════════════════════

    public bool CanReceive(DraggableItem item)
    {
        return isUnlocked
            && _state == OvenState.Empty
            && item.itemType == ItemType.WaffleMix;
    }

    public void ReceiveItem(DraggableItem item)
    {
        Debug.Log($"[Oven] ReceiveItem — {item?.itemType} | State: {_state}");
        if (!CanReceive(item)) return;
        Destroy(item.gameObject);
        StartCoroutine(StartCookingSequence());
    }

    // ═══════════════════════════════════════════════════════════
    // SECUENCIAS
    // ═══════════════════════════════════════════════════════════

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
        yield return new WaitForSeconds(openAnimDuration + 0.5f);

        if (_state == OvenState.Opening)
            OnWaffleReveal();
    }

    // ═══════════════════════════════════════════════════════════
    // ANIMATION EVENT
    // ═══════════════════════════════════════════════════════════

    public void OnWaffleReveal()
    {
        if (_state != OvenState.Opening) return;
        ShowWaffle(spriteWaffleReady);
        ActivateReadyGlow();
        SetAnimatorTrigger(PARAM_DO_IDLE);
        SetState(OvenState.Ready);
        _timerRunning = true;
        Debug.Log("[Oven] OnWaffleReveal → Ready");
    }

    /// <summary>
    /// Activa el ReadyGlow y hace Play() en todos sus ParticleSystems.
    /// SetActive(true) no reinicia partículas automáticamente en Unity,
    /// por lo que es necesario llamar Play() explícitamente.
    /// </summary>
    private void ActivateReadyGlow()
    {
        if (readyGlow == null) return;
        readyGlow.SetActive(true);
        // Reproducir todos los ParticleSystems hijos
        foreach (var ps in readyGlow.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear();
            ps.Play();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // DEGRADACIÓN
    // ═══════════════════════════════════════════════════════════

    private void WaffleOvercooked()
    {
        SetState(OvenState.Overcooked);
        ShowWaffle(spriteWaffleOvercooked);
        if (readyGlow != null) readyGlow.SetActive(false);
        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        Debug.Log("[Oven] WaffleOvercooked");
    }

    private void WaffleBurned()
    {
        _timerRunning = false;
        SetState(OvenState.Burned);
        ShowWaffle(spriteWaffleBurned);
        if (_smokeInstance != null) _smokeInstance.SetActive(true);
        if (readyGlow != null) readyGlow.SetActive(false);
        SetAnimatorTrigger(PARAM_DO_SHAKE);
        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        FeedbackManager.Instance?.ShowBurnEffect(transform.position);
        Debug.Log("[Oven] WaffleBurned");
    }

    // ═══════════════════════════════════════════════════════════
    // EXTRACCIÓN
    // ═══════════════════════════════════════════════════════════

    // OnMouseDown() eliminado — DragManager.HandleWorldClick() llama
    // RequestExtract() después de detectar el Oven con OverlapPoint.

    public void RequestExtract()
    {
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
        {
            DraggableItem held = DragManager.Instance.SelectedItem;
            if (held != null && CanReceive(held))
            {
                ReceiveItem(held);
                DragManager.Instance.OnItemReleased(held);
            }
            else
            {
                FeedbackManager.Instance?.ShowInvalidAction(transform.position);
                AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
            }
            return;
        }

        switch (_state)
        {
            case OvenState.Ready: ExtractWaffle(ItemType.WaffleReady); break;
            case OvenState.Overcooked: ExtractWaffle(ItemType.WaffleOvercooked); break;
            case OvenState.Burned: ExtractWaffle(ItemType.WaffleBurned); break;
        }
    }

    private void ExtractWaffle(ItemType waffleType)
    {
        if (_state == OvenState.WaffleExtracted) return;
        if (waffleDisplay == null)
        {
            Debug.LogError("[Oven] ExtractWaffle: waffleDisplay es null.");
            return;
        }

        _timerRunning = false;
        _extractedWaffleState = _state;

        // ── 1. Guardar transform LOCAL completo (dentro de la jerarquía del Oven) ──
        // Guardamos los valores LOCALES porque son los que necesitamos restaurar.
        // Si guardáramos world-values, al hacer SetParent de vuelta los recalcularía
        // y quedarían corruptos si el Oven tiene escala distinta de (1,1,1).
        _waffleOriginalParent = waffleDisplay.transform.parent;
        _waffleLocalPos = waffleDisplay.transform.localPosition;
        _waffleLocalScale = waffleDisplay.transform.localScale;
        _waffleLocalRot = waffleDisplay.transform.localRotation;

        Debug.Log($"[Oven] ExtractWaffle — lossyScale antes: {waffleDisplay.transform.lossyScale} | localScale guardado: {_waffleLocalScale}");

        // ── 2. Apagar efectos ──
        if (_smokeInstance != null) _smokeInstance.SetActive(false);
        if (readyGlow != null) readyGlow.SetActive(false);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);

        // ── 3. Capturar lossyScale ANTES de desparentizar ──
        // Tras SetParent(null), Unity recalcula localScale = lossyScale / scale_del_nuevo_padre.
        // El nuevo padre es null (world, escala 1,1,1), así que localScale = lossyScale anterior.
        // Capturamos lossyScale para forzarlo manualmente después.
        Vector3 worldScaleToPreserve = waffleDisplay.transform.lossyScale;

        // ── 4. Desparentizar ──
        waffleDisplay.transform.SetParent(null, worldPositionStays: true);

        // ── 5. Forzar escala world correcta ──
        // Sin esto, si el Oven tiene escala != (1,1,1), el waffle aparece enorme o diminuto.
        waffleDisplay.transform.localScale = worldScaleToPreserve;

        Debug.Log($"[Oven] Tras SetParent(null) — localScale = {worldScaleToPreserve}");

        // ── 6. Desactivar colliders de hijos (no deben interferir con el drop) ──
        foreach (Transform child in waffleDisplay.transform)
        {
            Collider2D childCol = child.GetComponent<Collider2D>();
            if (childCol != null) childCol.enabled = false;
        }

        // ── 7. Añadir DraggableItem al raíz ──
        // Destruir residual de un ciclo anterior para evitar estado corrupto.
        _extractedWaffle = waffleDisplay.GetComponent<DraggableItem>();

        if (_extractedWaffle == null)
        {
            _extractedWaffle = waffleDisplay.AddComponent<DraggableItem>();
        }
        else
        {
            // MUY IMPORTANTE:
            // limpiar estado residual del ciclo anterior
            _extractedWaffle.ResetState();
        }

        // RESET COMPLETO DEL ESTADO
        _extractedWaffle.enabled = true;

        _extractedWaffle.itemType = waffleType;
        _extractedWaffle.isDraggable = true;
        _extractedWaffle.persistentDrag = true;
        _extractedWaffle.destroyOnFailedDrop = false;
        _extractedWaffle.carrySortingOrder = waffleCarrySortingOrder;

        // MUY IMPORTANTE
        _extractedWaffle.SetOriginOven(this);

        // ── 8. Posicionar en el cursor ──
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 mouseWorld = cam.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y,
                            Mathf.Abs(cam.transform.position.z)));
            mouseWorld.z = 0f;
            waffleDisplay.transform.position = mouseWorld;
        }

        _extractedWaffle.SetOriginOven(this);
        DragManager.Instance?.OnItemPickedUp(_extractedWaffle);

        SetState(OvenState.WaffleExtracted);
        Debug.Log($"[Oven] Waffle extraído: {waffleType}");
    }

    // ═══════════════════════════════════════════════════════════
    // RETORNO AL HORNO
    // ═══════════════════════════════════════════════════════════

    public void ReturnWaffle(DraggableItem waffle)
    {
        if (waffle == null || waffleDisplay == null) return;

        Debug.Log($"[Oven] ReturnWaffle → restaurando estado: {_extractedWaffleState}");

        // ── Orden crítico: primero SetParent, luego asignar valores locales ──
        // Si asignáramos localScale ANTES del SetParent, Unity lo recalcularía
        // al cambiar de padre y quedaría corrupto.
        waffleDisplay.transform.SetParent(_waffleOriginalParent, worldPositionStays: false);
        waffleDisplay.transform.localPosition = _waffleLocalPos;
        waffleDisplay.transform.localScale = _waffleLocalScale;
        waffleDisplay.transform.localRotation = _waffleLocalRot;

        waffleDisplay.SetActive(false);

        Debug.Log($"[Oven] ReturnWaffle — localScale restaurado: {_waffleLocalScale}");

        // Restaurar sprite
        Sprite restoreSprite = _extractedWaffleState switch
        {
            OvenState.Overcooked => spriteWaffleOvercooked,
            OvenState.Burned => spriteWaffleBurned,
            _ => spriteWaffleReady
        };
        ShowWaffle(restoreSprite);

        // Restaurar efectos
        if (_extractedWaffleState == OvenState.Burned)
        {
            if (_smokeInstance != null) _smokeInstance.SetActive(true);
        }
        else
        {
            if (readyGlow != null) readyGlow.SetActive(true);
        }

        SetState(_extractedWaffleState);

        if (_state == OvenState.Ready || _state == OvenState.Overcooked)
        {
            _timerRunning = true;
            if (cookingBar != null) cookingBar.gameObject.SetActive(true);
        }

        _extractedWaffle = null;
        _extractedWaffle = null;

        waffle.isDraggable = false;
        waffle.persistentDrag = false;
        waffle.destroyOnFailedDrop = false;
    }


    // ═══════════════════════════════════════════════════════════
    // MEJORAS
    // ═══════════════════════════════════════════════════════════

    public void Unlock()
    {
        isUnlocked = true;
        if (lockedOverlay != null) lockedOverlay.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════════
    // BARRA DE CALOR
    // ═══════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Activa el waffleDisplay y asigna el sprite al waffleRenderer.
    /// waffleRenderer es un campo explícito del Inspector — nunca se busca
    /// dinámicamente, así que siempre apunta al objeto correcto.
    /// </summary>
    private void ShowWaffle(Sprite sprite)
    {
        if (waffleDisplay != null)
            waffleDisplay.SetActive(true);

        if (waffleRenderer != null)
            waffleRenderer.sprite = sprite;
        else
            Debug.LogError("[Oven] ShowWaffle: waffleRenderer es null. " +
                           "Asigna el SpriteRenderer del waffle en el Inspector.");
    }

    private void SetAnimatorTrigger(string p)
    {
        if (waffleraAnimator != null) waffleraAnimator.SetTrigger(p);
    }

    private void SetAnimatorBool(string p, bool v)
    {
        if (waffleraAnimator != null) waffleraAnimator.SetBool(p, v);
    }

    private void SetState(OvenState s)
    {
        _state = s;
        Debug.Log($"[Oven] Estado → {s}");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// OVEN ANIMATOR EVENT RELAY
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Puente para Animation Events cuando el Animator vive en un hijo (AnimatedLayer).
/// Añadir al GameObject del Animator. Asignar en el campo "animatorEventRelay" del Oven.
/// </summary>
public class OvenAnimatorEventRelay : MonoBehaviour
{
    private Oven _owner;
    public void SetOwner(Oven owner) => _owner = owner;
    public void OnWaffleReveal() => _owner?.OnWaffleReveal();
}