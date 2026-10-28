using UnityEngine;
using HPlayer;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(LocalPlayerInput))]
public class ThirdPersonPlayerController : MonoBehaviour
{
    private LocalPlayerInput _localInput;
    private Rigidbody _rb;
    [SerializeField] private Animator _animator;

    [Header("Movement Settings")]
    [SerializeField] private float _moveSpeed = 8f;
    [SerializeField] private float _rotationSpeed = 15f;
    [SerializeField, Min(1f)] private float _runMultiplier = 1.65f;
    [SerializeField, Min(.1f)] private float _acceleration = 14f;
    [SerializeField, Min(.1f)] private float _deceleration = 20f;
    [SerializeField, Min(.01f)] private float _animationDamping = .15f;
    private bool _runRequested, _hasLocomotionRate;
    private InteractionController _interaction;
    private float _animatedSpeed;

    private float _speedModifier = 1f;
    private Transform _lookTarget;
    private Vector2 _inputVector;
    private bool _movementLocked;
    private static readonly int WalkingId = Animator.StringToHash("isWalking");
    private static readonly int RunningId = Animator.StringToHash("isRunning");
    private static readonly int RateId = Animator.StringToHash("LocomotionRate");

    public Animator CharacterAnimator => _animator;
    public float BaseMoveSpeed => _moveSpeed;
    public Vector2 RequestedMove => !_movementLocked && _localInput ? _localInput.MoveInput : Vector2.zero;
    public TA.AdaptiveAnimation.CooperativeCarryObject CooperativeCarry { get; set; }
    public TA.AdaptiveAnimation.CooperativeFireHose CooperativeHose { get; set; }
    private float knockbackRemaining;
    public bool IsKnockedBack => knockbackRemaining > 0f;

    // A short horizontal recoil keeps this top-down controller on its established ground plane.
    public void ApplyHoseKnockback(Vector3 direction, float speed, float duration)
    {
        direction.y = 0f;
        knockbackRemaining = Mathf.Max(.1f, duration);
        SetVelocity(direction.normalized * speed);
    }

    private void Awake()
    {
        _localInput = GetComponent<LocalPlayerInput>();
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        // Mempertahankan gerakan di bidang XZ dari controller awal.
        _rb.constraints = RigidbodyConstraints.FreezePositionY |
                          RigidbodyConstraints.FreezeRotation;
        if (!_animator) _animator = GetComponentInChildren<Animator>();
        _interaction = GetComponent<InteractionController>();
        if (_animator)
            foreach (var parameter in _animator.parameters)
                if (parameter.nameHash == RateId) _hasLocomotionRate = true;

    }


    private void OnDisable()
    {
        knockbackRemaining = 0f;
        _inputVector = Vector2.zero;
        StopHorizontalMovement();
        if (_animator) _animator.SetBool(WalkingId, false);
        if (_animator) _animator.SetBool(RunningId, false);
        _animatedSpeed = 0f;
    }

    private void Update()
    {
        _inputVector = _localInput ? Vector2.ClampMagnitude(_localInput.MoveInput, 1f) : Vector2.zero;
        _runRequested = _localInput && _localInput.RunHeld;
        Vector3 actual = CooperativeCarry ? CooperativeCarry.GetPlayerVelocity(_interaction) :
            CooperativeHose ? CooperativeHose.GetPlayerVelocity(_interaction) : GetVelocity();
        actual.y = 0f;
        UpdateLocomotionAnimation(_movementLocked || IsKnockedBack ? 0f : actual.magnitude, Time.deltaTime);
    }

    private void UpdateLocomotionAnimation(float speed, float deltaTime)
    {
        if (!_animator) return;
        _animatedSpeed = Mathf.Lerp(_animatedSpeed, speed, 1f - Mathf.Exp(-deltaTime / _animationDamping));
        bool wasWalking = _animator.GetBool(WalkingId);
        bool walking = _animatedSpeed > (wasWalking ? .035f : .08f);
        bool running = !CooperativeCarry && !CooperativeHose && _animatedSpeed > _moveSpeed * Mathf.Max(.01f, _speedModifier) *
            (_animator.GetBool(RunningId) ? 1.08f : 1.2f);
        _animator.SetBool(WalkingId, walking);
        _animator.SetBool(RunningId, walking && running);
        if (_hasLocomotionRate)
        {
            float reference = _moveSpeed * (running ? _runMultiplier : 1f);
            _animator.SetFloat(RateId, Mathf.Clamp(_animatedSpeed / Mathf.Max(.1f, reference), .12f, 1.3f),
                _animationDamping, deltaTime);
        }
    }

    private float DesiredMoveSpeed(bool run) => _moveSpeed * _speedModifier *
        (run && !CooperativeCarry && !(_interaction && _interaction.ExternalInteractionLocked) ? _runMultiplier : 1f);

    private void FixedUpdate()
    {
        // One coordinator moves both players; individual controllers must not fight it.
        if (IsKnockedBack)
        {
            knockbackRemaining = Mathf.Max(0f, knockbackRemaining - Time.fixedDeltaTime);
            SetVelocity(Vector3.MoveTowards(GetVelocity(), Vector3.zero, 5f * Time.fixedDeltaTime));
            return;
        }
        if (CooperativeCarry || CooperativeHose) return;
        // Berhenti juga jika sumber input dinonaktifkan di antara Update.
        if (!_localInput || !_localInput.IsReady) _inputVector = Vector2.zero;
        if (_movementLocked)
        {
            StopHorizontalMovement();
            return;
        }

        Vector3 direction = new Vector3(_inputVector.x, 0f, _inputVector.y);
        Vector3 current = GetVelocity();
        Vector3 targetVelocity = direction * DesiredMoveSpeed(_runRequested);
        Vector3 horizontal = new Vector3(current.x, 0f, current.z);
        Vector3 velocity = Vector3.MoveTowards(horizontal, targetVelocity,
            (targetVelocity.sqrMagnitude > horizontal.sqrMagnitude ? _acceleration : _deceleration) * Time.fixedDeltaTime);
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
        if (_animator) _animator.SetBool(RunningId, false);
        _animatedSpeed = 0f;
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
