using UnityEngine;
using System.Collections;

/// <summary>
/// DRAGGABLE ITEM — Sistema click-to-carry para objetos 2D con SpriteRenderer.
///
/// REGLAS DE ARRASTRE (GDD actualizado):
///   - Solo los Waffles (WaffleReady, WaffleOvercooked) y los Plates con
///     receta completa pueden arrastrarse libremente.
///   - Al soltar un Waffle fuera de un Plate válido, vuelve al Oven de origen.
///   - Al soltar un Plate fuera de la DeliveryPlatform, vuelve a su posición
///     original en la mesa.
///
/// DETECCIÓN DE RECEPTORES:
///   Usa Physics2D.OverlapPoint con ContactFilter2D.NoFilter() para detectar
///   todos los colliders en el punto del cursor, sin importar layers, triggers
///   o la Collision Matrix de Physics2D.
///
/// SETUP DEL PREFAB:
///   - SpriteRenderer con el sprite del ítem
///   - Collider2D con IS TRIGGER = true
///   - Este script, sin Rigidbody2D
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class DraggableItem : MonoBehaviour
{
    [Header("Configuración del Ítem")]
    public ItemType itemType;
    public bool isDraggable = true;

    [Header("Visual")]
    [Tooltip("Sorting Order mientras está siendo cargado")]
    public int carrySortingOrder = 50;

    // ─── Referencias privadas ─────────────────────────────────────
    private SpriteRenderer _spriteRenderer;
    private Camera _mainCamera;
    private int _originalSortingOrder;
    private Vector3 _originPosition;   // Posición a la que vuelve si el drop falla
    private ItemSlot _currentSlot;
    private Collider2D _ownCollider;

    // ─── Origen tipado — para devolver el waffle al horno ─────────
    /// <summary>
    /// Si este item es un Waffle extraído de un horno, se asigna aquí
    /// para poder devolverlo si el jugador no lo coloca en un Plate.
    /// </summary>
    private Oven _originOven;

    // ─── Estado ───────────────────────────────────────────────────
    private bool _isBeingCarried = false;

    public bool IsBeingCarried => _isBeingCarried;

    // ═════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        _mainCamera = Camera.main;
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _ownCollider = GetComponent<Collider2D>();
        _originalSortingOrder = _spriteRenderer != null ? _spriteRenderer.sortingOrder : 0;

        if (_ownCollider != null) _ownCollider.isTrigger = true;
    }

    void Start()
    {
        _originPosition = transform.position;
    }

    void Update()
    {
        if (!_isBeingCarried) return;

        FollowCursor();

        if (Input.GetMouseButtonDown(0))
            TryDeliverToTarget();

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            DragManager.Instance?.CancelSelection();
    }

    // ═════════════════════════════════════════════════════════════
    // SEGUIR AL CURSOR
    // ═════════════════════════════════════════════════════════════

    private void FollowCursor()
    {
        if (_mainCamera == null) return;
        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        transform.position = mouseWorld;
    }

    // ═════════════════════════════════════════════════════════════
    // INTENTAR ENTREGAR A UN RECEPTOR
    // ═════════════════════════════════════════════════════════════

    private void TryDeliverToTarget()
    {
        if (_mainCamera == null) return;

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = Mathf.Abs(_mainCamera.transform.position.z);
        Vector2 mouseWorld2D = _mainCamera.ScreenToWorldPoint(mouseScreen);

        Collider2D[] results = new Collider2D[16];
        ContactFilter2D filter = new ContactFilter2D().NoFilter();
        int count = Physics2D.OverlapPoint(mouseWorld2D, filter, results);

        Debug.Log($"[DraggableItem] Click en: {mouseWorld2D} | colliders encontrados: {count}");

        IItemReceiver bestReceiver = null;
        string bestName = "ninguno";

        // Obtener el ítem actualmente seleccionado por el DragManager para excluirlo
        // (evita que el waffle arrastrado se detecte como su propio receptor)
        DraggableItem carriedItem = DragManager.Instance?.SelectedItem;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = results[i];

            // Ignorar el propio collider de este ítem
            if (hit == _ownCollider || hit.gameObject == gameObject) continue;

            // Ignorar cualquier collider que pertenezca al ítem siendo arrastrado
            // (puede ser distinto a este si se llama desde otro contexto)
            if (carriedItem != null && hit.gameObject == carriedItem.gameObject) continue;

            GameObject hitGO = hit.gameObject;
            Debug.Log($"[DraggableItem]   → hit: '{hitGO.name}' layer: '{LayerMask.LayerToName(hitGO.layer)}'");

            IItemReceiver receiver = hitGO.GetComponent<IItemReceiver>()
                                  ?? hitGO.GetComponentInParent<IItemReceiver>();

            if (receiver == null)
            {
                Debug.Log($"[DraggableItem]     Sin IItemReceiver en '{hitGO.name}' ni en sus padres.");
                continue;
            }

            bool canReceive = receiver.CanReceive(this);
            Debug.Log($"[DraggableItem]     IItemReceiver encontrado → CanReceive: {canReceive}");

            if (canReceive)
            {
                bestReceiver = receiver;
                bestName = hitGO.name;
                break;
            }
        }

        if (bestReceiver != null)
        {
            Debug.Log($"[DraggableItem] Entregando a: '{bestName}'");
            DragManager.Instance?.NotifySuccessfulPlacement(this, bestReceiver);
            bestReceiver.ReceiveItem(this);
            DragManager.Instance?.OnItemReleased(this);
        }
        else
        {
            // ── Drop fallido → devolver al origen ─────────────────
            Debug.Log("[DraggableItem] Sin receptor válido — devolviendo al origen.");
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
            FeedbackManager.Instance?.ShowInvalidAction(transform.position);

            // ReturnToOrigin maneja tanto el caso del horno como el del plato
            ReturnToOrigin();
            DragManager.Instance?.OnItemReleased(this);
        }
    }

    // ═════════════════════════════════════════════════════════════
    // API PÚBLICA
    // ═════════════════════════════════════════════════════════════

    public void StartCarrying()
    {
        if (!isDraggable) return;
        _isBeingCarried = true;

        if (_spriteRenderer != null)
            _spriteRenderer.sortingOrder = carrySortingOrder;

        StartCoroutine(BounceAnimation());
        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);

        Debug.Log($"[DraggableItem] StartCarrying → {gameObject.name}");
    }

    public void StopCarrying()
    {
        _isBeingCarried = false;
        if (_spriteRenderer != null)
            _spriteRenderer.sortingOrder = _originalSortingOrder;
    }

    /// <summary>
    /// Devuelve el ítem a su posición de origen sin destruirlo.
    ///
    /// Si el item es un Waffle que vino de un horno:
    ///   → llama Oven.ReturnWaffle() para que el horno recobre su estado y
    ///     destruya este GameObject (el horno gestiona su propio sprite).
    ///
    /// Para el Plate u otros ítems:
    ///   → anima el movimiento de vuelta a la posición original.
    /// </summary>
    public void ReturnToOrigin()
    {
        StopCarrying();

        if (_originOven != null)
        {
            // El horno se encarga de restaurar su estado y destruir este objeto
            _originOven.ReturnWaffle(this);
            return;
        }

        // Plate u otros: interpolar de vuelta a la posición guardada
        StartCoroutine(MoveToOriginCoroutine());
    }

    private IEnumerator MoveToOriginCoroutine()
    {
        float elapsed = 0f;
        float duration = 0.2f;
        Vector3 start = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, _originPosition, elapsed / duration);
            yield return null;
        }

        transform.position = _originPosition;
    }

    public void SetSlot(ItemSlot slot)
    {
        _currentSlot = slot;
        if (slot != null)
            _originPosition = slot.transform.position;
    }

    /// <summary>
    /// Registra el horno de origen. Llamar desde Oven al extraer el waffle.
    /// </summary>
    public void SetOriginOven(Oven oven)
    {
        _originOven = oven;
    }

    /// <summary>
    /// Limpia la referencia al horno de origen.
    /// Llamar cuando el waffle se coloca exitosamente en un Plate.
    /// </summary>
    public void ClearOriginOven()
    {
        _originOven = null;
    }

    // ═════════════════════════════════════════════════════════════
    // VISUAL
    // ═════════════════════════════════════════════════════════════

    private IEnumerator BounceAnimation()
    {
        float elapsed = 0f;
        float duration = 0.12f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float bounce = 1f + Mathf.Sin(t * Mathf.PI) * 0.15f;
            transform.localScale = Vector3.one * bounce;
            yield return null;
        }

        transform.localScale = Vector3.one;
    }
}