using UnityEngine;
using System;

/// <summary>
/// DRAG MANAGER v3 — gestión centralizada de input.
///
/// CAMBIO v3:
///   NotifySuccessfulPlacement() ahora detecta si el receiver es una
///   DeliveryPlatform. En ese caso llama item.MarkReleaseHandledByReceiver()
///   para que DraggableItem.TryDeliverToTarget() sepa que NO debe llamar
///   OnItemReleased() por segunda vez (ya lo hará DeliveryPlatform).
///
///   Para todos los demás receptores (Plate, Oven, ItemSlot, TrashDropZone):
///   DraggableItem llama OnItemReleased() normalmente después de ReceiveItem().
///
/// FLUJO DEL CLICK (sin ítem en mano):
///   1. Update() detecta GetMouseButtonDown(0)
///   2. HandleWorldClick() hace OverlapPoint en la posición del cursor
///   3. Busca, en orden de prioridad:
///      a. DraggableItem arrastrable → SelectItem()
///      b. ItemSource → OnMouseDown() ya lo maneja, no duplicar
///      c. Oven con waffle → RequestExtract()
///
/// FLUJO DEL CLICK (con ítem en mano):
///   DraggableItem.Update() llama TryDeliverToTarget() → ReceiveItem().
/// </summary>
public class DragManager : MonoBehaviour
{
    public static DragManager Instance { get; private set; }

    [Header("Cursor Feedback")]
    public Texture2D cursorDefault;
    public Texture2D cursorHolding;

    // ─── Estado interno ───────────────────────────────
    private DraggableItem _selectedItem;
    private GameObject _ghostIcon;
    private bool _hasSelectedItem = false;

    public bool HasSelectedItem => _hasSelectedItem;
    public DraggableItem SelectedItem => _selectedItem;

    // ─── Eventos ──────────────────────────────────────
    public event Action<DraggableItem> OnItemPickedUpEvent;
    public event Action<DraggableItem> OnItemDroppedEvent;
    public event Action<DraggableItem, IItemReceiver> OnSuccessfulPlacementEvent;

    // Previene doble-disparo: OnMouseDown + HandleWorldClick en el mismo frame
    private bool _clickHandledByOnMouseDown = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Llamado por ItemSource.OnMouseDown() / Plate.OnMouseDown() para indicar
    /// que el click de este frame ya fue procesado por Unity's event system.
    /// </summary>
    public void MarkClickHandled() => _clickHandledByOnMouseDown = true;

    void Update()
    {
        _clickHandledByOnMouseDown = false;

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            if (_hasSelectedItem) CancelSelection();
            return;
        }

