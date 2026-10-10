using System;
using UnityEngine;

namespace DisasterSquad.Mekanik
{
    public enum PlayerActivityState
    {
        Idle,
        Walk,
        Interact,
        Carry
    }

    public class PlayerManager : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private string walkParameter = "isWalking";
        [SerializeField] private string interactParameter = "isInteracting";
        [SerializeField] private string carryParameter = "isCarrying";

        public PlayerActivityState CurrentState { get; private set; } = PlayerActivityState.Idle;
        public bool IsWalking { get; private set; }
        public bool IsInteracting { get; private set; }
        public bool IsCarrying => CarriedItem != null;
        public InteractableItem CarriedItem { get; private set; }
        public float MovementSpeedMultiplier { get; private set; } = 1f;
        public Transform ForcedLookTarget { get; private set; }

        public event Action<PlayerActivityState, PlayerActivityState> OnStateChanged;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            RefreshState();
        }

        public void SetWalking(bool isWalking)
        {
            if (IsWalking == isWalking) return;

            IsWalking = isWalking;
            RefreshState();
        }

        public void BeginInteract()
        {
            if (IsInteracting) return;

            IsInteracting = true;
            RefreshState();
        }

        public void EndInteract()
        {
            if (!IsInteracting) return;

            IsInteracting = false;
            RefreshState();
        }

        public void SetCarriedItem(InteractableItem item)
        {
            if (CarriedItem == item) return;

            CarriedItem = item;
            RefreshState();
        }

        public void ApplyCarryModifiers(float movementSpeedMultiplier, Transform forcedLookTarget)
        {
            MovementSpeedMultiplier = Mathf.Clamp01(movementSpeedMultiplier);
            ForcedLookTarget = forcedLookTarget;
        }

        public void ClearCarryModifiers()
        {
            MovementSpeedMultiplier = 1f;
            ForcedLookTarget = null;
        }

        private void RefreshState()
        {
            PlayerActivityState nextState = ResolveState();
            if (CurrentState != nextState)
            {
                PlayerActivityState previousState = CurrentState;
                CurrentState = nextState;
                OnStateChanged?.Invoke(previousState, nextState);
            }

            UpdateAnimator();
        }

        private PlayerActivityState ResolveState()
        {
            if (IsInteracting) return PlayerActivityState.Interact;
            if (IsCarrying) return PlayerActivityState.Carry;
            if (IsWalking) return PlayerActivityState.Walk;
            return PlayerActivityState.Idle;
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;

            SetAnimatorBool(walkParameter, IsWalking);
            SetAnimatorBool(interactParameter, IsInteracting);
            SetAnimatorBool(carryParameter, IsCarrying);
        }

        private void SetAnimatorBool(string parameterName, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return;

            animator.SetBool(parameterName, value);
        }
    }
}
