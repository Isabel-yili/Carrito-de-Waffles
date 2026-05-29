using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Escena del Juego")]
    [SerializeField] private string gameSceneName = "Juego";

    [Header("Rotación Rayos de Luz")]
    [SerializeField] private RectTransform rotatingLights;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Audio")]
    [SerializeField] private Image soundButtonImage;
    [SerializeField] private Sprite soundOnIcon;
    [SerializeField] private Sprite soundOffIcon;

    private bool soundMuted = false;

    private void Start()
    {
        UnmuteAudio();
    }

    private void Update()
    {
        RotateLights();
    }

    private void RotateLights()
    {
        if (rotatingLights != null)
        {
            rotatingLights.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
        }
    }

    public void StartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameSceneName);
    }

    public void ToggleSound()
    {
        if (soundMuted)
            UnmuteAudio();
        else
            MuteAudio();
    }

    private void MuteAudio()
    {
        soundMuted = true;

        AudioListener.volume = 0f;

        if (soundButtonImage != null)
            soundButtonImage.sprite = soundOffIcon;

        Debug.Log("Audio Muted");
    }

    private void UnmuteAudio()
    {
        soundMuted = false;

        AudioListener.volume = 1f;

        if (soundButtonImage != null)
            soundButtonImage.sprite = soundOnIcon;

        Debug.Log("Audio Unmuted");
    }

    public void ExitGame()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
    }
}