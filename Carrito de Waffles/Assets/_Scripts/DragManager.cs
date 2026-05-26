using UnityEngine;
using System;

/// <summary>
/// DRAG MANAGER v2 — gestión centralizada de input.
///
/// CAMBIO PRINCIPAL respecto a v1:
///   El click izquierdo (cuando no hay ítem en mano) ahora se procesa aquí
///   mediante Physics2D.OverlapPoint, en lugar de depender de OnMouseDown()
///   en cada objeto individual.
///
///   Esto resuelve el problema donde el Collider2D del Oven bloqueaba el
///   raycast hacia el WaffleDisplay: OnMouseDown solo se dispara en el
///   PRIMER collider interceptado, pero OverlapPoint devuelve TODOS los
///   colliders en ese punto, permitiendo buscar el más apropiado.
///
/// FLUJO DEL CLICK (sin ítem en mano):
///   1. Update() detecta GetMouseButtonDown(0).
///   2. HandleWorldClick() hace OverlapPoint en la posición del cursor.
///   3. Busca en los resultados, en orden de prioridad:
///      a. DraggableItem arrastrable → SelectItem()
///      b. ItemSource → SpawnItem() + OnItemPickedUp()
///      c. Oven con waffle listo → RequestExtract()
///   4. Si no encuentra nada interactivo, no hace nada.
///
/// FLUJO DEL CLICK (con ítem en mano):
///   El DraggableItem activo maneja su propio Update() y llama
///   TryDeliverToTarget() — sin cambios respecto a v1.
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

    // Previene doble-disparo: OnMouseDown de Plate/ItemSource + HandleWorldClick en el mismo frame
    private bool _clickHandledByOnMouseDown = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Llamado por Plate.OnMouseDown() e ItemSource.OnMouseDown() para indicar
    /// que el click de este frame ya fue procesado por Unity's event system.
    /// Evita que HandleWorldClick lo procese una segunda vez.
    /// </summary>
    public void MarkClickHandled() => _clickHandledByOnMouseDown = true;

    void Update()
    {
        // Limpiar flag al inicio de cada frame
        _clickHandledByOnMouseDown = false;

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            if (_hasSelectedItem) CancelSelection();
            return;
        }

        // Click izquierdo sin ítem en mano → buscar objeto interactivo en el mundo
        if (Input.GetMouseButtonDown(0) && !_hasSelectedItem)
        {
            HandleWorldClick();
        }
        // Con ítem en mano, el DraggableItem activo maneja su propio Update()
    }

    // ═════════════════════════════════════════════════
    // CLICK SIN ÍTEM EN MANO
    // ═════════════════════════════════════════════════

    /// <summary>
    /// Hace OverlapPoint en la posición del cursor y busca el objeto
    /// interactivo más apropiado entre TODOS los colliders superpuestos.
    /// Resuelve el problema de colliders apilados (Oven + WaffleDisplay).
    /// </summary>
    private void HandleWorldClick()
    {
        // Si Plate.OnMouseDown() o ItemSource.OnMouseDown() ya procesaron este click,
        // no procesar de nuevo (evita doble SelectItem o doble spawn)
        if (_clickHandledByOnMouseDown) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[DragManager] Camera.main es null — asegúrate de que la cámara tiene el tag 'MainCamera'.");
            return;
        }

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = Mathf.Abs(cam.transform.position.z);
        Vector2 worldPos = cam.ScreenToWorldPoint(mouseScreen);

        Collider2D[] hits = new Collider2D[16];
        ContactFilter2D filter = new ContactFilter2D().NoFilter();
        int count = Physics2D.OverlapPoint(worldPos, filter, hits);

        Debug.Log($"[DragManager] HandleWorldClick en {worldPos} | hits: {count}");

        // ── Prioridad 1: DraggableItem listo para arrastrar ──────
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            DraggableItem draggable = hits[i].GetComponent<DraggableItem>()
                                  ?? hits[i].GetComponentInParent<DraggableItem>();
            if (draggable != null && draggable.isDraggable && !draggable.IsBeingCarried)
            {
                Debug.Log($"[DragManager]   → DraggableItem encontrado: {draggable.gameObject.name}");
                SelectItem(draggable);
                return;
            }
        }

        // ── Prioridad 2: ItemSource (helados, mezcla, miel) ──────
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            ItemSource source = hits[i].GetComponent<ItemSource>()
                             ?? hits[i].GetComponentInParent<ItemSource>();
            if (source != null)
            {
                Debug.Log($"[DragManager]   → ItemSource encontrado: {source.gameObject.name}");
                // ItemSource.OnMouseDown() maneja su propia lógica de spawn+carry.
                // No duplicamos aquí; si ItemSource tiene Collider, OnMouseDown sí funciona
                // porque no hay otro collider apilado bloqueándolo.
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
                Debug.Log($"[DragManager]   → Oven encontrado: {oven.gameObject.name} | Estado: {oven.State}");
                oven.RequestExtract();
                return;
            }
        }

        Debug.Log("[DragManager]   → Sin objeto interactivo en ese punto.");
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
    /// </summary>
    public void OnItemReleased(DraggableItem item)
    {
        if (item != null)
            item.StopCarrying();

        _selectedItem = null;
        _hasSelectedItem = false;
        SetCursor(cursorDefault);
        OnItemDroppedEvent?.Invoke(item);

        Debug.Log($"[DragManager] OnItemReleased → {item?.name}");
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
            OnSuccessfulPlacementEvent?.Invoke(_selectedItem, receiver);
            OnItemReleased(_selectedItem);
        }
        else
        {
            FeedbackManager.Instance?.ShowInvalidAction(target.transform.position);
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
        }
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

    public void NotifySuccessfulPlacement(DraggableItem item, IItemReceiver receiver)
    {
        OnSuccessfulPlacementEvent?.Invoke(item, receiver);
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