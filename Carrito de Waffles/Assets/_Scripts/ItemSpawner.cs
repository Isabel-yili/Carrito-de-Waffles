using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawner centralizado de ítems.
/// Mantiene un pool simple de prefabs por ItemType.
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    public static ItemSpawner Instance { get; private set; }

    [System.Serializable]
    public class ItemEntry
    {
        public ItemType type;
        public GameObject prefab;
    }

    [Header("Prefabs registrados")]
    public List<ItemEntry> itemPrefabs;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public DraggableItem SpawnItem(ItemType type, Vector3 position)
    {
        foreach (var entry in itemPrefabs)
        {
            if (entry.type == type && entry.prefab != null)
            {
                GameObject go = Instantiate(entry.prefab, position, Quaternion.identity);
                DraggableItem item = go.GetComponent<DraggableItem>();
                if (item != null) item.itemType = type;
                return item;
            }
        }

        Debug.LogWarning($"[ItemSpawner] No se encontró prefab para: {type}");
        return null;
    }
}
