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

    [Header("Character Selection Buttons - Player 1")]
    [SerializeField] private Button previousCharacterButton;
    [SerializeField] private Button nextCharacterButton;
    [SerializeField] private TMP_Text characterNameLabel;
    [SerializeField] private TMP_Text player1JoinStatusLabel;

    [Header("Character Selection Buttons - Player 2")]
    [SerializeField] private Button previousCharacterButtonP2;
    [SerializeField] private Button nextCharacterButtonP2;
    [SerializeField] private TMP_Text characterNameLabelP2;
    [SerializeField] private TMP_Text player2JoinStatusLabel;

    [Header("Character Selection Button")]
    [SerializeField] private Button characterSelectBackButton;
    [SerializeField] private Button startGameButton;

    [SerializeField]
    private string[] characterNames =
        { "Character 1", "Character 2", "Character 3" };

    [SerializeField] private bool requireTwoPlayersToStart = true;
    [SerializeField] private LocalCoopJoinManager localCoopJoinManager;

    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "Gameplay";
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Pause Menu")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button exitToMainMenuButton;

    private int selectedCharacterIndexP1;
    private int selectedCharacterIndexP2;
    private bool isPaused;
    private bool isGameplayScene;

    private void Awake()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        isGameplayScene = activeSceneName != mainMenuSceneName || mainMenuPanel == null;

        selectedCharacterIndexP1 = Mathf.Max(0, CharacterSelectionData.SelectedCharacterIndex);
        selectedCharacterIndexP2 = 0;

        ConnectButtons();

        if (isGameplayScene)
        {
            isPaused = false;
            Time.timeScale = 1f;

            if (pausePanel == null)
            {
                Debug.LogError($"[MainMenuUIManager] PausePanel belum di-assign di Inspector pada scene '{activeSceneName}'!", this);
            }
            else if (pausePanel == gameObject || transform.IsChildOf(pausePanel.transform))
            {
                Debug.LogError("[MainMenuUIManager] Jangan tempelkan script ini di dalam PausePanel! Ketika PausePanel dinonaktifkan, Update() tidak akan berjalan.", this);
            }
            else
            {
                pausePanel.SetActive(false);
            }

            if (Keyboard.current == null)
            {
                Debug.LogWarning("[MainMenuUIManager] Keyboard.current bernilai null. Pastikan Active Input Handling memakai New Input System dan window Game sedang fokus.", this);
            }
        }
        else
        {
            UpdateCharacterSelectionUI();
            ShowMainMenu();
        }
    }

    private void Update()
    {
        if (!isGameplayScene)
        {
            if (selectCharacterPanel != null && selectCharacterPanel.activeSelf)
                UpdateCharacterSelectionUI();

            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    private void Start()
    {
        if (!isGameplayScene)
            EnsureMenuSelection();
    }

    private void ConnectButtons()
    {
        AddListener(playButton, ShowCharacterSelect);
        AddListener(creditsButton, ShowCredits);
        AddListener(quitButton, QuitGame);

        AddListener(creditsBackButton, ShowMainMenu);
        AddListener(previousCharacterButton, PreviousCharacter);
        AddListener(nextCharacterButton, NextCharacter);
        AddListener(previousCharacterButtonP2, PreviousCharacterP2);
        AddListener(nextCharacterButtonP2, NextCharacterP2);
        AddListener(startGameButton, StartGame);
        AddListener(characterSelectBackButton, ShowMainMenu);

        AddListener(resumeButton, ResumeGame);
        AddListener(restartButton, RestartGame);
        AddListener(exitToMainMenuButton, ExitToMainMenu);
    }

    private void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    // MAIN MENU

    public void ShowMainMenu()
    {
        if (localCoopJoinManager != null)
            localCoopJoinManager.CancelLobby();
        else
            LocalCoopSession.Reset();

        SetPanel(mainMenuPanel, true);
        SetPanel(creditsPanel, false);
        SetPanel(selectCharacterPanel, false);
        UpdateCharacterSelectionUI();
    }

    public void ShowCredits()
    {
        if (localCoopJoinManager != null)
            localCoopJoinManager.CancelLobby();

        SetPanel(mainMenuPanel, false);
        SetPanel(creditsPanel, true);
        SetPanel(selectCharacterPanel, false);
        EnsureMenuSelection();
    }

    public void ShowCharacterSelect()
    {
        SetPanel(mainMenuPanel, false);
        SetPanel(creditsPanel, false);
        SetPanel(selectCharacterPanel, true);

        selectedCharacterIndexP1 = 0;
        selectedCharacterIndexP2 = 0;

        if (localCoopJoinManager != null)
            localCoopJoinManager.BeginJoining();
        else
            Debug.LogError("LocalCoopJoinManager has not been assigned in MainMenuUIManager.", this);

        UpdateCharacterSelectionUI();
    }

    // CHARACTER SELECTION: PLAYER 1

    public void PreviousCharacter()
    {
        if (!LocalCoopSession.IsJoined(0) || characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndexP1 =
            (selectedCharacterIndexP1 - 1 + characterNames.Length) % characterNames.Length;

        UpdateCharacterSelectionUI();
    }

    public void NextCharacter()
    {
        if (!LocalCoopSession.IsJoined(0) || characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndexP1 = (selectedCharacterIndexP1 + 1) % characterNames.Length;
        UpdateCharacterSelectionUI();
    }

    // CHARACTER SELECTION: PLAYER 2

    public void PreviousCharacterP2()
    {
        if (!LocalCoopSession.IsJoined(1) || characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndexP2 =
            (selectedCharacterIndexP2 - 1 + characterNames.Length) % characterNames.Length;

        UpdateCharacterSelectionUI();
    }

    public void NextCharacterP2()
    {
        if (!LocalCoopSession.IsJoined(1) || characterNames == null || characterNames.Length == 0)
            return;

        selectedCharacterIndexP2 = (selectedCharacterIndexP2 + 1) % characterNames.Length;
        UpdateCharacterSelectionUI();
    }

    public void StartGame()
    {
        if (characterNames == null || characterNames.Length == 0)
        {
            Debug.LogError("No characters configured.", this);
            return;
        }

        if (!LocalCoopSession.IsJoined(0))
        {
            Debug.LogWarning("Player 1 must join before starting the game.", this);
            return;
        }

        if (requireTwoPlayersToStart && !LocalCoopSession.IsJoined(1))
        {
            Debug.LogWarning("Player 2 must join before starting the game.", this);
            return;
        }

        selectedCharacterIndexP1 = Mathf.Clamp(
            selectedCharacterIndexP1, 0, characterNames.Length - 1);
        selectedCharacterIndexP2 = Mathf.Clamp(
            selectedCharacterIndexP2, 0, characterNames.Length - 1);

        LocalCoopSession.SetCharacterIndex(0, selectedCharacterIndexP1);
        if (LocalCoopSession.IsJoined(1))
            LocalCoopSession.SetCharacterIndex(1, selectedCharacterIndexP2);

        CharacterSelectionData.SelectedCharacterIndex = selectedCharacterIndexP1;
        CharacterSelectionData.SelectedCharacterName = characterNames[selectedCharacterIndexP1];

        if (localCoopJoinManager == null)
        {
            Debug.LogError("Cannot save player device assignments: LocalCoopJoinManager is missing.", this);
            return;
        }

        localCoopJoinManager.PrepareForGameplay();
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void UpdateCharacterSelectionUI()
    {
        bool hasCharacters = characterNames != null && characterNames.Length > 0;

        if (hasCharacters)
        {
            selectedCharacterIndexP1 = Mathf.Clamp(selectedCharacterIndexP1, 0, characterNames.Length - 1);
            selectedCharacterIndexP2 = Mathf.Clamp(selectedCharacterIndexP2, 0, characterNames.Length - 1);
        }

        if (characterNameLabel != null)
            characterNameLabel.text = hasCharacters ? characterNames[selectedCharacterIndexP1] : "No characters configured";

        if (characterNameLabelP2 != null)
            characterNameLabelP2.text = hasCharacters ? characterNames[selectedCharacterIndexP2] : "No characters configured";

        bool player1Joined = LocalCoopSession.IsJoined(0);
        bool player2Joined = LocalCoopSession.IsJoined(1);

        if (player1JoinStatusLabel != null)
        {
            player1JoinStatusLabel.text = player1Joined
                ? $"Player 1 joined ({LocalCoopSession.GetPlayer(0).ControlScheme})"
                : "Tekan tombol WASD, arah, atau gamepad untuk bergabung";
        }

        if (player2JoinStatusLabel != null)
        {
            player2JoinStatusLabel.text = player2Joined
                ? $"Player 2 joined ({LocalCoopSession.GetPlayer(1).ControlScheme})"
                : "Tekan tombol WASD, arah, atau gamepad untuk bergabung";
        }

        if (previousCharacterButton != null) previousCharacterButton.interactable = player1Joined && hasCharacters;
        if (nextCharacterButton != null) nextCharacterButton.interactable = player1Joined && hasCharacters;
        if (previousCharacterButtonP2 != null) previousCharacterButtonP2.interactable = player2Joined && hasCharacters;
        if (nextCharacterButtonP2 != null) nextCharacterButtonP2.interactable = player2Joined && hasCharacters;

        if (startGameButton != null)
        {
            startGameButton.interactable = player1Joined &&
                (!requireTwoPlayersToStart || player2Joined) && hasCharacters;
        }

        EnsureMenuSelection();
    }

    private void EnsureMenuSelection()
    {
        EventSystem eventSystem = EventSystem.current;
        if (isGameplayScene || eventSystem == null)
            return;

        GameObject panel = mainMenuPanel != null && mainMenuPanel.activeInHierarchy ? mainMenuPanel :
            creditsPanel != null && creditsPanel.activeInHierarchy ? creditsPanel :
            selectCharacterPanel != null && selectCharacterPanel.activeInHierarchy ? selectCharacterPanel : null;
        if (panel == null)
            return;

        GameObject current = eventSystem.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy && current.transform.IsChildOf(panel.transform))
        {
            Selectable selected = current.GetComponent<Selectable>();
            if (selected != null && selected.IsActive() && selected.IsInteractable())
                return;
        }

        bool hasSelection;
        if (panel == mainMenuPanel)
            hasSelection = SelectIfUsable(eventSystem, playButton) ||
                SelectIfUsable(eventSystem, creditsButton) || SelectIfUsable(eventSystem, quitButton);
        else if (panel == creditsPanel)
            hasSelection = SelectIfUsable(eventSystem, creditsBackButton);
        else
            hasSelection = SelectIfUsable(eventSystem, previousCharacterButton) ||
                SelectIfUsable(eventSystem, nextCharacterButton) ||
                SelectIfUsable(eventSystem, previousCharacterButtonP2) ||
                SelectIfUsable(eventSystem, nextCharacterButtonP2) ||
                SelectIfUsable(eventSystem, startGameButton) ||
                SelectIfUsable(eventSystem, characterSelectBackButton);

        if (!hasSelection && current != null)
            eventSystem.SetSelectedGameObject(null);
    }

    private static bool SelectIfUsable(EventSystem eventSystem, Button button)
    {
        if (button == null || !button.IsActive() || !button.IsInteractable())
            return false;

        eventSystem.SetSelectedGameObject(button.gameObject);
        return true;
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

        if (EventSystem.current != null)
        {
            if (resumeButton != null && resumeButton.IsActive() && resumeButton.IsInteractable())
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
            else
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void ResumeGame()
    {
        if (!isPaused)
            return;

        isPaused = false;
        Time.timeScale = 1f;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void RestartGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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