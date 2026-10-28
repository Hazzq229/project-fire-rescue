using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns the selected character prefab for each saved local-coop slot,
/// restores the saved device pairing, and logs enough runtime information
/// to diagnose overlapping spawn positions or shared input devices.
/// </summary>
public class GameplayPlayerSpawner : MonoBehaviour
{
    [Header("Character Prefabs")]
    [Tooltip("Order must match characterNames in MainMenuUIManager.")]
    [SerializeField] private GameObject[] characterPrefabs;

    [Header("Spawn Points")]
    [SerializeField] private Transform player1SpawnPoint;
    [SerializeField] private Transform player2SpawnPoint;

    [Header("Runtime Camera Targets")]
    [Tooltip("Assign SplitScreenManager from the Gameplay scene. If empty, it will be searched at runtime.")]
    [SerializeField] private SplitScreenManager splitScreenManager;

    [Header("Diagnostics")]
    [SerializeField] private bool logDiagnostics = true;

    private readonly PlayerInput[] _spawnedPlayers = new PlayerInput[2];
    private readonly Transform[] _spawnPoints = new Transform[2];

    private void Start()
    {
        _spawnPoints[0] = player1SpawnPoint;
        _spawnPoints[1] = player2SpawnPoint;

        if (logDiagnostics)
        {
            Debug.Log(
                $"[GameplayPlayerSpawner] P1 spawn: {DescribeTransform(player1SpawnPoint)} | " +
                $"P2 spawn: {DescribeTransform(player2SpawnPoint)}", this);

            if (player1SpawnPoint != null && player2SpawnPoint != null &&
                player1SpawnPoint == player2SpawnPoint)
            {
                Debug.LogError(
                    "Player 1 and Player 2 reference the exact same Spawn Point Transform in the Inspector.", this);
            }
        }

        SpawnPlayer(0, player1SpawnPoint);
        SpawnPlayer(1, player2SpawnPoint);

        AssignCameraTargets();
        StartCoroutine(LogPositionsNextFrame());
    }

    private void AssignCameraTargets()
    {
        if (!splitScreenManager)
            splitScreenManager = FindFirstObjectByType<SplitScreenManager>();

        if (!splitScreenManager)
        {
            Debug.LogWarning(
                "[GameplayPlayerSpawner] SplitScreenManager tidak ditemukan. " +
                "Runtime player references tidak ditetapkan ke pengelola split-screen.", this);
            return;
        }

        Transform p1 = _spawnedPlayers[0] ? _spawnedPlayers[0].transform : null;
        Transform p2 = _spawnedPlayers[1] ? _spawnedPlayers[1].transform : null;

        splitScreenManager.SetPlayerTargets(p1, p2);

        if (logDiagnostics)
        {
            Debug.Log(
                $"[GameplayPlayerSpawner] Camera targets assigned: " +
                $"P1={(p1 ? p1.name : "<null>")}, P2={(p2 ? p2.name : "<null>")}.", this);
        }
    }

    private void SpawnPlayer(int slotIndex, Transform spawnPoint)
    {
        LocalCoopSession.PlayerSlot slot = LocalCoopSession.GetPlayer(slotIndex);
        if (slot == null || !slot.IsJoined)
        {
            Debug.LogWarning($"[GameplayPlayerSpawner] Player {slotIndex + 1} did not join; no character spawned.", this);
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError($"[GameplayPlayerSpawner] Spawn point for Player {slotIndex + 1} is not assigned.", this);
            return;
        }

        if (characterPrefabs == null || slot.CharacterIndex < 0 ||
            slot.CharacterIndex >= characterPrefabs.Length || characterPrefabs[slot.CharacterIndex] == null)
        {
            Debug.LogError(
                $"[GameplayPlayerSpawner] Character prefab missing for Player {slotIndex + 1}; " +
                $"character index = {slot.CharacterIndex}.", this);
            return;
        }

        if (string.IsNullOrEmpty(slot.ControlScheme) || slot.Devices == null || slot.Devices.Length == 0)
        {
            Debug.LogError(
                $"[GameplayPlayerSpawner] Input assignment missing for Player {slotIndex + 1}. " +
                $"Scheme='{slot.ControlScheme ?? "<null>"}', device count={slot.Devices?.Length ?? 0}.", this);
            return;
        }

        if (logDiagnostics)
            Debug.Log($"[GameplayPlayerSpawner] Saved slot P{slotIndex + 1}: scheme='{slot.ControlScheme}', " +
                      $"devices=[{DescribeDevices(slot.Devices)}], spawn='{spawnPoint.name}' at {spawnPoint.position}.", this);

        PlayerInput playerInput = PlayerInput.Instantiate(
            characterPrefabs[slot.CharacterIndex],
            playerIndex: slotIndex,
            controlScheme: slot.ControlScheme,
            splitScreenIndex: -1,
            pairWithDevices: slot.Devices);

        if (playerInput == null)
        {
            Debug.LogError($"[GameplayPlayerSpawner] PlayerInput.Instantiate failed for Player {slotIndex + 1}.", this);
            return;
        }

        _spawnedPlayers[slotIndex] = playerInput;

        // Keep the player's chosen scheme stable and reassert the exact saved device assignment.
        playerInput.neverAutoSwitchControlSchemes = true;
        playerInput.SwitchCurrentControlScheme(slot.ControlScheme, slot.Devices);

        // Update both physics and Transform positions, which avoids the previous spawn reset.
        Vector3 spawnPosition = spawnPoint.position;
        Quaternion spawnRotation = spawnPoint.rotation;
        Rigidbody rb = playerInput.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = spawnPosition;
            rb.rotation = spawnRotation;
        }
        playerInput.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        Physics.SyncTransforms();

