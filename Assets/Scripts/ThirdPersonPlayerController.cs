using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class ThirdPersonPlayerController : MonoBehaviour
{
    private InputSystem_Actions _playerInput;
    private Rigidbody _rb;
    [SerializeField] private Animator _animator;

    [Header("Movement Settings")]
    [SerializeField] private float _moveSpeed = 8f;
    [SerializeField] private float _rotationSpeed = 15f;

    private float _speedModifier = 1f;
    private Transform _lookTarget;
    private Vector2 _inputVector;
    private bool _movementLocked;
    private static readonly int WalkingId = Animator.StringToHash("isWalking");

    public Animator CharacterAnimator => _animator;

    private void Awake()
    {
        _playerInput = new InputSystem_Actions();
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        // Mempertahankan gerakan di bidang XZ dari controller awal.
        _rb.constraints = RigidbodyConstraints.FreezePositionY |
                          RigidbodyConstraints.FreezeRotation;
        if (!_animator) _animator = GetComponentInChildren<Animator>();

        _playerInput.Player.Move.started += OnMovementInput;
        _playerInput.Player.Move.performed += OnMovementInput;
        _playerInput.Player.Move.canceled += OnMovementInput;
    }

    private void OnEnable() => _playerInput.Player.Enable();

    private void OnDisable()
    {
        _playerInput.Player.Disable();
        _inputVector = Vector2.zero;
        StopHorizontalMovement();
        if (_animator) _animator.SetBool(WalkingId, false);
    }

    private void OnDestroy()
    {
        if (_playerInput == null) return;
        _playerInput.Player.Move.started -= OnMovementInput;
        _playerInput.Player.Move.performed -= OnMovementInput;
        _playerInput.Player.Move.canceled -= OnMovementInput;
        _playerInput.Dispose();
    }

    private void OnMovementInput(InputAction.CallbackContext context)
    {
        _inputVector = Vector2.ClampMagnitude(context.ReadValue<Vector2>(), 1f);
    }

    private void Update()
    {
        if (_animator)
            _animator.SetBool(WalkingId, !_movementLocked &&
                _inputVector.sqrMagnitude > 0.001f && _speedModifier > 0f);
    }

    private void FixedUpdate()
    {
        if (_movementLocked)
        {
            StopHorizontalMovement();
            return;
        }

        Vector3 direction = new Vector3(_inputVector.x, 0f, _inputVector.y);
        Vector3 velocity = direction * _moveSpeed * _speedModifier;
        velocity.y = GetVelocity().y;
        SetVelocity(velocity);

        bool moving = direction.sqrMagnitude > 0.001f;
        float rotationSpeed = _rotationSpeed;
        if (_lookTarget)
        {
            direction = _lookTarget.position - _rb.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) return;
            float angle = Vector3.Angle(_rb.rotation * Vector3.forward, direction);
            if (!(moving || angle > 30f) || angle <= 5f) return;
            rotationSpeed *= 0.5f;
        }
        else if (!moving) return;

        Quaternion target = Quaternion.LookRotation(direction);
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, target,
            rotationSpeed * Time.fixedDeltaTime));
    }

    public void SetMovementState(float penalty, Transform lookTarget)
    {
        _speedModifier = Mathf.Clamp01(1f - penalty);
        _lookTarget = lookTarget;
    }

    public void SetMovementLocked(bool locked)
    {
        _movementLocked = locked;
        if (!locked) return;
        StopHorizontalMovement();
        if (_animator) _animator.SetBool(WalkingId, false);
    }

    private void StopHorizontalMovement()
    {
        if (!_rb || _rb.isKinematic) return;
        Vector3 velocity = GetVelocity();
        SetVelocity(new Vector3(0f, velocity.y, 0f));
    }

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return _rb.linearVelocity;
#else
        return _rb.velocity;
#endif
    }

    private void SetVelocity(Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        _rb.linearVelocity = velocity;
#else
        _rb.velocity = velocity;
#endif
    }
}
