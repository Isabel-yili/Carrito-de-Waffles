using UnityEngine;

/// <summary>
/// PAUSA — GDD sección 5
/// "Se accede pulsando ESC o el botón de pausa. El tiempo se detiene."
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("UI")]
    public GameObject pausePanel;

    private bool _isPaused = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
        Time.timeScale = _isPaused ? 0f : 1f;

        if (pausePanel != null)
            pausePanel.SetActive(_isPaused);
    }

    public void Resume()
    {
        _isPaused = false;
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    public void RestartFromPause()
    {
        Time.timeScale = 1f;
        GameManager.Instance?.RestartGame();
    }

    public void GoToMenuFromPause()
    {
        Time.timeScale = 1f;
        GameManager.Instance?.GoToMainMenu();
    }
}
