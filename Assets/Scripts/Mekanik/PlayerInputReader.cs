using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DisasterSquad.Mekanik
{
    public class PlayerInputReader : MonoBehaviour
    {
        public enum InputSource
        {
            GeneratedActions,
            PlayerInputComponent
        }

        [Header("Input Source")]
        [SerializeField] private InputSource inputSource = InputSource.GeneratedActions;
        [SerializeField] private PlayerInput playerInput;

        [Header("Action Names")]
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string interactActionName = "Interact";
        [SerializeField] private string actActionName = "Act";

        public Vector2 MoveInput { get; private set; }
        public bool HasMoveInput => MoveInput.sqrMagnitude > 0.01f;

        public event Action<Vector2> OnMoveChanged;
        public event Action OnInteractPressed;
        public event Action OnInteractReleased;
        public event Action OnActPressed;

        private InputSystem_Actions generatedActions;
        private InputAction moveAction;
        private InputAction interactAction;
        private InputAction actAction;
        private bool actionsSubscribed;

        private void Awake()
        {
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            BindActions();
        }

        private void OnDisable()
        {
            UnbindActions();
            MoveInput = Vector2.zero;
            OnMoveChanged?.Invoke(MoveInput);
        }

        private void OnDestroy()
        {
            generatedActions?.Dispose();
        }

        private void BindActions()
        {
            if (inputSource == InputSource.PlayerInputComponent && TryBindPlayerInputActions())
                return;

            BindGeneratedActions();
        }

        private bool TryBindPlayerInputActions()
        {
            if (playerInput == null || playerInput.actions == null)
                return false;

            moveAction = playerInput.actions.FindAction(moveActionName, false);
            interactAction = playerInput.actions.FindAction(interactActionName, false);
            actAction = playerInput.actions.FindAction(actActionName, false);

            if (moveAction == null)
                return false;

            SubscribeActions();
            moveAction?.Enable();
            interactAction?.Enable();
            actAction?.Enable();

            return true;
        }

        private void BindGeneratedActions()
        {
            generatedActions ??= new InputSystem_Actions();

            moveAction = generatedActions.Player.Move;
            interactAction = generatedActions.Player.Interact;
            actAction = generatedActions.Player.Act;

            SubscribeActions();
            generatedActions.Player.Enable();
        }

        private void SubscribeActions()
        {
            if (actionsSubscribed) return;

            if (moveAction != null)
            {
                moveAction.started += HandleMove;
                moveAction.performed += HandleMove;
                moveAction.canceled += HandleMove;
            }

            if (interactAction != null)
            {
                interactAction.started += HandleInteractStarted;
                interactAction.canceled += HandleInteractCanceled;
            }

            if (actAction != null)
                actAction.started += HandleActStarted;

            actionsSubscribed = true;
        }

        private void UnbindActions()
        {
            if (!actionsSubscribed) return;

            if (moveAction != null)
            {
                moveAction.started -= HandleMove;
                moveAction.performed -= HandleMove;
                moveAction.canceled -= HandleMove;
            }

            if (interactAction != null)
            {
                interactAction.started -= HandleInteractStarted;
                interactAction.canceled -= HandleInteractCanceled;
            }

            if (actAction != null)
                actAction.started -= HandleActStarted;

            generatedActions?.Player.Disable();

            moveAction = null;
            interactAction = null;
            actAction = null;
            actionsSubscribed = false;
        }

        private void HandleMove(InputAction.CallbackContext context)
        {
            MoveInput = context.ReadValue<Vector2>();
            OnMoveChanged?.Invoke(MoveInput);
        }

        private void HandleInteractStarted(InputAction.CallbackContext context)
        {
            OnInteractPressed?.Invoke();
        }

        private void HandleInteractCanceled(InputAction.CallbackContext context)
        {
            OnInteractReleased?.Invoke();
        }

        private void HandleActStarted(InputAction.CallbackContext context)
        {
            OnActPressed?.Invoke();
        }
    }
}
