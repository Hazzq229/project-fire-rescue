using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

public class MainMenuUIManager : MonoBehaviour
{
    [Header("Main Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject creditsPanel;
    [SerializeField] private GameObject selectCharacterPanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button quitButton;

    [Header("Credits Buttons")]
    [SerializeField] private Button creditsBackButton;

    [Header("Character Selection Buttons")]
    [SerializeField] private Button previousCharacterButton;
    [SerializeField] private Button nextCharacterButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button characterSelectBackButton;
    [SerializeField] private TMP_Text characterNameLabel;

    [SerializeField]
    private string[] characterNames =
        { "Character 1", "Character 2", "Character 3" };

    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Pause Menu")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button exitToMainMenuButton;

    private int selectedCharacterIndex;
    private bool isPaused;
    private bool isGameplayScene;

    private void Awake()
    {
        isGameplayScene =
            SceneManager.GetActiveScene().name == gameplaySceneName;

        selectedCharacterIndex =
            Mathf.Max(0, CharacterSelectionData.SelectedCharacterIndex);

        ConnectButtons();
        UpdateCharacterLabel();

        if (isGameplayScene)
        {
            isPaused = false;
            Time.timeScale = 1f;

            if (pausePanel != null)
                pausePanel.SetActive(false);
        }
        else
        {
            ShowMainMenu();
        }
    }

    private void Update()
    {
        if (!isGameplayScene)
            return;

        if (Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    private void ConnectButtons()
    {
        AddListener(playButton, ShowCharacterSelect);
        AddListener(creditsButton, ShowCredits);
        AddListener(quitButton, QuitGame);

        AddListener(creditsBackButton, ShowMainMenu);
        AddListener(previousCharacterButton, PreviousCharacter);
        AddListener(nextCharacterButton, NextCharacter);
        AddListener(startGameButton, StartGame);
        AddListener(characterSelectBackButton, ShowMainMenu);

        AddListener(resumeButton, ResumeGame);
        AddListener(exitToMainMenuButton, ExitToMainMenu);
    }

    private void AddListener(
        Button button,
        UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    // MAIN MENU

    public void ShowMainMenu()
    {
        SetPanel(mainMenuPanel, true);
        SetPanel(creditsPanel, false);
        SetPanel(selectCharacterPanel, false);
    }

    public void ShowCredits()
    {
        SetPanel(mainMenuPanel, false);
        SetPanel(creditsPanel, true);
        SetPanel(selectCharacterPanel, false);
    }

    public void ShowCharacterSelect()
    {
        SetPanel(mainMenuPanel, false);
        SetPanel(creditsPanel, false);
        SetPanel(selectCharacterPanel, true);

        UpdateCharacterLabel();
    }

    // CHARACTER SELECTION

    public void PreviousCharacter()
    {
        if (characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndex =
            (selectedCharacterIndex - 1 + characterNames.Length)
            % characterNames.Length;

        UpdateCharacterLabel();
    }

    public void NextCharacter()
    {
        if (characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndex =
            (selectedCharacterIndex + 1) % characterNames.Length;

        UpdateCharacterLabel();
    }

    public void StartGame()
    {
        if (characterNames == null || characterNames.Length == 0)
        {
            Debug.LogError("No character names configured.");
            return;
        }

        selectedCharacterIndex = Mathf.Clamp(
            selectedCharacterIndex, 0, characterNames.Length - 1);

        CharacterSelectionData.SelectedCharacterIndex =
            selectedCharacterIndex;

        CharacterSelectionData.SelectedCharacterName =
            characterNames[selectedCharacterIndex];

        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void UpdateCharacterLabel()
    {
        if (characterNameLabel == null)
            return;

        if (characterNames == null || characterNames.Length == 0)
        {
            characterNameLabel.text = "No characters configured";
            return;
        }

        selectedCharacterIndex = Mathf.Clamp(
            selectedCharacterIndex, 0, characterNames.Length - 1);

        characterNameLabel.text =
            characterNames[selectedCharacterIndex];
    }

    // PAUSE MENU

    public void PauseGame()
    {
        if (!isGameplayScene || isPaused)
            return;

        isPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        Time.timeScale = 0f;

        // Bersihkan pilihan UI sebelumnya agar raycast mouse
        // tidak terganggu oleh pilihan tombol lama.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void ResumeGame()
    {
        if (!isPaused)
            return;

        isPaused = false;
        Time.timeScale = 1f;

        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    public void ExitToMainMenu()
    {
        isPaused = false;
        Time.timeScale = 1f;

        SceneManager.LoadScene(mainMenuSceneName);
    }

    // QUIT APPLICATION

    private void QuitGame()
    {
#if UNITY_EDITOR
        Debug.Log("Quit button clicked.");
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SetPanel(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }
}