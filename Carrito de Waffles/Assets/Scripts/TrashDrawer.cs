using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// CAJÓN DE BASURA — mecánica inspirada en Papa's Series.
///
/// Al presionar X (o el botón de basura del HUD), un panel desliza desde
/// el lado derecho de la pantalla. El jugador arrastra el plato/ítem
/// al interior del panel para desecharlo.
/// Presionar X de nuevo (o soltar fuera) cierra el cajón.
///
/// JERARQUÍA EN EL CANVAS (Screen Space - Overlay):
///   TrashDrawer                           ← este script
///   └── DrawerPanel   (RectTransform)     ← el panel que desliza
///       ├── Background  (Image)           ← fondo semitransparente del cajón
///       ├── TrashZone   (Image + script TrashDropZone)  ← zona de drop
///       │   └── TrashIcon  (Image)        ← ícono de papelera
///       ├── Label       (TMP)             ← "Tirar aquí"
///       └── XButton     (Button)          ← botón de cierre (también la X del HUD)
///
/// POSICIONES (referencia 1920×1080, anchor derecha):
///   Panel cerrado: anchoredPosition.x = +350  (fuera de pantalla por la derecha)
///   Panel abierto: anchoredPosition.x =  -10  (visible al borde derecho)
///   Panel size:    (320, 400)
/// </summary>
public class TrashDrawer : MonoBehaviour
{
    public static TrashDrawer Instance { get; private set; }

    [Header("══ Animación del cajón ══")]
    public RectTransform drawerPanel;
    [Tooltip("Posición X (anchoredPosition) cuando el cajón está CERRADO (fuera de pantalla)")]
    public float closedX  =  350f;
    [Tooltip("Posición X (anchoredPosition) cuando el cajón está ABIERTO")]
    public float openX    = -10f;
    [Tooltip("Velocidad del deslizamiento")]
    public float slideSpeed = 1800f;

    [Header("══ Zona de drop ══")]
    public TrashDropZone dropZone;      // El área donde se suelta el ítem para tirarlo
    public Image         dropZoneImage;
    public Color         dropZoneIdle      = new Color(0.9f, 0.4f, 0.3f, 0.6f);
    public Color         dropZoneHighlight = new Color(0.9f, 0.2f, 0.1f, 0.9f);

    [Header("══ Tecla de apertura ══")]
    public KeyCode toggleKey = KeyCode.X;

    // ─── Estado ───────────────────────────────────────────────────
    private bool _isOpen   = false;
    private bool _isMoving = false;
    private Coroutine _slideCoroutine;

    public bool IsOpen => _isOpen;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Asegurarse de que empieza cerrado
        if (drawerPanel != null)
        {
            Vector2 pos = drawerPanel.anchoredPosition;
            pos.x = closedX;
            drawerPanel.anchoredPosition = pos;
        }

        SetDropZoneColor(dropZoneIdle);
    }

    void Update()
    {
        // Tecla X para abrir/cerrar
        if (Input.GetKeyDown(toggleKey))
            Toggle();

        // Si hay un ítem siendo arrastrado y el cajón está cerrado,
        // mostrar hint visual de que X abre el cajón
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem && !_isOpen)
            ShowKeyHint(true);
        else
            ShowKeyHint(false);
    }

    // ─────────────────────────────────────────────────────────────
    // API PÚBLICA
    // ─────────────────────────────────────────────────────────────

    public void Toggle()
    {
        if (_isOpen) Close();
        else         Open();
    }

    public void Open()
    {
        if (_isOpen || _isMoving) return;
        _isOpen = true;

        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlideTo(openX));

        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced); // Sonido de cajón abriéndose
    }

    public void Close()
    {
        if (!_isOpen || _isMoving) return;
        _isOpen = false;

        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlideTo(closedX));

        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);
    }

    /// <summary>
    /// Llamado desde TrashDropZone cuando un ítem se suelta dentro del cajón.
    /// </summary>
    public void ItemDroppedInTrash(DraggableItem item)
    {
        if (item == null) return;

        StartCoroutine(TrashSequence(item));
    }

    // ─────────────────────────────────────────────────────────────
    // HIGHLIGHT cuando un ítem se arrastra sobre el cajón
    // ─────────────────────────────────────────────────────────────

    public void OnItemHoverEnter() => SetDropZoneColor(dropZoneHighlight);
    public void OnItemHoverExit()  => SetDropZoneColor(dropZoneIdle);

    // ─────────────────────────────────────────────────────────────
    // SECUENCIAS
    // ─────────────────────────────────────────────────────────────

    private IEnumerator SlideTo(float targetX)
    {
        _isMoving = true;

        while (drawerPanel != null)
        {
            Vector2 pos = drawerPanel.anchoredPosition;
            pos.x = Mathf.MoveTowards(pos.x, targetX, slideSpeed * Time.deltaTime);
            drawerPanel.anchoredPosition = pos;

            if (Mathf.Abs(pos.x - targetX) < 0.5f)
            {
                pos.x = targetX;
                drawerPanel.anchoredPosition = pos;
                break;
            }

            yield return null;
        }

        _isMoving = false;

        // Cerrar automáticamente tras tirar
        if (_isOpen && drawerPanel != null)
        {
            // El cajón permanece abierto hasta que el jugador lo cierre con X
        }
    }

    private IEnumerator TrashSequence(DraggableItem item)
    {
        // Animación: el ítem "cae" al centro del cajón y desaparece
        Vector3 startPos  = item.transform.position;
        Vector3 targetPos = dropZone != null
            ? dropZone.transform.position
            : transform.position;

        float elapsed  = 0f;
        float duration = 0.25f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);
            if (item != null)
            {
                item.transform.position   = Vector3.Lerp(startPos, targetPos, t);
                item.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t);
            }
            yield return null;
        }

        if (item != null) Object.Destroy(item.gameObject);

        AudioManager.Instance?.PlaySound(SoundType.InvalidAction); // Sonido de cubo metálico

        // Cerrar el cajón tras tirar el ítem
        yield return new WaitForSeconds(0.3f);
        Close();
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS VISUALES
    // ─────────────────────────────────────────────────────────────

    private void SetDropZoneColor(Color c)
    {
        if (dropZoneImage != null) dropZoneImage.color = c;
    }

    [Header("══ Hint de tecla ══")]
    public GameObject keyHintObject; // "Presiona X para tirar" — texto/icono flotante

    private void ShowKeyHint(bool show)
    {
        if (keyHintObject != null && keyHintObject.activeSelf != show)
            keyHintObject.SetActive(show);
    }
}

// ─────────────────────────────────────────────────────────────────────
// TRASH DROP ZONE — el área receptora dentro del cajón
// ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Componente separado para la zona de drop del cajón.
/// Actúa como IItemReceiver para el sistema de drag existente.
/// </summary>
public class TrashDropZone : MonoBehaviour, IItemReceiver
{
    public bool CanReceive(DraggableItem item) => item != null && TrashDrawer.Instance != null && TrashDrawer.Instance.IsOpen;

    public void ReceiveItem(DraggableItem item)
    {
        TrashDrawer.Instance?.ItemDroppedInTrash(item);
    }

    // Highlight al arrastrar encima
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<DraggableItem>() != null)
            TrashDrawer.Instance?.OnItemHoverEnter();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<DraggableItem>() != null)
            TrashDrawer.Instance?.OnItemHoverExit();
    }
}
