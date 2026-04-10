using UnityEngine;

/// <summary>
/// Fuente infinita de ítems — mezcla de waffle, helados, miel.
/// Al hacer click/drag genera un nuevo ítem que sigue el cursor.
/// GDD sección 4.2: "Clic en el recipiente de mezcla → ícono de mezcla sigue el cursor"
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemSource : MonoBehaviour, IItemSource, IItemReceiver
{
    [Header("Configuración")]
    public ItemType producedItemType;
    public GameObject itemPrefab;

    [Header("Visual Feedback")]
    public Animator sourceAnimator;  // Animación de "pulsación" al hacer click

    public ItemType ProducedItemType => producedItemType;

    // IItemSource
    public DraggableItem SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogError($"[ItemSource] No hay prefab asignado para {producedItemType}");
            return null;
        }

        // Instanciar en la posición de la fuente
        GameObject go = Instantiate(itemPrefab, transform.position, Quaternion.identity);
        DraggableItem item = go.GetComponent<DraggableItem>();

        if (item != null)
        {
            item.itemType = producedItemType;
        }

        // Feedback visual en la fuente
        if (sourceAnimator != null)
            sourceAnimator.SetTrigger("Pulse");

        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);

        return item;
    }

    // IItemReceiver — las fuentes NO aceptan ítems (excepto basura)
    public bool CanReceive(DraggableItem item) => false;
    public void ReceiveItem(DraggableItem item) { }

    // Al hacer click sobre la fuente, spawnear y comenzar drag automáticamente
    void OnMouseDown()
    {
        DraggableItem spawned = SpawnItem();
        if (spawned != null)
        {
            // Iniciar el drag programáticamente en el próximo frame
            StartCoroutine(BeginDragNextFrame(spawned));
        }
    }

    private System.Collections.IEnumerator BeginDragNextFrame(DraggableItem item)
    {
        yield return null; // Esperar un frame para que Unity procese el evento
        DragManager.Instance?.OnItemPickedUp(item);
    }
}
