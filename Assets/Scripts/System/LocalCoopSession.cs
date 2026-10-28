using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stores local co-op selections and input-device assignments while changing scenes.
/// Runtime-only data; it does not persist between application launches.
/// </summary>
public static class LocalCoopSession
{
    public const int MaxPlayers = 2;

    public sealed class PlayerSlot
    {
        public bool IsJoined { get; internal set; }
        public int CharacterIndex { get; internal set; }
        public string ControlScheme { get; internal set; }
        public InputDevice[] Devices { get; internal set; } = new InputDevice[0];
        public PlayerInput LobbyInput { get; internal set; }
    }

    private static readonly PlayerSlot[] Slots =
    {
        new PlayerSlot(),
        new PlayerSlot()
    };

    public static PlayerSlot GetPlayer(int slotIndex)
    {
        if (!IsValidSlot(slotIndex))
            return null;

        return Slots[slotIndex];
    }

    public static bool IsJoined(int slotIndex)
    {
        return IsValidSlot(slotIndex) && Slots[slotIndex].IsJoined;
    }

    public static int FindFirstOpenSlot()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!Slots[i].IsJoined)
                return i;
        }

        return -1;
    }

    public static bool IsDeviceAssigned(InputDevice device)
    {
        if (device == null)
            return false;

        for (int i = 0; i < Slots.Length; i++)
        {
            if (!Slots[i].IsJoined || Slots[i].Devices == null)
                continue;

            for (int j = 0; j < Slots[i].Devices.Length; j++)
            {
                if (Slots[i].Devices[j] == device)
                    return true;
            }
        }

        return false;
    }

    public static bool RegisterPlayer(int slotIndex, PlayerInput lobbyInput)
    {
        if (!IsValidSlot(slotIndex) || lobbyInput == null)
            return false;

        PlayerSlot slot = Slots[slotIndex];
        slot.IsJoined = true;
        slot.LobbyInput = lobbyInput;
        CapturePlayerInput(slotIndex, lobbyInput);
        return true;
    }

    public static void CapturePlayerInput(int slotIndex, PlayerInput playerInput)
    {
        if (!IsValidSlot(slotIndex) || playerInput == null)
            return;

        PlayerSlot slot = Slots[slotIndex];
        slot.ControlScheme = playerInput.currentControlScheme;

        var pairedDevices = playerInput.devices;
        var copiedDevices = new InputDevice[pairedDevices.Count];
        for (int i = 0; i < pairedDevices.Count; i++)
            copiedDevices[i] = pairedDevices[i];

        slot.Devices = copiedDevices;
        slot.IsJoined = true;
        slot.LobbyInput = playerInput;
    }

    public static void SetCharacterIndex(int slotIndex, int characterIndex)
    {
        if (!IsValidSlot(slotIndex))
            return;

        Slots[slotIndex].CharacterIndex = Mathf.Max(0, characterIndex);
    }

    public static void ClearLobbyInputReference(int slotIndex)
    {
        if (IsValidSlot(slotIndex))
            Slots[slotIndex].LobbyInput = null;
    }

    public static void Reset()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i].IsJoined = false;
            Slots[i].CharacterIndex = 0;
            Slots[i].ControlScheme = null;
            Slots[i].Devices = new InputDevice[0];
            Slots[i].LobbyInput = null;
        }
    }

    private static bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < Slots.Length;
    }
}
