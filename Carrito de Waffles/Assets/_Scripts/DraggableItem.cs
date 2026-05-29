using UnityEngine;
using System.Collections;

/// <summary>
/// DRAGGABLE ITEM v8 — Fix de holdToDrag + doble-release en DeliveryPlatform.
///
/// CAMBIOS v8:
///   - Añadido campo holdToDrag (bool). En MODO B, el objeto sigue el cursor
///     mientras el botón izquierdo esté presionado; al soltar el botón
///     intenta entregar en el receptor bajo el cursor.
///   - TryDeliverToTarget() ahora NO llama NotifySuccessfulPlacement()
///     para DeliveryPlatform — eso lo hace DeliveryPlatform.ReceiveItem()
///     directamente, evitando el doble marcado de _releaseHandledByReceiver.
/// </summary>
public class DraggableItem : MonoBehaviour
{
    [Header("Configuración del Ítem")]
    public ItemType itemType;
    public bool isDraggable = true;

    [Header("Modo de drag")]
    [Tooltip("FALSE = Cursor-Follow Disposable (helado, miel, mezcla).\n" +
             "TRUE  = Persistent Drag (Plate, Waffle). Queda donde se suelta.")]
    public bool persistentDrag = false;

    [Tooltip("TRUE = MODO B: sigue el cursor SOLO mientras el botón izquierdo\n" +
             "está presionado. Al soltar entrega o regresa al origen.\n" +
             "FALSE = MODO A: sigue al cursor hasta el siguiente click.")]
    public bool holdToDrag = false;

    [Header("Comportamiento de drop fallido")]
    [Tooltip("TRUE = se destruye si no hay receptor.\nFALSE = vuelve al origen.")]
    public bool destroyOnFailedDrop = false;

    [Header("Visual")]
    public int carrySortingOrder = 50;

    [Header("AnimatorMixWaffle")]
    public WaffleMixAnimatorSync ownerMixAnimator;

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
    private bool _releaseHandledByReceiver = false;

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

        if (_justPickedUp)
        {
            _justPickedUp = false;
            FollowCursor();
            return;
        }

        FollowCursor();

        if (holdToDrag)
        {
            // MODO B: entrega cuando el jugador suelta el botón
            if (Input.GetMouseButtonUp(0))
                TryDeliverToTarget();
        }
        else
        {
            // MODO A: entrega en el siguiente click
            if (Input.GetMouseButtonDown(0))
                TryDeliverToTarget();
        }

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
            if (hit == _ownCollider || hit.gameObject == gameObject) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (carriedItem != null &&
                (hit.gameObject == carriedItem.gameObject ||
                 hit.transform.IsChildOf(carriedItem.transform))) continue;

            GameObject hitGO = hit.gameObject;
            Debug.Log($"[DraggableItem]   hit: '{hitGO.name}'");

            IItemReceiver receiver = hitGO.GetComponent<IItemReceiver>()
                                  ?? hitGO.GetComponentInParent<IItemReceiver>();

            if (receiver == null)
            {
                DraggableItem di = hitGO.GetComponent<DraggableItem>()
                                ?? hitGO.GetComponentInParent<DraggableItem>();
                if (di != null) receiver = di.GetComponent<IItemReceiver>();
                if (receiver == null) continue;
            }

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

            _releaseHandledByReceiver = false;

            // FIX: NotifySuccessfulPlacement se llama SOLO si el receiver NO es
            // DeliveryPlatform (DeliveryPlatform lo llama él mismo en ReceiveItem).
            // Para los demás receptores, notificamos aquí como antes.
            if (!(bestReceiver is DeliveryPlatform))
                DragManager.Instance?.NotifySuccessfulPlacement(this, bestReceiver);

            bestReceiver.ReceiveItem(this);

            if (!_releaseHandledByReceiver)
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

    public void MarkReleaseHandledByReceiver()
    {
        _releaseHandledByReceiver = true;
    }

    // ═════════════════════════════════════════════════════════════
    // DROP FALLIDO
    // ═════════════════════════════════════════════════════════════

    private void HandleFailedDrop()
    {
        StopCarrying();

        if (destroyOnFailedDrop)
        {
            Destroy(gameObject);
            return;
        }

        if (_originOven != null)
        {
            _originOven.ReturnWaffle(this);
            return;
        }

        if (persistentDrag)
        {
            _originPosition = transform.position;
        }
        else
        {
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

    private void ApplySortingOrder(int order)
    {
        if (_spriteRenderer != null)
            _spriteRenderer.sortingOrder = order;
        else
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                sr.sortingOrder = order;
    }

    private IEnumerator BounceAnimation()
    {
        Vector3 original = transform.localScale;
        float elapsed = 0f;
        float duration = 0.12f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float bounce = 1f + Mathf.Sin(elapsed / duration * Mathf.PI) * 0.15f;
            transform.localScale = original * bounce;
            yield return null;
        }
        transform.localScale = original;
    }

    public void ResetState()
    {
        StopAllCoroutines();

        _isBeingCarried = false;
        _justPickedUp = false;
        _releaseHandledByReceiver = false;

        _currentSlot = null;

        ApplySortingOrder(_originalSortingOrder);

        transform.localScale = Vector3.one;
    }
}