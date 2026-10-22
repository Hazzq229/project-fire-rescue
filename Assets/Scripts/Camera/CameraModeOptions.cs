using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mengatur pilihan mode kamera di scene Gameloop_Demo:
/// 1. SharedCurrent: Kamera zoom bersama mengikuti Target Group kedua pemain (original demo).
/// 2. AdaptiveSplitScreen: Kamera split adaptif (1 kamera saat dekat, 2 kamera split saat jauh).
/// 
/// Menyediakan toggle mudah via:
/// - Dropdown Inspector (berlaku di Edit Mode & Play Mode)
/// - Keyboard shortcut di runtime (default: F1 via new Input System)
/// - UI On-screen badge/button yang dapat diklik
/// - Public method ToggleMode() untuk integrasi UI Button.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
[AddComponentMenu("Camera/Camera Mode Options")]
public sealed class CameraModeOptions : MonoBehaviour
{
    public enum CameraMode
    {
        SharedCurrent,        // Kamera Zoom bersama (Target Group)
        AdaptiveSplitScreen   // Kamera Split adaptif berdasarkan jarak
    }

    [Header("Current Mode")]
    [SerializeField] private CameraMode mode = CameraMode.SharedCurrent;

    [Header("Toggle Shortcut & UI")]
    [Tooltip("Shortcut keyboard untuk beralih mode saat gameplay (menggunakan Unity New Input System).")]
    [SerializeField] private Key toggleKey = Key.F1;

    [Tooltip("Tampilkan badge/tombol toggle di pojok layar untuk kemudahan klik saat playtest.")]
    [SerializeField] private bool showOnScreenToggle = true;

    [Header("References")]
    [SerializeField] private SplitScreenManager splitScreenManager;
    [SerializeField] private GameObject sharedVirtualCamera;
    [SerializeField] private GameObject splitCanvas;
    [SerializeField] private GameObject player1Camera;
    [SerializeField] private GameObject player2Camera;
    [SerializeField] private GameObject player1VirtualCamera;
    [SerializeField] private GameObject player2VirtualCamera;

    public CameraMode Mode
    {
        get => mode;
        set
        {
            if (mode != value)
            {
                mode = value;
                ApplyMode();
            }
        }
    }

    public bool IsSplitScreenMode => mode == CameraMode.AdaptiveSplitScreen;

    private void Awake()
    {
        SyncReferencesToManager();
        ApplyMode();
    }

    private void Update()
    {
        // Toggle shortcut menggunakan New Input System
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            ToggleMode();
        }
    }

    public void ToggleMode()
    {
        Mode = (mode == CameraMode.SharedCurrent)
            ? CameraMode.AdaptiveSplitScreen
            : CameraMode.SharedCurrent;

        Debug.Log($"[CameraModeOptions] Camera mode changed to: {mode}");
    }

    public void SetSharedCurrentMode() => Mode = CameraMode.SharedCurrent;
    public void SetAdaptiveSplitMode() => Mode = CameraMode.AdaptiveSplitScreen;

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        SyncReferencesToManager();
        ApplyMode();
    }

    private void SyncReferencesToManager()
    {
        if (splitScreenManager == null)
        {
            splitScreenManager = FindObjectOfType<SplitScreenManager>();
        }

        if (splitScreenManager != null)
        {
            if (splitScreenManager.player1Camera == null) splitScreenManager.player1Camera = player1Camera;
            if (splitScreenManager.player2Camera == null) splitScreenManager.player2Camera = player2Camera;
            if (splitScreenManager.player1VirtualCamera == null) splitScreenManager.player1VirtualCamera = player1VirtualCamera;
            if (splitScreenManager.player2VirtualCamera == null) splitScreenManager.player2VirtualCamera = player2VirtualCamera;
            if (splitScreenManager.kameraBersama == null) splitScreenManager.kameraBersama = sharedVirtualCamera;
            if (splitScreenManager.canvasSplitScreen == null) splitScreenManager.canvasSplitScreen = splitCanvas;
        }
    }

    private void ApplyMode()
    {
        bool isSplitMode = mode == CameraMode.AdaptiveSplitScreen;

        if (splitScreenManager != null)
        {
            splitScreenManager.enabled = isSplitMode;
            if (isSplitMode)
            {
                splitScreenManager.ForceEvaluate();
            }
            else
            {
                splitScreenManager.ApplyState(false);
            }
        }
        else
        {
            // Fallback jika tidak ada SplitScreenManager
            if (sharedVirtualCamera) sharedVirtualCamera.SetActive(true);
            if (splitCanvas) splitCanvas.SetActive(false);
            if (player1Camera) player1Camera.SetActive(false);
            if (player2Camera) player2Camera.SetActive(false);
            if (player1VirtualCamera) player1VirtualCamera.SetActive(false);
            if (player2VirtualCamera) player2VirtualCamera.SetActive(false);
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenToggle) return;

        // Tampilkan tombol toggle yang elegan di pojok kiri atas
        GUILayout.BeginArea(new Rect(10, 10, 280, 75));
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.Box(new Rect(0, 0, 280, 75), GUIContent.none);
        GUI.color = Color.white;

        GUILayout.Space(5);
        GUILayout.Label($"<b>Camera Mode:</b> {(mode == CameraMode.SharedCurrent ? "<color=#4FC3F7>Zoom (Shared)</color>" : "<color=#81C784>Adaptive Split</color>")}", GetLabelStyle());

        string buttonText = mode == CameraMode.SharedCurrent
            ? $"Switch to Split Screen [{toggleKey}]"
            : $"Switch to Zoom Camera [{toggleKey}]";

        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            ToggleMode();
        }

        GUILayout.EndArea();
    }

    private GUIStyle labelStyle;
    private GUIStyle GetLabelStyle()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 12
            };
        }
        return labelStyle;
    }
}
