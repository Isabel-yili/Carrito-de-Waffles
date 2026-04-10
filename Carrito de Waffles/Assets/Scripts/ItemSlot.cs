using UnityEngine;

/// <summary>
/// Ranura visual en la mesa de trabajo.
/// Puede contener un ítem y define su posición de origen.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemSlot : MonoBehaviour, IItemReceiver
{
    [Header("Configuración")]
    public ItemType acceptedType = ItemType.None; // None = acepta cualquiera
    public bool allowStack = false;

    private DraggableItem _containedItem;
    public DraggableItem ContainedItem => _containedItem;
    public bool IsEmpty => _containedItem == null;

    // IItemReceiver
    public bool CanReceive(DraggableItem item)
    {
        if (!IsEmpty && !allowStack) return false;
        if (acceptedType != ItemType.None && item.itemType != acceptedType) return false;
        return true;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        _containedItem = item;
        item.transform.position = transform.position;
        item.SetSlot(this);

        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);
    }

    public void ClearSlot()
    {
        _containedItem = null;
    }

    // Highlight visual cuando un ítem compatible está siendo arrastrado encima
    void OnTriggerEnter2D(Collider2D other)
    {
        DraggableItem item = other.GetComponent<DraggableItem>();
        if (item != null && CanReceive(item))
            GetComponent<SpriteRenderer>()?.material.SetFloat("_Highlight", 1f);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        GetComponent<SpriteRenderer>()?.material.SetFloat("_Highlight", 0f);
    }
}
