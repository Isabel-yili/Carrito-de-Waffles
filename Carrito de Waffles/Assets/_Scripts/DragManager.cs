using UnityEngine;
using System;

/// <summary>
/// NÚCLEO MÍNIMO — DragManager (Singleton)
/// Punto central de toda la interacción con ítems.
/// Gestiona tanto drag-and-drop como el modo alternativo click-click.
/// </summary>
public class DragManager : MonoBehaviour
{
    public static DragManager Instance { get; private set; }

    [Header("Cursor Feedback")]
    public Texture2D cursorDefault;
    public Texture2D cursorHolding;

    // ─── Estado interno ───────────────────────────────
    private DraggableItem _selectedItem;
    private GameObject _ghostIcon;       // Ícono flotante que sigue el cursor
    private bool _hasSelectedItem = false;

    public bool HasSelectedItem => _hasSelectedItem;
    public DraggableItem SelectedItem => _selectedItem;

    // ─── Eventos ──────────────────────────────────────
    public event Action<DraggableItem> OnItemPickedUpEvent;
    public event Action<DraggableItem> OnItemDroppedEvent;
    public event Action<DraggableItem, IItemReceiver> OnSuccessfulPlacementEvent;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        // Cancelar selección con clic derecho o Escape
        if (_hasSelectedItem)
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelSelection();
            }
        }
    }

    // ─────────────────────────────────────────────────
    // API PÚBLICA — llamada desde ItemSource / DraggableItem
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Registra un ítem como "en mano" y activa su lógica de seguimiento.
    /// DEBE llamarse siempre que se quiera que un item siga al cursor.
    /// </summary>
    public void OnItemPickedUp(DraggableItem item)
    {
        if (item == null) return;

        _selectedItem = item;
        _hasSelectedItem = true;

        // ── CRÍTICO: activar el modo carry en el ítem ──────────────
        // Sin esta llamada, _isBeingCarried permanece false y el Update
        // del DraggableItem nunca se ejecuta → el item no sigue al cursor
        // y los clicks no disparan TryDeliverToTarget.
        item.StartCarrying();

        SetCursor(cursorHolding);
        OnItemPickedUpEvent?.Invoke(item);

        Debug.Log($"[DragManager] OnItemPickedUp → {item.name} | StartCarrying llamado");
    }

    /// <summary>
    /// Libera el ítem actual (drop exitoso o cancelación desde fuera).
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
    /// Modo click-to-place: selecciona un item con click.
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
    /// Intenta interactuar el item seleccionado con el target clickeado.
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
            _selectedItem.ReturnToOrigin(); // ReturnToOrigin llama StopCarrying internamente

        _selectedItem = null;
        _hasSelectedItem = false;
        SetCursor(cursorDefault);
        DestroyGhost();
    }

    /// <summary>
    /// Llamado por DraggableItem.TryDeliverToTarget cuando el drop fue exitoso.
    /// Dispara OnSuccessfulPlacementEvent para que WaffleMixAnimatorSync
    /// (y cualquier listener) puedan reaccionar.
    /// </summary>
    public void NotifySuccessfulPlacement(DraggableItem item, IItemReceiver receiver)
    {
        OnSuccessfulPlacementEvent?.Invoke(item, receiver);
    }

    // ─────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────

    private void SetCursor(Texture2D texture)
    {
        if (texture != null)
            Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);
        else
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    private void DestroyGhost()
    {
        if (_ghostIcon != null)
        {
            Destroy(_ghostIcon);
            _ghostIcon = null;
        }
    }
}