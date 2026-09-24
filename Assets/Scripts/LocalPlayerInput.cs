using UnityEngine;
using UnityEngine.InputSystem;

namespace HPlayer
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class LocalPlayerInput : MonoBehaviour
    {
        public enum KeyboardProfile { Player1_WASD, Player2_Arrows }
        [SerializeField] private KeyboardProfile profile = KeyboardProfile.Player1_WASD;

        private InputActionMap actionMap;
        private InputAction move, grab, door, tool;
        public bool IsReady => isActiveAndEnabled && actionMap != null &&
                               actionMap.enabled && Application.isFocused;
        public Vector2 MoveInput => IsReady ? move.ReadValue<Vector2>() : Vector2.zero;
        public bool GrabPressed => IsReady && grab.WasPressedThisFrame();
        public bool GrabHeld => IsReady && grab.IsPressed();
        public bool DoorPressed => IsReady && door.WasPressedThisFrame();
        public bool ToolPressed => IsReady && tool.WasPressedThisFrame();

        private void OnEnable()
        {
            // Setiap player memiliki instance action terpisah dengan binding berbeda.
            actionMap = new InputActionMap("LocalPlayer_" + profile);
            move = actionMap.AddAction("Move", InputActionType.Value);
            move.expectedControlType = "Vector2";
            grab = actionMap.AddAction("Grab", InputActionType.Button);
            door = actionMap.AddAction("Door", InputActionType.Button);
            tool = actionMap.AddAction("Tool", InputActionType.Button);

            if (profile == KeyboardProfile.Player1_WASD)
            {
                move.AddCompositeBinding("2DVector(mode=0)")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                grab.AddBinding("<Keyboard>/f");
                door.AddBinding("<Keyboard>/e");
                tool.AddBinding("<Keyboard>/g");
            }
            else
            {
                move.AddCompositeBinding("2DVector(mode=0)")
                    .With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/rightArrow");
                grab.AddBinding("<Keyboard>/rightCtrl");
                door.AddBinding("<Keyboard>/rightShift");
                tool.AddBinding("<Keyboard>/enter");
            }
            actionMap.Enable();
        }

        private void OnDisable()
        {
            if (actionMap == null) return;
            actionMap.Disable();
            actionMap.Dispose();
            actionMap = null;
        }
    }
}
