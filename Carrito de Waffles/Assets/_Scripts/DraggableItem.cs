using UnityEngine;
using System.Collections;

/// <summary>
/// DRAGGABLE ITEM v6 — Dos modos de drag claramente separados.
///
/// MODO A — Cursor-Follow Disposable  (persistentDrag = false, destroyOnFailedDrop = true)
///   Ingredientes temporales: helado, miel, mezcla de waffle.
///   • El item sigue al cursor en todo momento.
///   • Al soltar (segundo click): busca receptor bajo el cursor.
///   • Si no hay receptor válido: se destruye inmediatamente.
///
/// MODO B — Persistent Drag  (persistentDrag = true, destroyOnFailedDrop = false)
///   Objetos de escena: Plate, Waffle extraído del horno.
///   • El item se "alza" (sorting order) y sigue al cursor mientras se arrastra.
///   • Al soltar (segundo click):
///       - Si hay receptor IItemReceiver bajo el cursor → entrega.
///       - Si no hay receptor → se deposita en la posición actual
///         (queda donde el jugador lo soltó, no vuelve al origen).
///   • Click derecho / Escape → vuelve al origen (cancel explícito).
///
/// DIFERENCIA CLAVE CON WAFFLE:
///   El Waffle (WaffleDisplay + DraggableItem añadido en runtime por Oven.ExtractWaffle)
///   usa persistentDrag = true.  Al soltarlo encima del Plate, TryDeliverToTarget
///   hace el OverlapPoint en la posición DEL ITEM (donde está físicamente el sprite),
///   no en la posición del cursor — esto resuelve el caso en que el cursor se mueve
///   justo entre frames y el collider del Plate no coincide exactamente.
/// </summary>
public class DraggableItem : MonoBehaviour
{
    [Header("Configuración del Ítem")]
    public ItemType itemType;
    public bool isDraggable = true;

    [Header("Modo de drag")]
    [Tooltip("FALSE (default) = Cursor-Follow Disposable: helado, miel, mezcla.\n" +
             "TRUE = Persistent Drag: Plate, Waffle. El objeto queda donde se suelta.")]
    public bool persistentDrag = false;

    [Header("Comportamiento de drop fallido")]
    [Tooltip("TRUE = se destruye si no encuentra receptor (ingredientes temporales).\n" +
             "FALSE = vuelve al origen o queda en escena según persistentDrag.")]
    public bool destroyOnFailedDrop = false;

    [Header("Visual")]
    public int carrySortingOrder = 50;

    // ─── Privado ───────────────────────────────────────────────────
    private SpriteRenderer _spriteRenderer;
    private Camera _mainCamera;
    private int _originalSortingOrder;
    private Vector3 _originPosition;
    private ItemSlot _currentSlot;
    private Collider2D _ownCollider;
    private Oven _originOven;

