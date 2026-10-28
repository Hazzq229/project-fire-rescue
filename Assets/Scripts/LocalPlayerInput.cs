using UnityEngine;
using UnityEngine.InputSystem;

namespace HPlayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public class LocalPlayerInput : MonoBehaviour
    {
        private PlayerInput _playerInput;

        public PlayerInput PlayerInputComponent => _playerInput;
        public bool IsReady => isActiveAndEnabled && _playerInput != null && Application.isFocused;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
        }

        private InputAction GetAction(string actionName)
        {
            if (!IsReady || _playerInput.actions == null) return null;
            return _playerInput.actions.FindAction(actionName);
        }

        public Vector2 MoveInput => GetAction("Move")?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool GrabPressed => GetAction("Grab")?.WasPressedThisFrame() ?? false;
        public bool GrabHeld => GetAction("Grab")?.IsPressed() ?? false;
        public bool DoorPressed => GetAction("Door")?.WasPressedThisFrame() ?? false;
        public bool ObjectInteractPressed => DoorPressed;
        public bool ToolPressed => GetAction("Tool")?.WasPressedThisFrame() ?? false;
        public bool ToolHeld => GetAction("Tool")?.IsPressed() ?? false;
        public Vector2 HoseAim => GetAction("HoseAim")?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool HoseExtendPressed => GetAction("HoseExtend")?.WasPressedThisFrame() ?? false;
        public bool HoseRetractPressed => GetAction("HoseRetract")?.WasPressedThisFrame() ?? false;
        public bool RunHeld => GetAction("Run")?.IsPressed() ?? false;
        public bool ThrowPressed => GetAction("Throw")?.WasPressedThisFrame() ?? false;
    }
}