        if (Input.GetMouseButtonDown(0) && !_hasSelectedItem)
            HandleWorldClick();
    }

    // ═════════════════════════════════════════════════
    // CLICK SIN ÍTEM EN MANO
    // ═════════════════════════════════════════════════

    private void HandleWorldClick()
    {
        if (_clickHandledByOnMouseDown) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[DragManager] Camera.main es null.");
            return;
        }

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = Mathf.Abs(cam.transform.position.z);
        Vector2 worldPos = cam.ScreenToWorldPoint(mouseScreen);

        Collider2D[] hits = new Collider2D[16];
        ContactFilter2D filter = new ContactFilter2D().NoFilter();
        int count = Physics2D.OverlapPoint(worldPos, filter, hits);

        Debug.Log($"[DragManager] HandleWorldClick en {worldPos} | hits: {count}");

        // ── Prioridad 1: DraggableItem arrastrable ───────────────
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            DraggableItem draggable = hits[i].GetComponent<DraggableItem>()
                                  ?? hits[i].GetComponentInParent<DraggableItem>();
            if (draggable != null && draggable.isDraggable && !draggable.IsBeingCarried)
            {
                Debug.Log($"[DragManager]   → DraggableItem: {draggable.gameObject.name}");
                SelectItem(draggable);
                return;
            }
        }

        // ── Prioridad 2: ItemSource ──────────────────────────────
        // ItemSource.OnMouseDown() lo maneja directamente — no duplicar aquí.
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            ItemSource source = hits[i].GetComponent<ItemSource>()
                             ?? hits[i].GetComponentInParent<ItemSource>();
            if (source != null)
            {
                Debug.Log($"[DragManager]   → ItemSource: {source.gameObject.name} (delegado a OnMouseDown)");
                return;
            }
        }

        // ── Prioridad 3: Oven con waffle disponible ──────────────
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            Oven oven = hits[i].GetComponent<Oven>()
                     ?? hits[i].GetComponentInParent<Oven>();
            if (oven != null)
            {
                Debug.Log($"[DragManager]   → Oven: {oven.gameObject.name} | Estado: {oven.State}");
                oven.RequestExtract();
                return;
            }
        }

        Debug.Log("[DragManager]   → Sin objeto interactivo.");
    }

    // ═════════════════════════════════════════════════
    // API PÚBLICA
    // ═════════════════════════════════════════════════

    /// <summary>
    /// Registra un ítem como "en mano" y activa su lógica de seguimiento.
    /// </summary>
    public void OnItemPickedUp(DraggableItem item)
    {
        if (item == null) return;

        _selectedItem = item;
        _hasSelectedItem = true;

        item.StartCarrying();
        SetCursor(cursorHolding);
        OnItemPickedUpEvent?.Invoke(item);

        Debug.Log($"[DragManager] OnItemPickedUp → {item.name}");
    }

    /// <summary>
    /// Libera el ítem actual (drop exitoso o cancelación).
    /// Llamar siempre ANTES de Destroy() en el objeto.
    /// </summary>
    public void OnItemReleased(DraggableItem item)
    {
        if (item != null)
            item.StopCarrying();

        _selectedItem = null;
        _hasSelectedItem = false;
        SetCursor(cursorDefault);
        OnItemDroppedEvent?.Invoke(item);

        Debug.Log($"[DragManager] OnItemReleased → {item?.name ?? "null"}");
    }

    /// <summary>
    /// Selecciona un ítem para arrastrarlo (modo click-to-place).
    /// </summary>
    public void SelectItem(DraggableItem item)
    {
        if (_hasSelectedItem && _selectedItem == item)
        {
            CancelSelection();
            return;
        }

        _selectedItem = item;
        _hasSelectedItem = true;
        item.StartCarrying();
        SetCursor(cursorHolding);
        OnItemPickedUpEvent?.Invoke(item);
    }

    /// <summary>
    /// Intenta interactuar el ítem seleccionado con el target clickeado.
    /// Llamado desde Plate.OnMouseDown cuando el jugador lleva algo.
    /// </summary>
    public void TryInteractWith(DraggableItem target)
    {
        if (!_hasSelectedItem || _selectedItem == null) return;

        IItemReceiver receiver = target.GetComponent<IItemReceiver>();
        if (receiver != null && receiver.CanReceive(_selectedItem))
        {
            receiver.ReceiveItem(_selectedItem);
            NotifySuccessfulPlacement(_selectedItem, receiver);
            OnItemReleased(_selectedItem);
        }
        else
        {
            FeedbackManager.Instance?.ShowInvalidAction(target.transform.position);
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
        }
    }

    /// <summary>
    /// Notifica a listeners que un placement fue exitoso.
    /// Si el receiver es DeliveryPlatform, marca el release como manejado
    /// por el receiver (evita doble OnItemReleased).
    /// </summary>
    public void NotifySuccessfulPlacement(DraggableItem item, IItemReceiver receiver)
    {
        // DeliveryPlatform llama OnItemReleased internamente (debe hacerlo
        // antes de Destroy). Marcar para que DraggableItem no lo llame también.
        if (receiver is DeliveryPlatform && item != null)
            item.MarkReleaseHandledByReceiver();

        OnSuccessfulPlacementEvent?.Invoke(item, receiver);
    }

    public void CancelSelection()
    {
        if (_selectedItem != null)
            _selectedItem.ReturnToOrigin();

        _selectedItem = null;
        _hasSelectedItem = false;
        SetCursor(cursorDefault);
        DestroyGhost();
    }

    // ─────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────

    private void SetCursor(Texture2D texture)
    {
        Cursor.SetCursor(texture != null ? texture : null, Vector2.zero, CursorMode.Auto);
    }

    private void DestroyGhost()
    {
        if (_ghostIcon != null) { Destroy(_ghostIcon); _ghostIcon = null; }
    }
}