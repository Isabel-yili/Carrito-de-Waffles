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
    // API PÚBLICA — llamada desde DraggableItem
    // ─────────────────────────────────────────────────

    public void OnItemPickedUp(DraggableItem item)
    {
        _selectedItem = item;
        _hasSelectedItem = true;
        SetCursor(cursorHolding);
        OnItemPickedUpEvent?.Invoke(item);
    }

    public void OnItemReleased(DraggableItem item)
    {
        _selectedItem = null;
        _hasSelectedItem = false;
        SetCursor(cursorDefault);
        OnItemDroppedEvent?.Invoke(item);
    }

    /// <summary>
    /// Modo click-to-place: selecciona un item con click
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
        SetCursor(cursorHolding);
        OnItemPickedUpEvent?.Invoke(item);
    }

    /// <summary>
    /// Intenta interactuar el item seleccionado con el target clickeado
    /// </summary>
    public void TryInteractWith(DraggableItem target)
    {
        if (!_hasSelectedItem || _selectedItem == null) return;

        // Verificar si el target es un receptor
        IItemReceiver receiver = target.GetComponent<IItemReceiver>();
        if (receiver != null && receiver.CanReceive(_selectedItem))
        {
            receiver.ReceiveItem(_selectedItem);
            OnSuccessfulPlacementEvent?.Invoke(_selectedItem, receiver);
            OnItemReleased(_selectedItem);
        }
        else
        {
            // Feedback de acción inválida
            FeedbackManager.Instance?.ShowInvalidAction(target.transform.position);
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
        }
    }

    public void CancelSelection()
    {
        if (_selectedItem != null)
        {
            _selectedItem.ReturnToOrigin();
        }
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
