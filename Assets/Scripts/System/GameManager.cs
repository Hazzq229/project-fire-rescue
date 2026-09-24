using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        Initialization,
        Playing,
        Victory,
        GameOver
    }

    public GameState CurrentState { get; private set; }

    [Header("Game Settings")]
    public float maxFireDamageThreshold = 100f;
    [Tooltip("Jumlah damage yang bertambah setiap detiknya")]
    public float damageIncreaseRate = 2f;
    [SerializeField] private float currentFireDamage = 0f;

    [Header("Win Condition")]
    [SerializeField] private bool hasActiveFire = false;
    [SerializeField] private List<Fire> activeFires = new List<Fire>();
    [SerializeField] private List<VictimAI> activeVictims = new List<VictimAI>();

    [Header("UI References")]
    public TextMeshProUGUI stateText;
    public TextMeshProUGUI damageText;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        ChangeState(GameState.Initialization);
    }

    private void Update()
    {
        if (CurrentState == GameState.Playing)
        {
            CheckActiveFire();
            ApplyFireDamage();
            UpdateUI();
            CheckWinCondition();
        }
    }

    public void ChangeState(GameState newState)
    {
        CurrentState = newState;
        UpdateUI();

        switch (newState)
        {
            case GameState.Initialization:
                HandleInitialization();
                break;
            case GameState.Playing:
                break;
            case GameState.Victory:
                break;
            case GameState.GameOver:
                break;
        }
    }

    private void HandleInitialization()
    {
        // Fire[] firesInScene = FindObjectsByType<Fire>(FindObjectsSortMode.None);
        // activeFires.Clear();
        // activeFires.AddRange(firesInScene);

        VictimAI[] victimsInScene = FindObjectsByType<VictimAI>(FindObjectsSortMode.None);
        activeVictims.Clear();
        activeVictims.AddRange(victimsInScene);

        currentFireDamage = 0f;

        ChangeState(GameState.Playing);
    }
    public void RegisterFire(Fire newFire)
    {
        if (!activeFires.Contains(newFire))
        {
            activeFires.Add(newFire);
        }
    }
    private void CheckActiveFire()
    {
        hasActiveFire = false;

        foreach (Fire fire in activeFires)
        {
            if (fire != null && fire.enabled)
            {
                hasActiveFire = true;
                break;
            }
        }
    }

    private void CheckWinCondition()
    {
        if (hasActiveFire) return;

        foreach (VictimAI victim in activeVictims)
        {
            if (victim != null && !victim.isSafe)
            {
                return;
            }
        }

        ChangeState(GameState.Victory);
    }

    private void ApplyFireDamage()
    {
        if (hasActiveFire)
        {
            currentFireDamage += damageIncreaseRate * Time.deltaTime;

            if (currentFireDamage >= maxFireDamageThreshold)
            {
                currentFireDamage = maxFireDamageThreshold;
                UpdateUI();
                ChangeState(GameState.GameOver);
                return;
            }
        }
    }

    private void UpdateUI()
    {
        if (stateText != null)
        {
            stateText.text = $"{CurrentState.ToString()}";
        }

        if (damageText != null)
        {
            damageText.text = $"{Mathf.FloorToInt(currentFireDamage)} / {maxFireDamageThreshold}";
        }
    }
}