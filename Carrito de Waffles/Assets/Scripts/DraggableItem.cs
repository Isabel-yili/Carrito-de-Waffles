using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// NÚCLEO MÍNIMO — DraggableItem
/// Permite tomar y soltar cualquier ítem de cocina con el mouse.
/// Toda la interacción del juego pasa por aquí.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [Header("Configuración del Ítem")]
    public ItemType itemType;
    public bool isDraggable = true;

    [Header("Visual")]
    [Tooltip("Escala al arrastrar (feedback visual de 'tomar' el objeto)")]
    public float dragScale = 1.1f;
    [Tooltip("Opacidad del ícono mientras arrastra")]
    [Range(0f, 1f)] public float dragAlpha = 0.8f;

    // Referencias privadas
    private Canvas _canvas;
    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private Vector2 _originalPosition;
    private Transform _originalParent;
    private int _originalSortingOrder;
    private SpriteRenderer _spriteRenderer;
    private Camera _mainCamera;

    // Estado del drag
    private bool _isDragging = false;
    private Vector3 _dragOffset;

    // Referencia al ItemHolder si está dentro de uno
    private ItemSlot _currentSlot;

    void Awake()
    {
        _mainCamera = Camera.main;
        _spriteRenderer = GetComponent<SpriteRenderer>();

        // Buscar canvas para items de UI, si existe
        _canvas = FindObjectOfType<Canvas>();
    }

    void Start()
    {
        _originalPosition = transform.position;
        _originalParent = transform.parent;
    }

    // ─────────────────────────────────────────
    // DRAG HANDLERS
    // ─────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;
        _isDragging = true;

        // Guardar slot actual
        _currentSlot = GetComponentInParent<ItemSlot>();

        // Feedback visual: scale up + transparencia leve
        transform.localScale *= dragScale;
        if (_spriteRenderer != null)
        {
            Color c = _spriteRenderer.color;
            c.a = dragAlpha;
            _spriteRenderer.color = c;
        }

        // Elevar sorting order para que aparezca encima de todo
        if (_spriteRenderer != null)
        {
            _originalSortingOrder = _spriteRenderer.sortingOrder;
            _spriteRenderer.sortingOrder = 100;
        }

        // Offset para que el drag no "salte" al centro del sprite
        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(eventData.position);
        mouseWorld.z = 0;
        _dragOffset = transform.position - mouseWorld;

        // Notificar al sistema
        DragManager.Instance?.OnItemPickedUp(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;

        // Seguir el mouse suavemente
        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(eventData.position);
        mouseWorld.z = 0;
        transform.position = mouseWorld + _dragOffset;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;
        _isDragging = false;

        // Restaurar visual
        transform.localScale = Vector3.one;
        if (_spriteRenderer != null)
        {
            Color c = _spriteRenderer.color;
            c.a = 1f;
            _spriteRenderer.color = c;
            _spriteRenderer.sortingOrder = _originalSortingOrder;
        }

        // Intentar soltar en un receptor válido
        bool dropped = TryDropOnTarget(eventData);

        if (!dropped)
        {
            // Si no hay receptor válido → volver al origen con animación
            ReturnToOrigin();
        }

        DragManager.Instance?.OnItemReleased(this);
    }

    // ─────────────────────────────────────────
    // CLICK (alternativa al drag para accesibilidad)
    // ─────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        // Si ya hay un item seleccionado en el DragManager, intentar combinar
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
        {
            DragManager.Instance.TryInteractWith(this);
        }
        else if (!_isDragging)
        {
            // Seleccionar este item (modo click-to-move)
            DragManager.Instance?.SelectItem(this);
            // Feedback visual de selección
            PlaySelectionFeedback();
        }
    }

    // ─────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────

    private bool TryDropOnTarget(PointerEventData eventData)
    {
        // Raycast para encontrar receptores bajo el mouse
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (var result in results)
        {
            if (result.gameObject == gameObject) continue;

            IItemReceiver receiver = result.gameObject.GetComponent<IItemReceiver>();
            if (receiver != null && receiver.CanReceive(this))
            {
                receiver.ReceiveItem(this);
                return true;
            }
        }

        // Física 2D como fallback
        Vector2 worldPos = _mainCamera.ScreenToWorldPoint(eventData.position);
        Collider2D hit = Physics2D.OverlapPoint(worldPos);
        if (hit != null && hit.gameObject != gameObject)
        {
            IItemReceiver receiver = hit.GetComponent<IItemReceiver>();
            if (receiver != null && receiver.CanReceive(this))
            {
                receiver.ReceiveItem(this);
                return true;
            }
        }

        return false;
    }

    public void ReturnToOrigin()
    {
        StartCoroutine(AnimateReturn());
    }

    private System.Collections.IEnumerator AnimateReturn()
    {
        Vector3 startPos = transform.position;
        float elapsed = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);
            transform.position = Vector3.Lerp(startPos, _originalPosition, t);
            yield return null;
        }

        transform.position = _originalPosition;
    }

    private void PlaySelectionFeedback()
    {
        // Pequeño "bounce" de selección
        StartCoroutine(BounceAnimation());
        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);
    }

    private System.Collections.IEnumerator BounceAnimation()
    {
        float elapsed = 0f;
        float duration = 0.15f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float bounce = 1f + Mathf.Sin(t * Mathf.PI) * 0.2f;
            transform.localScale = Vector3.one * bounce;
            yield return null;
        }

        transform.localScale = Vector3.one;
    }

    public void SetSlot(ItemSlot slot)
    {
        _currentSlot = slot;
        if (slot != null)
            _originalPosition = slot.transform.position;
    }
}
