using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
// WAFFLEMIX ANIMATOR SYNC
//
// Responsabilidad única:
//   Escuchar eventos del DragManager y traducirlos en parámetros del
//   AnimatorController. También gestiona qué SpriteRenderers son
//   visibles durante la animación para evitar que el sprite estático
//   tape los frames animados.
//
// JERARQUÍA DEL PREFAB:
//   WaffleMix  (ItemSource + Collider2D + este script)
//   ├── IceCreamBody / Body  → SpriteRenderer ESTÁTICO (se oculta durante animación)
//   └── AnimatedLayer        → Animator + SpriteRenderers ANIMADOS
//
// ANIMATOR CONTROLLER — parámetros requeridos:
//   DoSelect    Trigger  → activa MixWaffle.anim al hacer click
//   IsCarrying  Bool     → true mientras el ítem generado sigue en el cursor
//   DoCancel    Trigger  → mezcla cancelada / drop inválido / Escape
//   DoDelivered Trigger  → mezcla entregada al horno con éxito
//
// SETUP EN UNITY:
//   1. Añadir este script al mismo GameObject que tiene ItemSource.
//   2. Arrastrar el Animator del hijo AnimatedLayer al campo "waffleMixAnimator".
//   3. El script detecta automáticamente qué SpriteRenderers ocultar.
//      Si la detección automática falla, asignar manualmente en "staticRenderers".
// ═══════════════════════════════════════════════════════════════════