        if (playerInput.currentActionMap == null || playerInput.currentActionMap.name != "Player")
            playerInput.SwitchCurrentActionMap("Player");

        if (logDiagnostics)
        {
            Debug.Log(
                $"[GameplayPlayerSpawner] Spawned P{slotIndex + 1}: root='{playerInput.name}', " +
                $"playerIndex={playerInput.playerIndex}, scheme='{playerInput.currentControlScheme}', " +
                $"devices=[{DescribePlayerDevices(playerInput)}], " +
                $"actionsInstanceID={(playerInput.actions != null ? playerInput.actions.GetInstanceID() : 0)}, " +
                $"map='{playerInput.currentActionMap?.name}', " +
                $"position={playerInput.transform.position}.", this);
        }

        CheckForDuplicateSpawnedDevices();
    }

    private void CheckForDuplicateSpawnedDevices()
    {
        PlayerInput p1 = _spawnedPlayers[0];
        PlayerInput p2 = _spawnedPlayers[1];
        if (p1 == null || p2 == null)
            return;

        for (int i = 0; i < p1.devices.Count; i++)
        {
            for (int j = 0; j < p2.devices.Count; j++)
            {
                if (p1.devices[i] == p2.devices[j] &&
                    p1.currentControlScheme == "Gamepad" &&
                    p2.currentControlScheme == "Gamepad")
                {
                    Debug.LogError(
                        $"[GameplayPlayerSpawner] The same gamepad '{p1.devices[i].displayName}' " +
                        $"(deviceId {p1.devices[i].deviceId}) is paired to both gameplay players. " +
                        "Check the lobby join-time device assignment logs.", this);
                }
            }
        }
    }

    private IEnumerator LogPositionsNextFrame()
    {
        // Lets player prefab Start() methods run; this helps reveal if another script resets the spawn position.
        yield return null;

        if (!logDiagnostics)
            yield break;

        for (int i = 0; i < _spawnedPlayers.Length; i++)
        {
            PlayerInput player = _spawnedPlayers[i];
            if (player == null)
                continue;

            Vector3 expected = _spawnPoints[i] != null ? _spawnPoints[i].position : Vector3.zero;
            Debug.Log(
                $"[GameplayPlayerSpawner] Next-frame P{i + 1} position={player.transform.position}; " +
                $"expected={expected}; devices=[{DescribePlayerDevices(player)}].", player);
        }
    }

    private static string DescribeTransform(Transform target)
    {
        return target == null ? "<not assigned>" : $"{target.name} at {target.position}";
    }

    private static string DescribeDevices(InputDevice[] devices)
    {
        if (devices == null || devices.Length == 0)
            return "<none>";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < devices.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            InputDevice device = devices[i];
            sb.Append(device == null ? "<null>" : $"{device.displayName}#{device.deviceId}");
        }
        return sb.ToString();
    }

    private static string DescribePlayerDevices(PlayerInput playerInput)
    {
        if (playerInput == null || playerInput.devices.Count == 0)
            return "<none>";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < playerInput.devices.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            InputDevice device = playerInput.devices[i];
            sb.Append(device == null ? "<null>" : $"{device.displayName}#{device.deviceId}");
        }
        return sb.ToString();
    }
}
