using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// GAME MANAGER — GDD secciones 4.7, 5, 8.1
/// Controla el estado global de la partida:
/// dinero, errores (máx. 3), temporizador (3 min MVP).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Configuración MVP — GDD sección 7")]
    [Tooltip("Duración de la partida en segundos — GDD recomienda 180s (3 min)")]
    public float gameDuration = 180f;
    [Tooltip("Máximo de errores antes del Game Over — GDD: 3")]
    public int maxErrors = 3;

    [Header("UI — HUD Superior")]
    public TextMeshProUGUI moneyText;
    public TextMeshProUGUI timerText;
    public GameObject[] errorIndicators; // 3 íconos de X o corazón
    public GameObject gameOverPanel;
    public GameObject timeUpPanel;

    // ─── Estado de partida ────────────────────────────────────────
    private int   _money     = 0;
    private int   _errors    = 0;
    private float _timeLeft;
    private bool  _isRunning = false;

    public bool IsGameRunning => _isRunning;
    public int  CurrentMoney  => _money;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        StartGame();
    }

    void Update()
    {
        if (!_isRunning) return;

        _timeLeft -= Time.deltaTime;
        UpdateTimerUI();

        if (_timeLeft <= 0f)
        {
            _timeLeft = 0f;
            TimeUp();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // INICIO / FIN DE PARTIDA
    // ─────────────────────────────────────────────────────────────

    public void StartGame()
    {
        _money    = 0;
        _errors   = 0;
        _timeLeft = gameDuration;
        _isRunning = true;

        UpdateMoneyUI();
        UpdateTimerUI();
        UpdateErrorUI();

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (timeUpPanel   != null) timeUpPanel.SetActive(false);
    }

    /// <summary>
    /// Game Over por acumulación de 3 errores — GDD sección 4.7
    /// "El carrito cierra el toldo con un letrero «Cerrado»"
    /// </summary>
    private void GameOver()
    {
        _isRunning = false;
        Time.timeScale = 0f; // Pausar el juego

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
            // Mostrar estadísticas finales
            ShowFinalStats(gameOverPanel);
        }

        AudioManager.Instance?.PlaySound(SoundType.GameOver);
        Debug.Log($"[GameManager] GAME OVER — Dinero final: ${_money}");
    }

    /// <summary>
    /// Fin por tiempo — GDD sección 4.7
    /// "El sol se pone en el fondo"
    /// </summary>
    private void TimeUp()
    {
        _isRunning = false;
        Time.timeScale = 0f;

        if (timeUpPanel != null)
        {
            timeUpPanel.SetActive(true);
            ShowFinalStats(timeUpPanel);
        }

        AudioManager.Instance?.PlaySound(SoundType.TimeUp);
        Debug.Log($"[GameManager] TIEMPO AGOTADO — Dinero final: ${_money}");
    }

    // ─────────────────────────────────────────────────────────────
    // API PÚBLICA
    // ─────────────────────────────────────────────────────────────

    public void AddMoney(int amount)
    {
        _money += amount;
        UpdateMoneyUI();

        // Feedback visual del dinero ganado
        FeedbackManager.Instance?.ShowMoneyGained(amount);
    }

    public void DeductMoney(int amount)
    {
        _money = Mathf.Max(0, _money - amount);
        UpdateMoneyUI();
    }

    /// <summary>
    /// Registra un error. Al llegar a 3 → Game Over.
    /// GDD sección 4.7
    /// </summary>
    public void AddError()
    {
        if (!_isRunning) return;

        _errors++;
        DeductMoney(5); // GDD sección 8.3: -$5 por error

        UpdateErrorUI();
        FeedbackManager.Instance?.ShakeErrorCounter();

        Debug.Log($"[GameManager] Error #{_errors}/{maxErrors}");

        if (_errors >= maxErrors)
            GameOver();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    // ─────────────────────────────────────────────────────────────
    // UI UPDATES
    // ─────────────────────────────────────────────────────────────

    private void UpdateMoneyUI()
    {
        if (moneyText != null)
            moneyText.text = $"${_money}";
    }

    private void UpdateTimerUI()
    {
        if (timerText == null) return;

        int minutes = Mathf.FloorToInt(_timeLeft / 60f);
        int seconds = Mathf.FloorToInt(_timeLeft % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";

        // Color rojo en los últimos 30 segundos — urgencia
        if (timerText.TryGetComponent<TextMeshProUGUI>(out var tmp))
            tmp.color = _timeLeft <= 30f ? Color.red : Color.white;
    }

    private void UpdateErrorUI()
    {
        if (errorIndicators == null) return;

        for (int i = 0; i < errorIndicators.Length; i++)
        {
            if (errorIndicators[i] != null)
                errorIndicators[i].SetActive(i < _errors);
        }
    }

    private void ShowFinalStats(GameObject panel)
    {
        // Buscar TextMeshPro en el panel para mostrar estadísticas
        TextMeshProUGUI[] texts = panel.GetComponentsInChildren<TextMeshProUGUI>();
        foreach (var t in texts)
        {
            if (t.name == "MoneyText")   t.text = $"Dinero ganado: ${_money}";
            if (t.name == "ErrorsText")  t.text = $"Errores: {_errors}/{maxErrors}";
        }
    }
}