[RequireComponent(typeof(ItemSource))]
public class WaffleMixAnimatorSync : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────
    [Header("══ Animator del AnimatedLayer ══")]
    [Tooltip("Arrastra aquí el Animator del hijo 'AnimatedLayer'")]
    public Animator waffleMixAnimator;

    [Header("══ SpriteRenderers estáticos a ocultar durante la animación ══")]
    [Tooltip("Si está vacío, el script los detecta automáticamente al inicio.\n" +
             "Incluye todos los SpriteRenderer del root que NO están dentro del AnimatedLayer.\n" +
             "Asignar manualmente solo si la detección automática falla.")]
    public List<SpriteRenderer> staticRenderers = new List<SpriteRenderer>();

    [Tooltip("Sorting Order que usa el AnimatedLayer. Los SpriteRenderers con Order\n" +
             "MAYOR O IGUAL que este valor se consideran parte de la animación y no se tocan.")]
    public int animatedLayerSortingOrder = 1;

    // ─── Parámetros del Animator ──────────────────────────────────
    private const string PARAM_DO_SELECT = "DoSelect";
    private const string PARAM_IS_CARRYING = "IsCarrying";
    private const string PARAM_DO_CANCEL = "DoCancel";
    private const string PARAM_DO_DELIVERED = "DoDelivered";

    // ─── Estado interno ───────────────────────────────────────────
    private ItemSource _itemSource;
    private DraggableItem _trackedItem;
    private bool _isCarrying = false;

    // ─────────────────────────────────────────────────────────────
    // CICLO DE VIDA
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _itemSource = GetComponent<ItemSource>();
        AutoDetectStaticRenderers();
    }

    void OnEnable()
    {
        if (DragManager.Instance != null)
            SubscribeToDragManager();
        else
            StartCoroutine(WaitForDragManager());
    }

    void OnDisable()
    {
        UnsubscribeFromDragManager();
    }

    // ─────────────────────────────────────────────────────────────
    // DETECCIÓN AUTOMÁTICA DE RENDERERS ESTÁTICOS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Busca todos los SpriteRenderers en el prefab que NO pertenecen al AnimatedLayer.
    /// Estrategia: cualquier SpriteRenderer que no sea hijo del Transform que tiene
    /// el waffleMixAnimator se considera estático.
    /// </summary>
    private void AutoDetectStaticRenderers()
    {
        if (staticRenderers.Count > 0) return; // Ya asignados manualmente — respetar

        if (waffleMixAnimator == null)
        {
            // Sin Animator asignado: ocultar todos los SpriteRenderers del root
            SpriteRenderer own = GetComponent<SpriteRenderer>();
            if (own != null) staticRenderers.Add(own);
            return;
        }

        Transform animRoot = waffleMixAnimator.transform;

        // Recoger todos los SpriteRenderers en el árbol completo
        SpriteRenderer[] all = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer sr in all)
        {
            // ¿Este SR es el propio AnimatedLayer o un descendiente suyo?
            bool isInsideAnimator = sr.transform == animRoot ||
                                    sr.transform.IsChildOf(animRoot);

            if (!isInsideAnimator)
            {
                staticRenderers.Add(sr);
                Debug.Log($"[WaffleMixAnimatorSync] Renderer estático detectado: {sr.gameObject.name}");
            }
        }

        if (staticRenderers.Count == 0)
            Debug.LogWarning("[WaffleMixAnimatorSync] No se encontraron SpriteRenderers estáticos. " +
                             "Asigna manualmente en 'Static Renderers' si el sprite sigue tapando la animación.");
    }

    // ─────────────────────────────────────────────────────────────
    // OCULTAR / MOSTRAR
    // ─────────────────────────────────────────────────────────────

    private void HideStaticRenderers()
    {
        foreach (var sr in staticRenderers)
            if (sr != null) sr.enabled = false;
    }

    private void ShowStaticRenderers()
    {
        foreach (var sr in staticRenderers)
            if (sr != null) sr.enabled = true;
    }

    // ─────────────────────────────────────────────────────────────
    // SUSCRIPCIÓN AL DRAGMANAGER
    // ─────────────────────────────────────────────────────────────

    private IEnumerator WaitForDragManager()
    {
        yield return null;
        if (DragManager.Instance != null)
            SubscribeToDragManager();
        else
            Debug.LogWarning("[WaffleMixAnimatorSync] DragManager no encontrado en escena.");
    }

    private void SubscribeToDragManager()
    {
        DragManager.Instance.OnItemPickedUpEvent += OnItemPickedUp;
        DragManager.Instance.OnItemDroppedEvent += OnItemDropped;
        DragManager.Instance.OnSuccessfulPlacementEvent += OnSuccessfulPlacement;
    }

    private void UnsubscribeFromDragManager()
    {
        if (DragManager.Instance == null) return;
        DragManager.Instance.OnItemPickedUpEvent -= OnItemPickedUp;
        DragManager.Instance.OnItemDroppedEvent -= OnItemDropped;
        DragManager.Instance.OnSuccessfulPlacementEvent -= OnSuccessfulPlacement;
    }

    // ─────────────────────────────────────────────────────────────
    // API PÚBLICA — llamada por ItemSource antes del spawn
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispara la animación de selección y oculta el sprite estático.
    /// Llamado desde ItemSource.SelectSequence() antes del spawn.
    /// </summary>
    public void NotifySelected()
    {
        SetAnimatorTrigger(PARAM_DO_SELECT);
        HideStaticRenderers();
    }

    // Fallback: si ItemSource.selectTrigger está vacío, este OnMouseDown
    // actúa de puente. No interfiere si ItemSource ya llamó NotifySelected().
    void OnMouseDown()
    {
        ItemSource src = GetComponent<ItemSource>();
        if (src != null && !string.IsNullOrEmpty(src.selectTrigger)) return;
        NotifySelected();
    }

    // ─────────────────────────────────────────────────────────────
    // EVENTOS DEL DRAGMANAGER
    // ─────────────────────────────────────────────────────────────

    private void OnItemPickedUp(DraggableItem item)
    {
        if (item == null) return;
        if (item.itemType != _itemSource.ProducedItemType) return;

        float dist = Vector3.Distance(item.transform.position, transform.position);
        if (dist > 2f) return; // Umbral — ajustar si la escena es muy grande

        _trackedItem = item;
        _isCarrying = true;
        SetAnimatorBool(PARAM_IS_CARRYING, true);
    }

    private void OnItemDropped(DraggableItem item)
    {
        if (!_isCarrying || item != _trackedItem) return;

        _isCarrying = false;
        _trackedItem = null;
        SetAnimatorBool(PARAM_IS_CARRYING, false);
        SetAnimatorTrigger(PARAM_DO_CANCEL);
        ShowStaticRenderers();
    }

    private void OnSuccessfulPlacement(DraggableItem item, IItemReceiver receiver)
    {
        if (!_isCarrying || item != _trackedItem) return;

        _isCarrying = false;
        _trackedItem = null;
        SetAnimatorBool(PARAM_IS_CARRYING, false);

        if (receiver is Oven)
            SetAnimatorTrigger(PARAM_DO_DELIVERED);
        else
            SetAnimatorTrigger(PARAM_DO_CANCEL);

        ShowStaticRenderers();
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private void SetAnimatorTrigger(string p)
    {
        if (waffleMixAnimator != null)
            waffleMixAnimator.SetTrigger(p);
        else
            Debug.LogWarning($"[WaffleMixAnimatorSync] Animator no asignado — trigger '{p}' ignorado.");
    }

    private void SetAnimatorBool(string p, bool v)
    {
        if (waffleMixAnimator != null)
            waffleMixAnimator.SetBool(p, v);
    }
}