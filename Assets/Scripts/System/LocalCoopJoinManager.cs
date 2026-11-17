using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Handles lobby joining for Gamepad, KeyboardWASD, and KeyboardArrows.
/// Each PlayerInput is locked to its assigned control scheme/device to prevent
/// the first lobby player from auto-switching to the second player's gamepad.
/// </summary>
public class LocalCoopJoinManager : MonoBehaviour
{
    [Header("Lobby Input")]
    [Tooltip("A lightweight prefab containing PlayerInput and the Controls Actions Asset. No gameplay controller is needed.")]
    [SerializeField] private PlayerInput lobbyPlayerPrefab;

    [Header("Diagnostics")]
    [SerializeField] private bool logDeviceAssignments = true;

    private bool _joiningActive;

    private void Awake()
    {
        if (lobbyPlayerPrefab == null)
        {
            Debug.LogError(
                "LocalCoopJoinManager: Lobby Player Prefab is missing. Assign a prefab with a PlayerInput component.",
                this);
        }
    }

    /// <summary>Call when the character-selection panel becomes visible.</summary>
    public void BeginJoining()
    {
        ClearLobbyPlayers();
        LocalCoopSession.Reset();

        if (lobbyPlayerPrefab == null)
        {
            _joiningActive = false;
            Debug.LogError("Cannot begin joining because Lobby Player Prefab is not assigned.", this);
            return;
        }

        _joiningActive = true;
        LogGamepadInventory("BeginJoining");
    }

    /// <summary>Call when returning to the main menu or abandoning character selection.</summary>
    public void CancelLobby()
    {
        _joiningActive = false;
        ClearLobbyPlayers();
        LocalCoopSession.Reset();
    }

    /// <summary>
    /// Keep the device assignment snapshot captured at join time. Do not re-capture
    /// from LobbyInput here: PlayerInput could have changed devices before it was locked.
    /// Unpair temporary lobby users so gameplay players can claim the saved devices.
    /// </summary>
    public void PrepareForGameplay()
    {
        _joiningActive = false;

        for (int i = 0; i < LocalCoopSession.MaxPlayers; i++)
        {
            LocalCoopSession.PlayerSlot slot = LocalCoopSession.GetPlayer(i);
            if (slot == null || slot.LobbyInput == null)
                continue;

            PlayerInput lobbyInput = slot.LobbyInput;

            if (logDeviceAssignments)
            {
                string saved = DescribeDevices(slot.Devices);
                string current = DescribePlayerDevices(lobbyInput);
                Debug.Log(
                    $"[LocalCoopJoinManager] Prepare P{i + 1}: saved=[{saved}], " +
                    $"lobby currently paired=[{current}], scheme='{lobbyInput.currentControlScheme}'. " +
                    "Saved join-time assignment will be preserved.", this);

                if (!SameDeviceSet(slot.Devices, lobbyInput.devices))
                {
                    Debug.LogWarning(
                        $"[LocalCoopJoinManager] P{i + 1} lobby devices changed after joining. " +
                        $"Preserving original saved assignment [{saved}] instead of overwriting it with [{current}].",
                        this);
                }
            }

            lobbyInput.user.UnpairDevices();
            LocalCoopSession.ClearLobbyInputReference(i);
            Destroy(lobbyInput.gameObject);
        }
    }

    private void Update()
    {
        if (!_joiningActive || lobbyPlayerPrefab == null)
            return;

        if (LocalCoopSession.FindFirstOpenSlot() < 0)
            return;

        DetectKeyboardJoin();
        DetectGamepadJoins();
    }

    private void DetectKeyboardJoin()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        // The same physical Keyboard is deliberately shared by the two keyboard schemes.
        if (WasdJoinPressed(keyboard))
            TryJoin("KeyboardWASD", keyboard);

