using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HORNO — GDD sección 4.3 y 4.4
/// Acepta mezcla de waffle, la cocina durante 6 segundos,
/// muestra barra de progreso verde→amarillo→rojo.
/// Si no se retira a tiempo → waffle quemado.
/// </summary>
public class Oven : MonoBehaviour, IItemReceiver
{
    // ─── Estados del horno ────────────────────────────────────────
    public enum OvenState { Empty, Cooking, Ready, Burned }

    [Header("Configuración")]
    [Tooltip("Segundos hasta que el waffle está listo — GDD: 6s base")]
    public float cookingTime = 6f;
    [Tooltip("Segundos adicionales antes de quemarse tras estar listo")]
    public float burnWindow = 3f;

    [Header("UI")]
    public Slider cookingBar;
    public Image cookingBarFill;   // Para cambiar color
    public GameObject smokeEffect;
    public GameObject readyGlow;

    [Header("Colores de la barra")]
    public Color colorCooking = new Color(0.2f, 0.8f, 0.2f);  // Verde
    public Color colorWarning  = new Color(1f, 0.7f, 0f);      // Amarillo
    public Color colorDanger   = new Color(0.9f, 0.2f, 0.1f);  // Rojo

    [Header("Sprites")]
    public SpriteRenderer ovenItemSprite; // Sprite del waffle dentro del horno
    public Sprite spriteEmpty;
    public Sprite spriteRaw;
    public Sprite spriteReady;
    public Sprite spriteBurned;

    // ─── Estado interno ───────────────────────────────────────────
    private OvenState _state = OvenState.Empty;
    private float _timer = 0f;
    private bool _timerRunning = false;
    private DraggableItem _readyWaffle; // Ítem listo para sacar

    public OvenState State => _state;

    void Awake()
    {
        SetState(OvenState.Empty);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timer += Time.deltaTime;

        // Actualizar barra de progreso
        UpdateCookingBar();

        if (_state == OvenState.Cooking)
        {
            // ¿Terminó de cocinarse?
            if (_timer >= cookingTime)
            {
                WaffleReady();
            }
        }
        else if (_state == OvenState.Ready)
        {
            // ¿Se quemó por no retirarlo?
            if (_timer >= cookingTime + burnWindow)
            {
                WaffleBurned();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        // Solo acepta mezcla de waffle cuando está vacío
        return _state == OvenState.Empty && item.itemType == ItemType.WaffleMix;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        // Destruir el ícono arrastrado (se "introduce" al horno)
        Destroy(item.gameObject);

        StartCooking();
    }

    // ─────────────────────────────────────────────────────────────
    // LÓGICA DE COCCIÓN
    // ─────────────────────────────────────────────────────────────

    private void StartCooking()
    {
        _timer = 0f;
        _timerRunning = true;
        SetState(OvenState.Cooking);

        if (cookingBar != null)
        {
            cookingBar.gameObject.SetActive(true);
            cookingBar.value = 0f;
        }

        AudioManager.Instance?.PlaySound(SoundType.OvenStart);
    }

    private void WaffleReady()
    {
        SetState(OvenState.Ready);
        AudioManager.Instance?.PlaySound(SoundType.OvenReady);
        FeedbackManager.Instance?.ShowReadyGlow(transform.position);

        // El waffle "listo" puede sacarse — el horno pasa a ser clickeable para sacarlo
    }

    private void WaffleBurned()
    {
        _timerRunning = false;
        SetState(OvenState.Burned);

        AudioManager.Instance?.PlaySound(SoundType.WaffleBurned);
        FeedbackManager.Instance?.ShowBurnEffect(transform.position);

        if (smokeEffect != null) smokeEffect.SetActive(true);
    }

    /// <summary>
    /// Click sobre el horno cuando hay un waffle listo → sacarlo
    /// GDD: "hacer clic sobre el horno → waffle listo"
    /// </summary>
    void OnMouseDown()
    {
        if (_state == OvenState.Ready)
        {
            ExtractWaffle(ItemType.WaffleReady);
        }
        else if (_state == OvenState.Burned)
        {
            ExtractWaffle(ItemType.WaffleBurned);
        }
    }

    private void ExtractWaffle(ItemType type)
    {
        _timerRunning = false;

        // Crear el ítem de waffle que el jugador ahora puede arrastrar
        DraggableItem waffle = ItemSpawner.Instance.SpawnItem(type, transform.position);
        if (waffle != null)
        {
            // Iniciar drag automático del waffle extraído
            DragManager.Instance?.OnItemPickedUp(waffle);
        }

        // Limpiar horno
        SetState(OvenState.Empty);
        if (cookingBar != null) cookingBar.gameObject.SetActive(false);
        if (smokeEffect != null) smokeEffect.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // VISUAL
    // ─────────────────────────────────────────────────────────────

    private void UpdateCookingBar()
    {
        if (cookingBar == null) return;

        float progress = 0f;

        if (_state == OvenState.Cooking)
        {
            progress = _timer / cookingTime;
        }
        else if (_state == OvenState.Ready)
        {
            // En estado listo, la barra muestra cuánto tiempo queda antes de quemarse
            float burnProgress = (_timer - cookingTime) / burnWindow;
            progress = 1f - burnProgress; // Decrece de 1 a 0
        }

        cookingBar.value = Mathf.Clamp01(progress);

        // Color dinámico de la barra
        if (cookingBarFill != null)
        {
            if (_state == OvenState.Ready)
            {
                float burnRatio = (_timer - cookingTime) / burnWindow;
                cookingBarFill.color = Color.Lerp(colorWarning, colorDanger, burnRatio);
            }
            else
            {
                float ratio = _timer / cookingTime;
                cookingBarFill.color = Color.Lerp(colorCooking, colorWarning, ratio);
            }
        }
    }

    private void SetState(OvenState newState)
    {
        _state = newState;

        if (ovenItemSprite != null)
        {
            switch (newState)
            {
                case OvenState.Empty:   ovenItemSprite.sprite = spriteEmpty;  break;
                case OvenState.Cooking: ovenItemSprite.sprite = spriteRaw;    break;
                case OvenState.Ready:   ovenItemSprite.sprite = spriteReady;  break;
                case OvenState.Burned:  ovenItemSprite.sprite = spriteBurned; break;
            }
        }

        if (readyGlow != null)
            readyGlow.SetActive(newState == OvenState.Ready);
    }
}
