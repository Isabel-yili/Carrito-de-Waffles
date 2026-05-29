using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// ═══════════════════════════════════════════════════════════════════
// ESTADO DE ÁNIMO — escala de 5 emociones
// ═══════════════════════════════════════════════════════════════════
public enum CustomerMood
{
    Ecstatic = 4,   // :D  — superfeliz, llega con mucha paciencia
    Happy = 3,   // :)  — feliz, paciencia normal
    Neutral = 2,   // :/  — neutral, menos paciencia
    Annoyed = 1,   // :(  — molesto, poca paciencia
    Furious = 0    // >:( — furioso, mínima paciencia
}

/// <summary>
/// CLIENTE — comportamiento de llegada y salida por animación vertical.
///
/// FLUJO DE ANIMATOR (ver imagen del controller):
///   Entry → Idle
///   Idle  → WalkViejita  (trigger "WalkIn")   ← cliente entra desde abajo
///   WalkViejita → Idle   (al terminar la anim de entrada)
///   Idle  → Happy        (trigger "Happy")     ← pedido correcto
///   Idle  → Angry        (trigger "Angry")     ← se va sin ser atendido
///   Happy / Angry → WalkBye                    ← sale hacia abajo (reversa)
///   WalkBye → Idle  (el objeto se destruye antes de que esta transición ocurra)
///
/// Los clientes ya NO se mueven lateralmente. La ilusión de movimiento
/// es 100% responsabilidad de las animaciones Procreate (WalkViejita / WalkBye).
/// El script coloca el GameObject directamente en targetPosition al inicializar.
///
/// PARÁMETROS DEL ANIMATOR:
///   Trigger  WalkIn   → inicia WalkViejita
///   Trigger  Happy    → inicia Happy → WalkBye
///   Trigger  Angry    → inicia Angry → WalkBye
///
/// JERARQUÍA DEL PREFAB:
///   Customer  (este script + Collider2D para hover)
///   ├── Body              SpriteRenderer
///   ├── OrderBubble       SpriteRenderer — globo
///   │   └── OrderIcon     SpriteRenderer — sprite del pedido
///   └── MoodPanel         (Canvas World Space — hover)
///       ├── Background    Image
///       ├── MoodSlider    Slider
///       │   └── Fill      Image
///       └── MoodFaceText  TMP
/// </summary>
public class Customer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────

    [Header("══ Configuración de llegada ══")]
    [Tooltip("Paciencia base en segundos — se sobreescribe desde OrderManager")]
    public float basePatience = 30f;
    [Tooltip("Multiplicador de paciencia según mood inicial")]
    public AnimationCurve patienceByMood = AnimationCurve.Linear(0, 0.6f, 4, 1.4f);

    [Header("══ Duraciones de animación ══")]
    [Tooltip("Duración de WalkViejita (entrada). Debe coincidir con la duración del clip en el Animator.")]
    public float walkInDuration = 0.8f;
    [Tooltip("Duración de Happy o Angry antes de pasar a WalkBye.")]
    public float reactionDuration = 0.6f;
    [Tooltip("Duración de WalkBye (salida). Debe coincidir con la duración del clip en el Animator.")]
    public float walkByeDuration = 0.8f;

    [Header("══ Visuals ══")]
    public SpriteRenderer bodyRenderer;
    public SpriteRenderer orderBubbleRenderer;
    public SpriteRenderer orderIconRenderer;
    public Animator customerAnimator;

    [Header("══ Orientación según slot ══")]
    [Tooltip("Si está activado, el sprite se voltea horizontalmente cuando el cliente está en el slot DERECHO.\n" +
             "Desactívalo si tu animación ya maneja la orientación correctamente.")]
    public bool flipWhenRight = true;

    [Header("══ Sprites del globo ══")]
    public Sprite bubbleNormal;
    public Sprite bubbleGreen;
    public Sprite bubbleRed;
    public Sprite bubbleFlash;

    [Header("══ Panel de hover (mood) ══")]
    public GameObject moodPanel;
    public Slider moodSlider;
    public Image moodSliderFill;
    public TextMeshProUGUI moodFaceText;
    public Image moodPanelBackground;

    [Header("══ Colores del slider de mood ══")]
    public Color colorEcstatic = new Color(0.3f, 0.9f, 0.3f);
    public Color colorHappy = new Color(0.6f, 0.9f, 0.3f);
    public Color colorNeutral = new Color(1.0f, 0.85f, 0.2f);
    public Color colorAnnoyed = new Color(1.0f, 0.5f, 0.1f);
    public Color colorFurious = new Color(0.9f, 0.15f, 0.1f);

    // ─── Estado interno ───────────────────────────────────────────

    private RecipeType _order;
    private float _maxPatience;
    private float _patience;
    private CustomerMood _currentMood;
    private bool _isServed = false;
    private bool _isLeaving = false;
    private bool _isHovering = false;

    // El timer de paciencia solo corre una vez que el cliente está en el mostrador
    private bool _hasArrived = false;

    private OrderManager _orderManager;

    // Posición en escena asignada por OrderManager
    [HideInInspector] public Vector3 targetPosition;

    // ─────────────────────────────────────────────────────────────
    // AWAKE — protección contra Root Motion
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        // Desactivar Apply Root Motion para que la animación NO mueva
        // el Transform del cliente fuera de su posición asignada.
        if (customerAnimator != null && customerAnimator.applyRootMotion)
        {
            customerAnimator.applyRootMotion = false;
            Debug.Log($"[Customer] Apply Root Motion desactivado en '{gameObject.name}'.");
        }
    }

    public RecipeType Order => _order;
    public float PatienceRatio => _patience / _maxPatience;
    public bool IsServed => _isServed;

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN — llamado desde OrderManager al instanciar
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Coloca al cliente en su posición de mostrador y arranca la entrada.
    /// Ya no recibe fromLeft porque el movimiento es puramente por animación.
    /// </summary>
    /// <param name="isLeftSlot">True si el cliente ocupa el slot IZQUIERDO, false si es el DERECHO.</param>
    public void Initialize(RecipeType order, float patience, Vector3 target, OrderManager manager, bool isLeftSlot = true)
    {
        _order = order;
        _orderManager = manager;
        targetPosition = target;

        // Mood inicial: aleatorio entre Happy y Ecstatic
        CustomerMood initialMood = (CustomerMood)Random.Range(3, 5);
        _currentMood = initialMood;

        // Paciencia ajustada por mood
        float moodMultiplier = patienceByMood.Evaluate((float)initialMood);
        _maxPatience = patience * moodMultiplier;
        _patience = _maxPatience;

        // Colocar en la posición del slot (la animación hace la "llegada" visual)
        transform.position = target;

        // Orientar el sprite según el lado del slot (recibido directamente desde OrderManager)
        if (flipWhenRight && bodyRenderer != null)
        {
            bool isRightSlot = !isLeftSlot;
            bodyRenderer.flipX = isRightSlot;
            // También voltear el globo de pedido para que quede del lado correcto
            if (orderBubbleRenderer != null)
                orderBubbleRenderer.flipX = isRightSlot;
            Debug.Log($"[Customer] '{gameObject.name}' → slot {(isLeftSlot ? "IZQUIERDO" : "DERECHO")} | flipX={isRightSlot}");
        }

        // Icono del pedido
        SetOrderIcon(order);

        // Panel de mood oculto hasta el hover
        if (moodPanel != null) moodPanel.SetActive(false);

        StartCoroutine(EnterSequence());
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE — timer de paciencia + bloqueo de posición
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        // ── BLOQUEO DE POSICIÓN X/Z ──
        // El Animator puede tener curvas de posición que resetean X/Z a (0,0)
        // aunque se hayan eliminado los KeyFrames, por "Write Defaults" de Unity.
        // Forzamos X y Z al valor correcto cada frame mientras el cliente no está
        // saliendo (la animación de salida sí necesita mover el personaje libremente).
        if (!_isLeaving && targetPosition != Vector3.zero)
        {
            Vector3 pos = transform.position;
            pos.x = targetPosition.x;
            pos.z = targetPosition.z;
            transform.position = pos;
        }

        if (!_hasArrived || _isServed || _isLeaving) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;

        _patience -= Time.deltaTime;

        UpdateMoodFromPatience();

        if (_isHovering) UpdateMoodPanel();

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

        if (ratio > 0.75f) newMood = CustomerMood.Ecstatic;
        else if (ratio > 0.50f) newMood = CustomerMood.Happy;
        else if (ratio > 0.30f) newMood = CustomerMood.Neutral;
        else if (ratio > 0.10f) newMood = CustomerMood.Annoyed;
        else newMood = CustomerMood.Furious;

        // El mood solo empeora, nunca mejora
        if ((int)newMood < (int)_currentMood)
        {
            _currentMood = newMood;
            OnMoodChanged();
        }
    }

    private void OnMoodChanged()
    {
        customerAnimator?.SetInteger("Mood", (int)_currentMood);

        if (_currentMood == CustomerMood.Furious)
            StartCoroutine(FuriousEffect());
    }

    private IEnumerator FuriousEffect()
    {
        Vector3 original = transform.localPosition;
        for (int i = 0; i < 6; i++)
        {
            transform.localPosition = original + new Vector3(Random.Range(-0.05f, 0.05f), 0, 0);
            yield return new WaitForSeconds(0.05f);
        }
        transform.localPosition = original;
    }

    // ─────────────────────────────────────────────────────────────
    // PANEL DE HOVER
    // ─────────────────────────────────────────────────────────────

    void OnMouseEnter()
    {
        if (!_hasArrived || _isLeaving) return;
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
        if (moodSlider != null) moodSlider.value = PatienceRatio;
        if (moodSliderFill != null) moodSliderFill.color = GetMoodColor(_currentMood);
        if (moodFaceText != null) moodFaceText.text = GetMoodFace(_currentMood);
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA DE PEDIDO
    // ─────────────────────────────────────────────────────────────

    /// <summary>Pedido correcto: globo verde y salida feliz.</summary>
    public void ServeCorrect()
    {
        if (_isServed || _isLeaving) return;
        _isServed = true;

        SetBubbleColor(BubbleState.Correct);
        AudioManager.Instance?.PlaySound(SoundType.CustomerHappy);
        StartCoroutine(ExitSequence(happy: true));
    }

    /// <summary>Penaliza paciencia por entrega incorrecta a otro cliente.</summary>
    public void PenalizePatience(float amount)
    {
        if (_isServed || _isLeaving) return;
        _patience = Mathf.Max(0f, _patience - amount);
        Debug.Log($"[Customer] Paciencia penalizada -{amount:F1}s → {_patience:F1}s restantes.");
    }

    /// <summary>Flash de globo rojo al entregar algo que no era su pedido.</summary>
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

    /// <summary>El cliente se va: por tiempo agotado (served=false) o tras ser atendido.</summary>
    public void Leave(bool served)
    {
        if (_isLeaving) return;
        _isLeaving = true;

        if (!served)
        {
            SetBubbleColor(BubbleState.Error);
            AudioManager.Instance?.PlaySound(SoundType.CustomerLeave);
        }

        if (moodPanel != null) moodPanel.SetActive(false);

        // Notificar al OrderManager antes de iniciar la salida
        _orderManager?.OnCustomerLeft(this, served);

        StartCoroutine(ExitSequence(happy: served));
    }

    // ─────────────────────────────────────────────────────────────
    // SECUENCIAS DE ANIMACIÓN
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Entrada: dispara WalkViejita y espera su duración antes de activar
    /// el timer de paciencia. No mueve el Transform — la animación lo hace.
    /// </summary>
    private IEnumerator EnterSequence()
    {
        // Esperar un frame para que Initialize() fije la posición ANTES de que
        // el Animator aplique su estado inicial (Write Defaults).
        yield return null;

        // Re-aplicar la posición por si el Animator ya la movio en el primer frame
        transform.position = targetPosition;

        customerAnimator?.SetTrigger("WalkIn");

        // Volver a fijar la posición inmediatamente tras el trigger,
        // antes de que el Animator procese el nuevo estado.
        transform.position = targetPosition;

        yield return new WaitForSeconds(walkInDuration);

        // El cliente ya está en el mostrador: empieza la cuenta regresiva
        _hasArrived = true;
        customerAnimator?.SetTrigger("Idle");

        // Asegurar posición final correcta tras la animación de entrada
        transform.position = targetPosition;
    }

    /// <summary>
    /// Salida: dispara Happy o Angry, espera la reacción, luego WalkBye
    /// y destruye el GameObject al terminar.
    /// </summary>
    private IEnumerator ExitSequence(bool happy)
    {
        // Reacción emocional
        customerAnimator?.SetTrigger(happy ? "Happy" : "Angry");
        yield return new WaitForSeconds(reactionDuration);

        // Salida (WalkBye = WalkViejita en reversa)
        customerAnimator?.SetTrigger("WalkBye");
        yield return new WaitForSeconds(walkByeDuration);

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
            BubbleState.Error => bubbleRed,
            _ => bubbleNormal
        };
    }

    private void SetOrderIcon(RecipeType recipe)
    {
        if (orderIconRenderer == null) return;
        // El sprite se asigna desde OrderManager antes de llamar a Initialize()
        // orderIconRenderer.sprite = OrderIconLibrary.Get(recipe);
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private Color GetMoodColor(CustomerMood mood) => mood switch
    {
        CustomerMood.Ecstatic => colorEcstatic,
        CustomerMood.Happy => colorHappy,
        CustomerMood.Neutral => colorNeutral,
        CustomerMood.Annoyed => colorAnnoyed,
        CustomerMood.Furious => colorFurious,
        _ => colorNeutral
    };

    private string GetMoodFace(CustomerMood mood) => mood switch
    {
        CustomerMood.Ecstatic => ":D",
        CustomerMood.Happy => ":)",
        CustomerMood.Neutral => ":/",
        CustomerMood.Annoyed => ":(",
        CustomerMood.Furious => ">:(",
        _ => ":/"
    };
}