        if (ArrowsJoinPressed(keyboard))
            TryJoin("KeyboardArrows", keyboard);
    }

    private void DetectGamepadJoins()
    {
        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            Gamepad gamepad = Gamepad.all[i];
            if (gamepad == null || LocalCoopSession.IsDeviceAssigned(gamepad))
                continue;

            if (AnyButtonPressedThisFrame(gamepad))
                TryJoin("Gamepad", gamepad);
        }
    }

    private void TryJoin(string controlScheme, InputDevice device)
    {
        if (!_joiningActive || lobbyPlayerPrefab == null || device == null)
            return;

        int slotIndex = LocalCoopSession.FindFirstOpenSlot();
        if (slotIndex < 0)
            return;

        // Extra guard for gamepads: never create a second slot from a device already assigned.
        // Keyboard sharing remains allowed for KeyboardWASD + KeyboardArrows.
        if (controlScheme == "Gamepad" && LocalCoopSession.IsDeviceAssigned(device))
        {
            Debug.LogWarning(
                $"[LocalCoopJoinManager] Rejected join: {DescribeDevice(device)} is already assigned to a player.",
                this);
            return;
        }

        if (logDeviceAssignments)
        {
            Debug.Log(
                $"[LocalCoopJoinManager] Join request P{slotIndex + 1}: " +
                $"scheme='{controlScheme}', requested={DescribeDevice(device)}, " +
                $"Gamepad.all=[{DescribeGamepads()}]", this);
        }

        PlayerInput lobbyInput = PlayerInput.Instantiate(
            lobbyPlayerPrefab.gameObject,
            playerIndex: slotIndex,
            controlScheme: controlScheme,
            splitScreenIndex: -1,
            pairWithDevice: device);

        if (lobbyInput == null)
        {
            Debug.LogWarning(
                $"Could not join Player {slotIndex + 1} using control scheme '{controlScheme}'. " +
                "Check Lobby Player Prefab, its PlayerInput Actions Asset, and the exact control scheme name.", this);
            return;
        }

        // Critical: one active lobby PlayerInput must not migrate to another gamepad
        // when Player 2 begins using that device.
        lobbyInput.neverAutoSwitchControlSchemes = true;

        // Reassert the exact scheme and device supplied by this join request.
        try
        {
            lobbyInput.SwitchCurrentControlScheme(controlScheme, device);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[LocalCoopJoinManager] Could not force scheme '{controlScheme}' to " +
                $"{DescribeDevice(device)} for P{slotIndex + 1}. {exception}", this);
            lobbyInput.user.UnpairDevices();
            Destroy(lobbyInput.gameObject);
            return;
        }

        if (!HasPairedDevice(lobbyInput, device))
        {
            Debug.LogError(
                $"[LocalCoopJoinManager] Pairing verification failed for P{slotIndex + 1}. " +
                $"Requested={DescribeDevice(device)}, actual=[{DescribePlayerDevices(lobbyInput)}]. " +
                "The player will not be registered.", this);
            lobbyInput.user.UnpairDevices();
            Destroy(lobbyInput.gameObject);
            return;
        }

        if (lobbyInput.currentActionMap == null || lobbyInput.currentActionMap.name != "Player")
            lobbyInput.SwitchCurrentActionMap("Player");

        if (!LocalCoopSession.RegisterPlayer(slotIndex, lobbyInput))
        {
            lobbyInput.user.UnpairDevices();
            Destroy(lobbyInput.gameObject);
            return;
        }

        if (logDeviceAssignments)
        {
            Debug.Log(
                $"[LocalCoopJoinManager] JOINED P{slotIndex + 1}: " +
                $"requested={DescribeDevice(device)}, actual=[{DescribePlayerDevices(lobbyInput)}], " +
                $"scheme='{lobbyInput.currentControlScheme}', " +
                $"neverAutoSwitch={lobbyInput.neverAutoSwitchControlSchemes}, " +
                $"saved=[{DescribeDevices(LocalCoopSession.GetPlayer(slotIndex).Devices)}]", this);
        }
        else
        {
            Debug.Log(
                $"Player {slotIndex + 1} joined. Scheme: {lobbyInput.currentControlScheme}; " +
                $"Device(s): {lobbyInput.devices.Count}.", this);
        }
    }

    private void ClearLobbyPlayers()
    {
        for (int i = 0; i < LocalCoopSession.MaxPlayers; i++)
        {
            LocalCoopSession.PlayerSlot slot = LocalCoopSession.GetPlayer(i);
            if (slot == null || slot.LobbyInput == null)
                continue;

            PlayerInput lobbyInput = slot.LobbyInput;
            lobbyInput.user.UnpairDevices();
            Destroy(lobbyInput.gameObject);
        }
    }

    private void LogGamepadInventory(string context)
    {
        if (!logDeviceAssignments)
            return;

        Debug.Log(
            $"[LocalCoopJoinManager] {context}: Gamepad.all.Count={Gamepad.all.Count}; " +
            $"devices=[{DescribeGamepads()}]", this);
    }

    private static bool WasdJoinPressed(Keyboard keyboard)
    {
        return keyboard.wKey.wasPressedThisFrame ||
               keyboard.aKey.wasPressedThisFrame ||
               keyboard.sKey.wasPressedThisFrame ||
               keyboard.dKey.wasPressedThisFrame;
    }

    private static bool ArrowsJoinPressed(Keyboard keyboard)
    {
        return keyboard.upArrowKey.wasPressedThisFrame ||
               keyboard.downArrowKey.wasPressedThisFrame ||
               keyboard.leftArrowKey.wasPressedThisFrame ||
               keyboard.rightArrowKey.wasPressedThisFrame;
    }

    private static bool AnyButtonPressedThisFrame(Gamepad gamepad)
    {
        foreach (var control in gamepad.allControls)
        {
            if (control is ButtonControl button && button.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    private static bool HasPairedDevice(PlayerInput playerInput, InputDevice requested)
    {
        if (playerInput == null || requested == null)
            return false;

        for (int i = 0; i < playerInput.devices.Count; i++)
        {
            if (playerInput.devices[i] == requested)
                return true;
        }

        return false;
    }

    private static bool SameDeviceSet(InputDevice[] saved, UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice> current)
    {
        if (saved == null || saved.Length != current.Count)
            return false;

        for (int i = 0; i < saved.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < current.Count; j++)
            {
                if (saved[i] == current[j])
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static string DescribeGamepads()
    {
        if (Gamepad.all.Count == 0)
            return "<none>";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(DescribeDevice(Gamepad.all[i]));
        }

        return sb.ToString();
    }

    private static string DescribeDevices(InputDevice[] devices)
    {
        if (devices == null || devices.Length == 0)
            return "<none>";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < devices.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(DescribeDevice(devices[i]));
        }

        return sb.ToString();
    }

    private static string DescribePlayerDevices(PlayerInput playerInput)
    {
        return playerInput == null ? "<no PlayerInput>" : DescribeDevices(ToDeviceArray(playerInput.devices));
    }

    private static InputDevice[] ToDeviceArray(UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice> devices)
    {
        InputDevice[] result = new InputDevice[devices.Count];
        for (int i = 0; i < devices.Count; i++)
            result[i] = devices[i];
        return result;
    }

    private static string DescribeDevice(InputDevice device)
    {
        return device == null ? "<null>" : $"{device.displayName}#{device.deviceId} ({device.layout})";
    }
}
