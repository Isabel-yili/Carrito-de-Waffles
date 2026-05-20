using UnityEngine;
using System.Collections;

/// <summary>
/// FUENTE DE ÍTEMS — mezcla de waffle, helados (x3 sabores), miel.
/// Mapeado sobre la ilustración:
///   - Helados: bandeja inferior izquierda — 3 fuentes separadas
///   - Mezcla:  tazón azul inferior derecho
///   - Miel:    tarro naranja del mostrador derecho
///
/// ANIMACIONES PROCREATE para helados:
///   Al hacer click en una bola de helado, se reproduce una animación
///   de "sacar la bola" del contenedor. La lógica espera a que termine
///   antes de poner el ítem en el cursor del jugador.
///
///   Trigger del Animator: "IceCreamSelect"
///   Exportar desde Procreate:
///     - Sprite sheet horizontal, fondo transparente
///     - Tamaño sugerido: 256×256 px por frame, 6-8 frames
///     - El último frame debe ser idéntico al primero (loop limpio)
///
/// JERARQUÍA DEL PREFAB:
///   IceCreamSource_Fresa  (este script + Collider2D)
///   ├── IceCreamBody      → SpriteRenderer — bola de helado estática (idle)
///   └── IceCreamAnimator  → Animator — animación de selección Procreate
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemSource : MonoBehaviour, IItemSource, IItemReceiver
{
    [Header("══ Configuración ══")]
    public ItemType producedItemType;
    public GameObject itemPrefab;

    [Header("══ Animación Procreate ══")]
    [Tooltip("Animator del hijo que contiene la animación de selección")]
    public Animator sourceAnimator;
    [Tooltip("Trigger del Animator — debe llamarse exactamente 'IceCreamSelect' para helados o 'BowlPour' para la mezcla")]
    public string selectTrigger = "IceCreamSelect";
    [Tooltip("Duración de la animación antes de que el ítem aparezca en el cursor")]
    public float animationDuration = 0.3f;
    [Tooltip("Si true, el ítem aparece DURANTE la animación (más responsivo). Si false, espera al final.")]
    public bool spawnDuringAnimation = true;

    [Header("══ Visual de feedback ══")]
    [Tooltip("Efecto de partículas opcional al seleccionar (frío para helado, vapor para mezcla)")]
    public ParticleSystem selectParticle;

    // ─── Estado interno  ───
    private bool _isSpawning = false;

    public ItemType ProducedItemType => producedItemType;

    // ─────────────────────────────────────────────────────────────
    // IItemSource
    // ─────────────────────────────────────────────────────────────

    public DraggableItem SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogError($"[ItemSource] No hay prefab asignado para {producedItemType}");
            return null;
        }

        GameObject go = Instantiate(itemPrefab, transform.position, Quaternion.identity);
        DraggableItem item = go.GetComponent<DraggableItem>();
        if (item != null) item.itemType = producedItemType;

        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);
        return item;
    }

    // IItemReceiver — las fuentes no aceptan ítems
    public bool CanReceive(DraggableItem item) => false;
    public void ReceiveItem(DraggableItem item) { }

    // ─────────────────────────────────────────────────────────────
    // INTERACCIÓN — click sobre la fuente
    // ─────────────────────────────────────────────────────────────

    void OnMouseDown()
    {
        Debug.Log("[ItemSource] OnMouseDown disparado");
        if (_isSpawning) return;
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;
        StartCoroutine(SelectSequence());
    }

    /// <summary>
    /// Secuencia completa: animación → spawn → drag
    /// </summary>
    private IEnumerator SelectSequence()
    {
        _isSpawning = true;  // ← añadir

        GetComponent<WaffleMixAnimatorSync>()?.NotifySelected();

        if (sourceAnimator != null && !string.IsNullOrEmpty(selectTrigger))
            sourceAnimator.SetTrigger(selectTrigger);

        if (selectParticle != null)
            selectParticle.Play();

        DraggableItem spawned = null;

        if (spawnDuringAnimation)
        {
            spawned = SpawnItem();
            if (spawned != null)
                PositionItemAtCursor(spawned);
            yield return new WaitForSeconds(animationDuration);
        }
        else
        {
            yield return new WaitForSeconds(animationDuration);
            spawned = SpawnItem();
            if (spawned != null)
                PositionItemAtCursor(spawned);
        }

        if (spawned != null)
        {
            Debug.Log($"[ItemSource] Llamando OnItemPickedUp con: {spawned.name}");
            DragManager.Instance?.OnItemPickedUp(spawned);
        }

        _isSpawning = false;  // ← añadir
    }

    /// <summary>
    /// Coloca el item recién spawneado en la posición del cursor del jugador.
    /// </summary>
    private void PositionItemAtCursor(DraggableItem item)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        item.transform.position = mouseWorld;
    }
}