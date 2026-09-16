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
    [SerializeField] private bool isVictimSafe = false;
    [SerializeField] private List<Fire> activeFires = new List<Fire>();

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
            // Cek api aktif dalam scene
            CheckActiveFire();

            // Tingkatkan damage seiring waktu hanya ketika ada api
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
                // Aktivasi kontrol player jika diperlukan
                break;
            case GameState.Victory:
                // Hentikan timer dan tampilkan UI Menang
                break;
            case GameState.GameOver:
                // Tampilkan UI Kalah
                break;
        }
    }

    private void HandleInitialization()
    {
        // Cari seluruh script Fire di dalam scene dan simpan ke dalam List
        Fire[] firesInScene = FindObjectsByType<Fire>(FindObjectsSortMode.None);
        activeFires.Clear();
        activeFires.AddRange(firesInScene);

        currentFireDamage = 0f;
        isVictimSafe = false;

        ChangeState(GameState.Playing);
    }

    // Dipanggil oleh script Trigger Safezone
    public void SetVictimSafe(bool state)
    {
        isVictimSafe = state;
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
        // Syarat 1: Korban harus berada di area aman
        if (!isVictimSafe) return;

        // Syarat 2: Pastikan tidak ada api yang menyala (enabled)
        if (hasActiveFire) return;

        // Jika semua syarat terpenuhi, kondisi menang tercapai
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
            // Menggunakan Mathf.FloorToInt agar tampilan UI rapi tanpa angka desimal panjang
            damageText.text = $"{Mathf.FloorToInt(currentFireDamage)} / {maxFireDamageThreshold}";
        }
    }
}