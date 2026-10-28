using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class GameplayUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject inGamePanel;
    [SerializeField] private GameObject pausedPanel;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;

    [Header("In Game Buttons")]
    [SerializeField] private Button pauseButton;

    [Header("Pause Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button pauseMainMenuButton;

    [Header("Win Buttons")]
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button winMainMenuButton;

    [Header("Lose Buttons")]
    [SerializeField] private Button retryButton;
    [SerializeField] private Button loseMainMenuButton;

    [Header("Optional HUD")]
    [SerializeField] private TMP_Text selectedCharacterLabel;
    [SerializeField] private TMP_Text timerLabel;

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string gameplaySceneName = "Gameplay";

    [Header("Optional Round Timer")]
    [SerializeField] private bool useRoundTimer = false;
    [Min(1f)][SerializeField] private float roundDurationSeconds = 120f;

    private float timeRemaining;
    private bool roundActive;

    private void Awake()
    {
        Time.timeScale = 1f;
        ConnectButtons();

        if (selectedCharacterLabel != null)
            selectedCharacterLabel.text = CharacterSelectionData.SelectedCharacterName;

        BeginRound();
    }

    private void Update()
    {
        if (!useRoundTimer || !roundActive || (pausedPanel != null && pausedPanel.activeSelf))
            return;

        timeRemaining -= Time.deltaTime;
        UpdateTimerLabel();

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            UpdateTimerLabel();
            ShowLose();
        }
    }

    private void ConnectButtons()
    {
        AddListener(pauseButton, PauseGame);
        AddListener(resumeButton, ResumeGame);
        AddListener(restartButton, RestartGame);
        AddListener(pauseMainMenuButton, ReturnToMainMenu);
        AddListener(playAgainButton, RestartGame);
        AddListener(winMainMenuButton, ReturnToMainMenu);
        AddListener(retryButton, RestartGame);
        AddListener(loseMainMenuButton, ReturnToMainMenu);
    }

    private void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            Debug.LogWarning($"GameplayUIManager: Button for {action.Method.Name} is not assigned.", this);
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void BeginRound()
    {
        timeRemaining = roundDurationSeconds;
        roundActive = true;
        ShowInGame();
        UpdateTimerLabel();
    }

    private void ShowInGame()
    {
        SetPanel(inGamePanel, true);
        SetPanel(pausedPanel, false);
        SetPanel(winPanel, false);
        SetPanel(losePanel, false);
        Time.timeScale = 1f;
    }

    public void PauseGame()
    {
        if (!roundActive) return;
        SetPanel(inGamePanel, false);
        SetPanel(pausedPanel, true);
        SetPanel(winPanel, false);
        SetPanel(losePanel, false);
        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        if (!roundActive) return;
        ShowInGame();
    }

    public void ShowWin()
    {
        if (!roundActive) return;
        roundActive = false;
        Time.timeScale = 1f;
        SetPanel(inGamePanel, false);
        SetPanel(pausedPanel, false);
        SetPanel(winPanel, true);
        SetPanel(losePanel, false);
    }

    public void ShowLose()
    {
        if (!roundActive) return;
        roundActive = false;
        Time.timeScale = 1f;
        SetPanel(inGamePanel, false);
        SetPanel(pausedPanel, false);
        SetPanel(winPanel, false);
        SetPanel(losePanel, true);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void UpdateTimerLabel()
    {
        if (timerLabel == null) return;
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining));
        timerLabel.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private static void SetPanel(GameObject panel, bool active)
    {
        if (panel != null) panel.SetActive(active);
    }
}