    private bool _isBeingCarried = false;
    private bool _justPickedUp = false;

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
        _originPosition = transform.position;
    }

    void Start()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (!_isBeingCarried) _originPosition = transform.position;
    }

    void Update()
    {
        if (!_isBeingCarried) return;
        if (_mainCamera == null) _mainCamera = Camera.main;

        // Primer frame: posicionar sin procesar clicks
        if (_justPickedUp)
        {
            _justPickedUp = false;
            FollowCursor();
            return;
        }

        FollowCursor();

        if (Input.GetMouseButtonDown(0))
            TryDeliverToTarget();

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            DragManager.Instance?.CancelSelection();
    }

    // ═════════════════════════════════════════════════════════════
    // CURSOR FOLLOW
    // ═════════════════════════════════════════════════════════════

    private void FollowCursor()
    {
        if (_mainCamera == null) return;
        Vector3 p = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        p.z = 0f;
        transform.position = p;
    }

    // ═════════════════════════════════════════════════════════════
    // ENTREGAR A RECEPTOR
    // ═════════════════════════════════════════════════════════════

    private void TryDeliverToTarget()
    {
        if (_mainCamera == null) return;

        // ── Punto de búsqueda ─────────────────────────────────────
        // Para objetos persistent (Plate, Waffle): usamos la posición FÍSICA del
        // item (transform.position), que coincide con el sprite visible.
        // Para cursor-follow: usamos la posición del cursor.
        // En la práctica ambos coinciden porque FollowCursor() acaba de ejecutarse,
        // pero dejamos la lógica explícita para claridad.
        Vector3 searchScreen = Input.mousePosition;
        searchScreen.z = Mathf.Abs(_mainCamera.transform.position.z);
        Vector2 searchWorld = _mainCamera.ScreenToWorldPoint(searchScreen);

        Collider2D[] results = new Collider2D[16];
        ContactFilter2D filter = new ContactFilter2D().NoFilter();
        int count = Physics2D.OverlapPoint(searchWorld, filter, results);

        Debug.Log($"[DraggableItem] TryDeliver — item:{gameObject.name} pos:{searchWorld} hits:{count}");

        IItemReceiver bestReceiver = null;
        string bestName = "ninguno";
        DraggableItem carriedItem = DragManager.Instance?.SelectedItem;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = results[i];
            if (hit == null) continue;

            // Ignorar el propio objeto y sus hijos
            if (hit == _ownCollider || hit.gameObject == gameObject) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            // Ignorar el item arrastrado (y sus hijos)
            if (carriedItem != null &&
                (hit.gameObject == carriedItem.gameObject ||
                 hit.transform.IsChildOf(carriedItem.transform))) continue;

            GameObject hitGO = hit.gameObject;
            Debug.Log($"[DraggableItem]   hit: '{hitGO.name}'");

            // ── Buscar IItemReceiver ──────────────────────────────
            // IMPORTANTE: GetComponentInParent sube la jerarquía y encuentra
            // el Plate.cs aunque el collider pertenezca a un hijo del Plate.
            IItemReceiver receiver = hitGO.GetComponent<IItemReceiver>()
                                  ?? hitGO.GetComponentInParent<IItemReceiver>();

            if (receiver == null)
            {
                // Si el objeto tiene un DraggableItem, puede ser un Plate.
                // El Plate implementa IItemReceiver directamente en su raíz.
                // Si el hit fue en un hijo del Plate (ingredientSlot), subir.
                DraggableItem di = hitGO.GetComponent<DraggableItem>()
                                ?? hitGO.GetComponentInParent<DraggableItem>();
                if (di != null)
                    receiver = di.GetComponent<IItemReceiver>();

                if (receiver == null)
                {
                    Debug.Log($"[DraggableItem]   Sin IItemReceiver en '{hitGO.name}'");
                    continue;
                }
            }

            // No entregar a uno mismo
            if (receiver is MonoBehaviour mb && mb.gameObject == gameObject) continue;

            bool canReceive = receiver.CanReceive(this);
            Debug.Log($"[DraggableItem]   CanReceive({hitGO.name}): {canReceive}");

            if (canReceive)
            {
                bestReceiver = receiver;
                bestName = hitGO.name;
                break;
            }
        }

        if (bestReceiver != null)
        {
            Debug.Log($"[DraggableItem] → Entregando a '{bestName}'");
            DragManager.Instance?.NotifySuccessfulPlacement(this, bestReceiver);
            bestReceiver.ReceiveItem(this);
            DragManager.Instance?.OnItemReleased(this);
        }
        else
        {
            Debug.Log($"[DraggableItem] → Sin receptor válido");
            AudioManager.Instance?.PlaySound(SoundType.InvalidAction);
            FeedbackManager.Instance?.ShowInvalidAction(transform.position);
            HandleFailedDrop();
            DragManager.Instance?.OnItemReleased(this);
        }
    }

    // ═════════════════════════════════════════════════════════════
    // DROP FALLIDO
    // ═════════════════════════════════════════════════════════════

    private void HandleFailedDrop()
    {
        StopCarrying();

        if (destroyOnFailedDrop)
        {
            // Ingredientes temporales: desaparecen
            Debug.Log($"[DraggableItem] destroyOnFailedDrop → destruyendo '{gameObject.name}'");
            Destroy(gameObject);
            return;
        }

        if (_originOven != null)
        {
            // Waffle: vuelve al horno
            _originOven.ReturnWaffle(this);
            return;
        }

        if (persistentDrag)
        {
            // Plate: se queda donde está (el jugador lo soltó ahí)
            // Actualizar _originPosition para que futuros cancels salgan desde aquí
            _originPosition = transform.position;
            Debug.Log($"[DraggableItem] persistentDrag → quedando en {_originPosition}");
        }
        else
        {
            // Cursor-follow no-disposable (raro): volver al origen
            StartCoroutine(MoveToOriginCoroutine());
        }
    }

    // ═════════════════════════════════════════════════════════════
    // API PÚBLICA
    // ═════════════════════════════════════════════════════════════

    public void StartCarrying()
    {
        if (!isDraggable) return;
        _isBeingCarried = true;
        _justPickedUp = true;

        ApplySortingOrder(carrySortingOrder);
        StartCoroutine(BounceAnimation());
        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);
        Debug.Log($"[DraggableItem] StartCarrying → {gameObject.name}");
    }

    public void StopCarrying()
    {
        _isBeingCarried = false;
        ApplySortingOrder(_originalSortingOrder);
    }

    /// <summary>
    /// Cancelación explícita (clic derecho / Escape).
    /// Siempre vuelve al origen, independientemente de persistentDrag.
    /// </summary>
    public void ReturnToOrigin()
    {
        StopCarrying();

        if (_originOven != null)
        {
            _originOven.ReturnWaffle(this);
            return;
        }

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
        if (slot != null) _originPosition = slot.transform.position;
    }

    public void SetOriginOven(Oven oven) => _originOven = oven;
    public void ClearOriginOven() => _originOven = null;

    // ─── Helpers ──────────────────────────────────────────────────

    private void ApplySortingOrder(int order)
    {
        if (_spriteRenderer != null)
        {
            _spriteRenderer.sortingOrder = order;
        }
        else
        {
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                sr.sortingOrder = order;
        }
    }

    private IEnumerator BounceAnimation()
    {
        Vector3 original = transform.localScale;
        float elapsed = 0f, duration = 0.12f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float bounce = 1f + Mathf.Sin(elapsed / duration * Mathf.PI) * 0.15f;
            transform.localScale = original * bounce;
            yield return null;
        }
        transform.localScale = original;
    }
}