using UnityEngine;
using System.Collections;

/// <summary>
/// FUENTE DE ÍTEMS v3 — Fix de visual fantasma en sabores de helado.
///
/// PROBLEMA RESUELTO:
///   En la jerarquía IceCreamContainer > [Vanilla|Strawberry|Chocolate],
///   el sub-hijo de cada sabor (AnimatedLayer / cuchara + Animator) arrancaba
///   activo desde el primer frame, causando que su SpriteRenderer fuera visible
///   antes de cualquier click. El contenedor padre también se renderizaba encima
///   de los BoxCollider2D de los hijos, dando la apariencia de "sprites duplicados".
///
/// CORRECCIÓN:
///   • Nuevo campo: visualLayer  → el GameObject hijo que contiene el SpriteRenderer
///     de la cuchara (y el Animator). Se oculta en Awake() y se muestra solo
///     durante SelectSequence(). Se vuelve a ocultar al terminar.
///   • Esto mantiene la jerarquía existente intacta; solo controla la visibilidad
///     del sub-hijo, no lo destruye ni lo mueve.
///
/// PRINCIPIO CENTRAL:
///   Una fuente = un tipo de ítem. Para 3 sabores de helado, usar
///   3 GameObjects separados, cada uno con este script.
///
/// JERARQUÍA DEL PREFAB (helados):
///   IceCreamContainer         ← SOLO visual del contenedor. Sin script ni Collider.
///   ├── ItemSource_Vanilla    ← BoxCollider2D + ItemSource (este script)
///   │   └── AnimatedLayer     ← SpriteRenderer cuchara + Animator  ← asignar a visualLayer
///   ├── ItemSource_Strawberry ← igual
///   └── ItemSource_Chocolate  ← igual
///
/// PREFAB DEL ÍTEM GENERADO:
///   Debe contener SOLO: SpriteRenderer (sprite de la cuchara correcta) + Collider2D + DraggableItem.
///   El sprite asignado en el SpriteRenderer de este prefab es el que aparece en el cursor.
///   Verificar en el Inspector que NO apunta al sprite del contenedor.
///
/// CONFIGURACIÓN EN UNITY:
///   • producedItemType   → ItemType del sabor (IceCreamVanilla, etc.)
///   • itemPrefab         → prefab con SpriteRenderer(cuchara) + Collider2D + DraggableItem
///   • visualLayer        → el hijo que tiene la cuchara animada (AnimatedLayer)
///   • sourceAnimator     → Animator dentro de visualLayer (puede quedar vacío)
///   • selectTrigger      → nombre del Trigger en el Animator
///   • animationDuration  → segundos que dura la animación
///   • spawnDuringAnimation → true = más responsivo, false = espera al final
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemSource : MonoBehaviour, IItemSource, IItemReceiver
{
    // ─── Inspector ──────────────────────────────────────────────────

    [Header("══ Configuración ══")]
    public ItemType producedItemType;

    [Tooltip("Prefab del ítem. DEBE contener SOLO: SpriteRenderer + Collider2D + DraggableItem.\n" +
             "El SpriteRenderer de este prefab es el que se ve en el cursor — verificar que\n" +
             "tenga el sprite correcto (cuchara de vainilla, fresa, etc.) y NO el del contenedor.")]
    public GameObject itemPrefab;

    [Header("══ Visual de la fuente ══")]
    [Tooltip("El hijo que contiene el SpriteRenderer de la cuchara y el Animator.\n" +
             "Se oculta al inicio y solo se muestra durante la animación de selección.\n" +
             "Dejar vacío si la fuente no tiene visual propio (p.ej. la mezcla de waffle\n" +
             "ya lo gestiona WaffleMixAnimatorSync).")]
    public GameObject visualLayer;

    [Header("══ Animación (opcional) ══")]
    [Tooltip("Animator del hijo AnimatedLayer. Puede quedar vacío si no hay animación.")]
    public Animator sourceAnimator;

    [Tooltip("Nombre del Trigger en el Animator. Ej: 'IceCreamSelect', 'BowlPour'.")]
    public string selectTrigger = "IceCreamSelect";

    [Tooltip("Duración de la animación antes de que el ítem aparezca en el cursor.")]
    public float animationDuration = 0.3f;

    [Tooltip("TRUE = ítem aparece durante la animación (más responsivo).\n" +
             "FALSE = ítem aparece al final de la animación.")]
    public bool spawnDuringAnimation = true;

    [Header("══ FX ══")]
    [Tooltip("Partículas opcionales al seleccionar.")]
    public ParticleSystem selectParticle;

    // ─── Estado interno ─────────────────────────────────────────────
    private bool _isSpawning = false;

    // IItemSource
    public ItemType ProducedItemType => producedItemType;

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        // Ocultar la capa visual de la cuchara desde el primer frame.
        // Permanece oculta hasta que el jugador hace click.
        if (visualLayer != null)
            visualLayer.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════════════
    // IItemSource — spawnea el ítem prefab
    // ═══════════════════════════════════════════════════════════════

    public DraggableItem SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogError($"[ItemSource] '{gameObject.name}': itemPrefab no asignado para {producedItemType}.");
            return null;
        }

        // Spawnear en la posición del cursor (se ajusta inmediatamente en FollowCursor)
        Vector3 spawnPos = GetCursorWorldPos();
        GameObject go = Instantiate(itemPrefab, spawnPos, Quaternion.identity);

        DraggableItem item = go.GetComponent<DraggableItem>();
        if (item == null)
        {
            Debug.LogError($"[ItemSource] El prefab '{itemPrefab.name}' no tiene DraggableItem. " +
                           "El prefab debe contener SOLO SpriteRenderer + Collider2D + DraggableItem.");
            Destroy(go);
            return null;
        }

        // Forzar configuración Modo A — cursor-follow desechable
        item.itemType = producedItemType;
        item.persistentDrag = false;
        item.destroyOnFailedDrop = true;

        return item;
    }

    // IItemReceiver — las fuentes NO aceptan ítems
    public bool CanReceive(DraggableItem item) => false;
    public void ReceiveItem(DraggableItem item) { }

    // ═══════════════════════════════════════════════════════════════
    // INPUT — click sobre la fuente
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        // Marcar antes de cualquier retorno temprano, para que DragManager no
        // procese el mismo click por segunda vez en HandleWorldClick().
        DragManager.Instance?.MarkClickHandled();

        if (_isSpawning) return;

        // Si el jugador ya lleva algo, ignorar
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;

        StartCoroutine(SelectSequence());
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIA — animación → spawn → drag
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator SelectSequence()
    {
        _isSpawning = true;

        // Mostrar la capa visual de la cuchara mientras dura la animación
        if (visualLayer != null)
            visualLayer.SetActive(true);

        // Notificar al WaffleMixAnimatorSync si existe (para la mezcla de waffle)
        GetComponent<WaffleMixAnimatorSync>()?.NotifySelected();

        // Disparar animación del Animator hijo
        if (sourceAnimator != null && !string.IsNullOrEmpty(selectTrigger))
            sourceAnimator.SetTrigger(selectTrigger);

        // Partículas opcionales
        if (selectParticle != null)
            selectParticle.Play();

        DraggableItem spawned = null;

        if (spawnDuringAnimation)
        {
            // Modo responsivo: el ítem aparece inmediatamente, la animación es cosmética
            spawned = SpawnItem();
            yield return new WaitForSeconds(animationDuration);
        }
        else
        {
            // Modo clásico: esperar al final de la animación
            yield return new WaitForSeconds(animationDuration);
            spawned = SpawnItem();
        }

        // Ocultar la capa visual de la cuchara — el ítem ya está en el cursor
        if (visualLayer != null)
            visualLayer.SetActive(false);

        if (spawned != null)
        {
            Debug.Log($"[ItemSource] '{gameObject.name}' → spawneado '{spawned.name}', entregando al DragManager.");
            DragManager.Instance?.OnItemPickedUp(spawned);
        }
        else
        {
            Debug.LogWarning($"[ItemSource] '{gameObject.name}' → SpawnItem() devolvió null.");
        }

        _isSpawning = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private Vector3 GetCursorWorldPos()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        p.z = 0f;
        return p;
    }
}