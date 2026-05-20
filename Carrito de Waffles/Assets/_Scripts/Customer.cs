using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// ═══════════════════════════════════════════════════════════════════
// ESTADO DE ÁNIMO — escala de 5 emociones
// ═══════════════════════════════════════════════════════════════════
public enum CustomerMood
{
    Ecstatic  = 4,   // :D  — superfeliz, llega con mucha paciencia
    Happy     = 3,   // :)  — feliz, paciencia normal
    Neutral   = 2,   // :/  — neutral, menos paciencia
    Annoyed   = 1,   // :(  — molesto, poca paciencia
    Furious   = 0    // >:( — furioso, mínima paciencia
}

/// <summary>
/// CLIENTE — sistema completo de comportamiento visual.
///
/// Cada cliente:
///   - Llega con un mood inicial (Ecstatic o Happy — nunca enojado al llegar)
///   - Tiene un timer de paciencia que baja con el tiempo
///   - El mood se degrada conforme baja la paciencia
///   - Muestra un globo de texto con el sprite del pedido
///   - Al hacer hover, muestra un panel translúcido con slider + carita
///   - Al recibir pedido correcto → globo verde + animación de salida
///   - Al recibir pedido incorrecto → globo rojo + el cliente se va
///   - Los clientes llegan desde izquierda o derecha de pantalla
///
/// JERARQUÍA DEL PREFAB:
///   Customer  (este script + Collider2D para hover)
///   ├── Body              SpriteRenderer — sprite del cliente (Procreate)
///   ├── OrderBubble       SpriteRenderer — globo de texto
///   │   └── OrderIcon     SpriteRenderer — sprite del pedido solicitado
///   └── MoodPanel         (Canvas World Space — aparece en hover)
///       ├── Background    Image (translúcido, color = #00000088)
///       ├── MoodSlider    Slider — 0 a 100, barra de estado de ánimo
///       │   └── Fill      Image — color cambia con el mood
///       └── MoodFaceText  TMP — la carita ":D" ":)" ":/" ":(" ">:("
///
/// COLORES DEL GLOBO según estado:
///   Verde puro    → pedido correcto entregado
///   Rojo puro     → error (pedido entregado no coincide con nadie)
///   Normal/blanco → esperando
/// </summary>
public class Customer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────
    [Header("══ Configuración de llegada ══")]
    [Tooltip("Mood con el que llega el cliente (Ecstatic o Happy — aleatorio)")]
    public CustomerMood initialMood = CustomerMood.Happy;
    [Tooltip("Paciencia base en segundos — se sobreescribe desde OrderManager")]
    public float basePatience = 30f;
    [Tooltip("Multiplicador de paciencia según mood inicial")]
    public AnimationCurve patienceByMood = AnimationCurve.Linear(0, 0.6f, 4, 1.4f);

    [Header("══ Visuals ══")]
    public SpriteRenderer bodyRenderer;
    public SpriteRenderer orderBubbleRenderer;   // El globo
    public SpriteRenderer orderIconRenderer;      // Sprite del pedido dentro del globo
    public Animator       customerAnimator;       // Animaciones Procreate del cliente

    [Header("══ Sprites del globo ══")]
    public Sprite bubbleNormal;   // Globo blanco/neutro
    public Sprite bubbleGreen;    // Globo verde — pedido correcto
    public Sprite bubbleRed;      // Globo rojo — error o todos los globos
    public Sprite bubbleFlash;    // Frame de flash al recibir

    [Header("══ Panel de hover (mood) ══")]
    public GameObject moodPanel;             // Se activa al hacer hover
    public Slider     moodSlider;            // 0–1, representa paciencia restante
    public Image      moodSliderFill;        // Cambia de color con el mood
    public TextMeshProUGUI moodFaceText;     // ":D" ":)" ":/" ":(" ">:("
    public Image      moodPanelBackground;   // Fondo translúcido

    [Header("══ Colores del slider de mood ══")]
    public Color colorEcstatic = new Color(0.3f, 0.9f, 0.3f);   // Verde brillante
    public Color colorHappy    = new Color(0.6f, 0.9f, 0.3f);   // Verde amarillento
    public Color colorNeutral  = new Color(1.0f, 0.85f, 0.2f);  // Amarillo
    public Color colorAnnoyed  = new Color(1.0f, 0.5f,  0.1f);  // Naranja
    public Color colorFurious  = new Color(0.9f, 0.15f, 0.1f);  // Rojo

    [Header("══ Entrada desde los lados ══")]
    [Tooltip("Posición final del cliente en la escena")]
    public Vector3 targetPosition;
    [Tooltip("TRUE = entra desde la izquierda, FALSE = desde la derecha")]
    public bool enterFromLeft = true;
    [Tooltip("Distancia fuera de pantalla desde donde aparece")]
    public float entryOffscreen = 8f;
    public float moveSpeed = 4f;

    // ─── Estado interno ───────────────────────────────────────────
    private RecipeType _order;
    private float      _maxPatience;
    private float      _patience;
    private CustomerMood _currentMood;
    private bool       _isServed    = false;
    private bool       _isLeaving   = false;
    private bool       _isHovering  = false;

    // Referencia al OrderManager para notificar cuando se va
    private OrderManager _orderManager;

    public RecipeType Order         => _order;
    public float      PatienceRatio => _patience / _maxPatience;
    public bool       IsServed      => _isServed;

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN — llamado desde OrderManager al instanciar
    // ─────────────────────────────────────────────────────────────

    public void Initialize(RecipeType order, float patience, bool fromLeft, Vector3 target, OrderManager manager)
    {
        _order        = order;
        _maxPatience  = patience;
        _patience     = patience;
        _orderManager = manager;
        targetPosition = target;
        enterFromLeft  = fromLeft;

        // Mood inicial: aleatorio entre Ecstatic y Happy
        initialMood  = (CustomerMood)Random.Range(3, 5);
        _currentMood = initialMood;

        // Ajustar paciencia según mood
        float moodMultiplier = patienceByMood.Evaluate((float)initialMood);
        _patience    = _maxPatience * moodMultiplier;
        _maxPatience = _patience;

        // Configurar visual del pedido en el globo
        SetOrderIcon(order);

        // Posición inicial: fuera de pantalla
        float startX = enterFromLeft
            ? targetPosition.x - entryOffscreen
            : targetPosition.x + entryOffscreen;
        transform.position = new Vector3(startX, targetPosition.y, targetPosition.z);

        // Flip del sprite si entra desde la derecha
        if (bodyRenderer != null)
            bodyRenderer.flipX = !enterFromLeft;

        // Panel de mood oculto
        if (moodPanel != null) moodPanel.SetActive(false);

        StartCoroutine(EnterSequence());
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE — timer de paciencia
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (_isServed || _isLeaving) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;

        _patience -= Time.deltaTime;

        // Actualizar mood según paciencia restante
        UpdateMoodFromPatience();

        // Actualizar panel si está visible
        if (_isHovering) UpdateMoodPanel();

        // El cliente se va si se acaba la paciencia
        if (_patience <= 0f)
        {
            _patience = 0f;
            Leave(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MOOD — degradación según paciencia
    // ─────────────────────────────────────────────────────────────

    private void UpdateMoodFromPatience()
    {
        float ratio = PatienceRatio;
        CustomerMood newMood;

        if      (ratio > 0.75f) newMood = CustomerMood.Ecstatic;
        else if (ratio > 0.50f) newMood = CustomerMood.Happy;
        else if (ratio > 0.30f) newMood = CustomerMood.Neutral;
        else if (ratio > 0.10f) newMood = CustomerMood.Annoyed;
        else                    newMood = CustomerMood.Furious;

        // Limitar: el mood nunca puede mejorar, solo empeorar
        if ((int)newMood < (int)_currentMood)
        {
            _currentMood = newMood;
            OnMoodChanged();
        }
    }

    private void OnMoodChanged()
    {
        // Activar animación del cliente según mood
        customerAnimator?.SetInteger("Mood", (int)_currentMood);

        // Efectos extra al llegar a Furious
        if (_currentMood == CustomerMood.Furious)
            StartCoroutine(FuriousEffect());
    }

    private IEnumerator FuriousEffect()
    {
        // Pequeño temblor del cliente
        Vector3 original = transform.localPosition;
        for (int i = 0; i < 6; i++)
        {
            transform.localPosition = original + new Vector3(Random.Range(-0.05f, 0.05f), 0, 0);
            yield return new WaitForSeconds(0.05f);
        }
        transform.localPosition = original;
    }

    // ─────────────────────────────────────────────────────────────
    // PANEL DE HOVER — aparece al pasar el mouse por el cliente
    // ─────────────────────────────────────────────────────────────

    void OnMouseEnter()
    {
        _isHovering = true;
        if (moodPanel != null) moodPanel.SetActive(true);
        UpdateMoodPanel();
    }

    void OnMouseExit()
    {
        _isHovering = false;
        if (moodPanel != null) moodPanel.SetActive(false);
    }

    private void UpdateMoodPanel()
    {
        // Slider de paciencia (0 a 1)
        if (moodSlider != null)
            moodSlider.value = PatienceRatio;

        // Color del slider según mood
        if (moodSliderFill != null)
            moodSliderFill.color = GetMoodColor(_currentMood);

        // Carita de texto
        if (moodFaceText != null)
            moodFaceText.text = GetMoodFace(_currentMood);
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA DE PEDIDO
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Llamado desde OrderManager cuando se entrega el pedido correcto.
    /// </summary>
    public void ServeCorrect()
    {
        if (_isServed || _isLeaving) return;
        _isServed = true;

        SetBubbleColor(BubbleState.Correct);
        customerAnimator?.SetTrigger("Happy");
        AudioManager.Instance?.PlaySound(SoundType.CustomerHappy);

        StartCoroutine(LeaveAfterDelay(1.2f, true));
    }

    /// <summary>
    /// Llamado cuando se entrega un pedido incorrecto a cualquier cliente
    /// (todos los globos se ponen rojos).
    /// </summary>
    public void FlashError()
    {
        if (_isServed || _isLeaving) return;
        StartCoroutine(ErrorFlash());
    }

    private IEnumerator ErrorFlash()
    {
        SetBubbleColor(BubbleState.Error);
        yield return new WaitForSeconds(1.0f);
        SetBubbleColor(BubbleState.Normal);
    }

    /// <summary>
    /// El cliente se va (paciencia agotada o pedido incorrecto).
    /// </summary>
    public void Leave(bool served)
    {
        if (_isLeaving) return;
        _isLeaving = true;

        if (!served)
        {
            SetBubbleColor(BubbleState.Error);
            customerAnimator?.SetTrigger("Angry");
            AudioManager.Instance?.PlaySound(SoundType.CustomerLeave);
        }

        if (moodPanel != null) moodPanel.SetActive(false);
        _orderManager?.OnCustomerLeft(this, served);

        StartCoroutine(ExitSequence());
    }

    // ─────────────────────────────────────────────────────────────
    // ANIMACIONES DE ENTRADA Y SALIDA
    // ─────────────────────────────────────────────────────────────

    private IEnumerator EnterSequence()
    {
        customerAnimator?.SetTrigger("Walk");

        while (Vector3.Distance(transform.position, targetPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;
        customerAnimator?.SetTrigger("Idle");
    }

    private IEnumerator LeaveAfterDelay(float delay, bool served)
    {
        yield return new WaitForSeconds(delay);
        Leave(served);
    }

    private IEnumerator ExitSequence()
    {
        // Salir por el mismo lado por donde entró
        float exitX = enterFromLeft
            ? targetPosition.x - entryOffscreen
            : targetPosition.x + entryOffscreen;
        Vector3 exitPos = new Vector3(exitX, targetPosition.y, targetPosition.z);

        customerAnimator?.SetTrigger("Walk");

        while (Vector3.Distance(transform.position, exitPos) > 0.1f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, exitPos, moveSpeed * 1.3f * Time.deltaTime);
            yield return null;
        }

        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    // GLOBO DE PEDIDO
    // ─────────────────────────────────────────────────────────────

    private enum BubbleState { Normal, Correct, Error }

    private void SetBubbleColor(BubbleState state)
    {
        if (orderBubbleRenderer == null) return;

        orderBubbleRenderer.sprite = state switch
        {
            BubbleState.Correct => bubbleGreen,
            BubbleState.Error   => bubbleRed,
            _                   => bubbleNormal
        };
    }

    private void SetOrderIcon(RecipeType recipe)
    {
        if (orderIconRenderer == null) return;
        // El sprite del pedido se asigna desde el OrderManager usando un diccionario
        // de RecipeType → Sprite. Por ahora se deja en null hasta tener los assets.
        // orderIconRenderer.sprite = OrderIconLibrary.Get(recipe);
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private Color GetMoodColor(CustomerMood mood) => mood switch
    {
        CustomerMood.Ecstatic => colorEcstatic,
        CustomerMood.Happy    => colorHappy,
        CustomerMood.Neutral  => colorNeutral,
        CustomerMood.Annoyed  => colorAnnoyed,
        CustomerMood.Furious  => colorFurious,
        _                     => colorNeutral
    };

    private string GetMoodFace(CustomerMood mood) => mood switch
    {
        CustomerMood.Ecstatic => ":D",
        CustomerMood.Happy    => ":)",
        CustomerMood.Neutral  => ":/",
        CustomerMood.Annoyed  => ":(",
        CustomerMood.Furious  => ">:(",
        _                     => ":/"
    };
}
