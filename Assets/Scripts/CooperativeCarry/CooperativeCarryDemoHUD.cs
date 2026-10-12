using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public sealed class CooperativeCarryDemoHUD : MonoBehaviour
    {
        [SerializeField] private CooperativeCarryObject carryObject;
        [SerializeField] private InteractionController player1;
        [SerializeField] private InteractionController player2;
        [SerializeField] private HGame.Objects.LampInteractable demoLamp;
        private void OnGUI()
        {
            if (!carryObject) return;
            GUI.Box(new Rect(12, 12, 500, 228), "Adaptive Animation - Two Players");
            GUILayout.BeginArea(new Rect(24, 38, 475, 204));
            GUILayout.Label("Move: P1 WASD | P2 Arrows. Grab / place: F | Right Ctrl");
            GUILayout.Label("Run (hold): Left Shift | Right Alt. Throw: Q | / (slash)");
            GUILayout.Label("Heavy crate: walk together; grab again to place together.");
            GUILayout.Label("P1: " + (player1 ? player1.CurrentCarryRole.ToString() : "-") +
                "     P2: " + (player2 ? player2.CurrentCarryRole.ToString() : "-"));
            GUILayout.Label("State: " + carryObject.State + " | Load / player: " +
                carryObject.LoadPerPlayerKg.ToString("0.#") + " kg");
            GUILayout.Label(carryObject.HasConflictingInput ? "Different input: body lean shows each player's effort." :
                "Solo: drag slowly. Two players: lift and move together.");
            GUILayout.Label("Press grab again to put down together. Front = object +Z.");
            GUILayout.Label("Object Interaction: P1 E | P2 Right Shift (empty hands).");
            if (demoLamp) GUILayout.Label("Lamp: " + (demoLamp.IsOn ? "ON" : "OFF") + " - approach its switch. Door is behind the boxes.");
            GUILayout.EndArea();
        }
    }
}
