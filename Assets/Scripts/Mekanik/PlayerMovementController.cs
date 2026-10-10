using UnityEngine;

namespace DisasterSquad.Mekanik
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerManager))]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerMovementController : MonoBehaviour
    {
        public enum MovementSpace
        {
            World,
            CameraRelative
        }

        [Header("References")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private Transform cameraTransform;

        [Header("Movement")]
        [SerializeField] private MovementSpace movementSpace = MovementSpace.World;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 15f;
        [SerializeField] private float inputDeadZone = 0.05f;
        [SerializeField] private bool preserveVerticalVelocity = true;

        private Rigidbody playerRigidbody;
        private Vector2 moveInput;

        private void Awake()
        {
            playerRigidbody = GetComponent<Rigidbody>();

            if (playerManager == null)
                playerManager = GetComponent<PlayerManager>();

            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            inputReader.OnMoveChanged += SetMoveInput;
        }

        private void OnDisable()
        {
            inputReader.OnMoveChanged -= SetMoveInput;
            playerManager.SetWalking(false);
        }

        private void FixedUpdate()
        {
            Vector3 moveDirection = GetMoveDirection();
            bool isWalking = moveDirection.sqrMagnitude > inputDeadZone * inputDeadZone;

            Move(moveDirection, isWalking);
            Rotate(moveDirection, isWalking);

            playerManager.SetWalking(isWalking);
        }

        private void SetMoveInput(Vector2 input)
        {
            moveInput = input;
        }

        private void Move(Vector3 moveDirection, bool isWalking)
        {
            Vector3 targetVelocity = Vector3.zero;

            if (isWalking)
            {
                float speed = moveSpeed * playerManager.MovementSpeedMultiplier;
                targetVelocity = moveDirection * speed;
            }

            if (preserveVerticalVelocity)
                targetVelocity.y = playerRigidbody.velocity.y;

            playerRigidbody.velocity = targetVelocity;
        }

        private void Rotate(Vector3 moveDirection, bool isWalking)
        {
            Vector3 lookDirection = moveDirection;

            if (playerManager.ForcedLookTarget != null)
                lookDirection = playerManager.ForcedLookTarget.position - transform.position;

            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.001f) return;
            if (!isWalking && playerManager.ForcedLookTarget == null) return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized);
            Quaternion nextRotation = Quaternion.Slerp(
                playerRigidbody.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime);

            playerRigidbody.MoveRotation(nextRotation);
        }

        private Vector3 GetMoveDirection()
        {
            Vector2 clampedInput = Vector2.ClampMagnitude(moveInput, 1f);

            if (movementSpace == MovementSpace.CameraRelative && cameraTransform != null)
                return GetCameraRelativeDirection(clampedInput);

            return new Vector3(clampedInput.x, 0f, clampedInput.y);
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            return right * input.x + forward * input.y;
        }
    }
}
