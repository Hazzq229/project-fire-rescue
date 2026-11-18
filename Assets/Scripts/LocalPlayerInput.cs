using UnityEngine;
using UnityEngine.InputSystem;

namespace HPlayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public class LocalPlayerInput : MonoBehaviour
    {
        private PlayerInput _playerInput;
        [SerializeField] private string keyboardControlScheme;

        public PlayerInput PlayerInputComponent => _playerInput;
        public bool IsReady => isActiveAndEnabled && _playerInput != null && Application.isFocused;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(keyboardControlScheme) || Keyboard.current == null || !_playerInput) return;
            _playerInput.neverAutoSwitchControlSchemes = true;
            _playerInput.SwitchCurrentControlScheme(keyboardControlScheme, Keyboard.current);
            _playerInput.SwitchCurrentActionMap("Player");
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
        // The existing Run binding is a one-shot dash button for both keyboard schemes.
        // Dash triggers from dedicated Dash action (gamepad buttonSouth/leftTrigger, keyboard shift/alt) or legacy Run action.
        public bool DashPressed => (GetAction("Dash")?.WasPressedThisFrame() ?? false) || (GetAction("Run")?.WasPressedThisFrame() ?? false);
        public bool ThrowPressed => GetAction("Throw")?.WasPressedThisFrame() ?? false;
    }
}
