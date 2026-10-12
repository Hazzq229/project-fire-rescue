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
        private InputAction move, grab, door, tool, run, throwAction, hoseAim, hoseExtend, hoseRetract;
        public bool IsReady => isActiveAndEnabled && actionMap != null &&
                               actionMap.enabled && Application.isFocused;
        public Vector2 MoveInput => IsReady ? move.ReadValue<Vector2>() : Vector2.zero;
        public bool GrabPressed => IsReady && grab.WasPressedThisFrame();
        public bool GrabHeld => IsReady && grab.IsPressed();
        public bool DoorPressed => IsReady && door.WasPressedThisFrame();
        public bool ObjectInteractPressed => DoorPressed;
        public bool ToolPressed => IsReady && tool.WasPressedThisFrame();
        public bool ToolHeld => IsReady && tool.IsPressed();
        public Vector2 HoseAim => IsReady ? hoseAim.ReadValue<Vector2>() : Vector2.zero;
        public bool HoseExtendPressed => IsReady && hoseExtend.WasPressedThisFrame();
        public bool HoseRetractPressed => IsReady && hoseRetract.WasPressedThisFrame();
        public bool RunHeld => IsReady && run.IsPressed();
        public bool ThrowPressed => IsReady && throwAction.WasPressedThisFrame();

        private void OnEnable()
        {
            // Setiap player memiliki instance action terpisah dengan binding berbeda.
            actionMap = new InputActionMap("LocalPlayer_" + profile);
            move = actionMap.AddAction("Move", InputActionType.Value);
            move.expectedControlType = "Vector2";
            grab = actionMap.AddAction("Grab", InputActionType.Button);
            door = actionMap.AddAction("Door", InputActionType.Button);
            tool = actionMap.AddAction("Tool", InputActionType.Button);
            run = actionMap.AddAction("Run", InputActionType.Button);
            throwAction = actionMap.AddAction("Throw", InputActionType.Button);
            hoseAim = actionMap.AddAction("HoseAim", InputActionType.Value);
            hoseExtend = actionMap.AddAction("HoseExtend", InputActionType.Button);
            hoseRetract = actionMap.AddAction("HoseRetract", InputActionType.Button);

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
                run.AddBinding("<Keyboard>/leftShift");
                throwAction.AddBinding("<Keyboard>/q");
                hoseAim.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/i").With("Down", "<Keyboard>/k")
                    .With("Left", "<Keyboard>/j").With("Right", "<Keyboard>/l");
                hoseExtend.AddBinding("<Keyboard>/r");
                hoseRetract.AddBinding("<Keyboard>/t");
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
                run.AddBinding("<Keyboard>/rightAlt");
                throwAction.AddBinding("<Keyboard>/slash");
                hoseAim.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/minus").With("Down", "<Keyboard>/leftBracket")
                    .With("Left", "<Keyboard>/p").With("Right", "<Keyboard>/rightBracket");
                hoseExtend.AddBinding("<Keyboard>/n");
                hoseRetract.AddBinding("<Keyboard>/m");
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
