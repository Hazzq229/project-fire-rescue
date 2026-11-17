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
    [Tooltip("Free walking only; carried loads and cooperative movement keep Base Move Speed.")]
    [SerializeField, Min(1f)] private float _unladenWalkSpeedMultiplier = 1f;
    [SerializeField] private float _rotationSpeed = 15f;
    [SerializeField, Min(.1f)] private float _dashSpeed = 6f;
    [SerializeField, Min(.01f)] private float _dashDuration = .28f;
    [SerializeField, Min(0f)] private float _dashCooldown = .65f;
    [SerializeField, Min(.1f)] private float _acceleration = 14f;
    [SerializeField, Min(.1f)] private float _deceleration = 20f;
    [SerializeField, Min(.01f)] private float _animationDamping = .15f;
    [SerializeField, Min(.1f)] private float _walkAnimationReferenceSpeed = 1f;
    private bool _hasLocomotionRate;
    private float _dashRemaining, _dashCooldownRemaining;
    private Vector3 _dashDirection;
    private InteractionController _interaction;
    private float _animatedSpeed;

    private float _speedModifier = 1f;
    private Transform _lookTarget;
    private Vector2 _inputVector;
    private bool _movementLocked;
    private static readonly int WalkingId = Animator.StringToHash("isWalking");
    private static readonly int DashingId = Animator.StringToHash("isDashing");
    private static readonly int RateId = Animator.StringToHash("LocomotionRate");

    public Animator CharacterAnimator => _animator;
    public float BaseMoveSpeed => _moveSpeed;
    public float UnladenMoveSpeed => _moveSpeed * _unladenWalkSpeedMultiplier;
    public Vector2 RequestedMove => !_movementLocked && _localInput ? _localInput.MoveInput : Vector2.zero;
    public TA.AdaptiveAnimation.CooperativeCarryObject CooperativeCarry { get; set; }
    public TA.AdaptiveAnimation.CooperativeFireHose CooperativeHose { get; set; }
    // Coordinators report each operator's movement, including formation rotation.
    // Both cadence and the visible model's stride plane must consume the same motion.
    public Vector3 LocomotionVelocity
    {
        get
        {
            Vector3 velocity = CooperativeCarry ? CooperativeCarry.GetPlayerVelocity(_interaction) :
                CooperativeHose ? CooperativeHose.GetPlayerVelocity(_interaction) :
                _rb ? GetVelocity() : Vector3.zero;
            velocity.y = 0f;
            return velocity;
        }
    }
    public bool UsesCarryStrideDirection => CooperativeCarry || CooperativeHose ||
        (_interaction && _interaction.HeldObject);
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
        _dashRemaining = _dashCooldownRemaining = 0f;
        _inputVector = Vector2.zero;
        StopHorizontalMovement();
        if (_animator) _animator.SetBool(WalkingId, false);
        if (_animator) _animator.SetBool(DashingId, false);
        _animatedSpeed = 0f;
    }

    private void Update()
    {
        _inputVector = _localInput ? Vector2.ClampMagnitude(_localInput.MoveInput, 1f) : Vector2.zero;
        if (_localInput && _localInput.DashPressed) TryStartDash();
        Vector3 actual = LocomotionVelocity;
        UpdateLocomotionAnimation(_movementLocked || IsKnockedBack ? 0f : actual.magnitude, Time.deltaTime);
    }

    private void UpdateLocomotionAnimation(float speed, float deltaTime)
    {
        if (!_animator) return;
        _animatedSpeed = Mathf.Lerp(_animatedSpeed, speed, 1f - Mathf.Exp(-deltaTime / _animationDamping));
        bool wasWalking = _animator.GetBool(WalkingId);
        // A solo heavy carry can legitimately move at only 0.04-0.05 m/s.
        // State changes still use unsmoothed motion so stopping does not retain a walk tail.
        bool walking = speed > (wasWalking ? .005f : .01f);
        _animator.SetBool(WalkingId, walking);
        _animator.SetBool(DashingId, _dashRemaining > 0f);
        if (_hasLocomotionRate)
        {
            float direction = UsesCarryStrideDirection &&
                Vector3.Dot(LocomotionVelocity.normalized, transform.forward) < -.1f ? -1f : 1f;
            // A playback floor makes slow heavy loads cycle faster than they travel.
            float maximumRate = IsUnladen ? Mathf.Max(1.3f, _unladenWalkSpeedMultiplier) : 1.3f;
            _animator.SetFloat(RateId, direction * Mathf.Clamp(_animatedSpeed / _walkAnimationReferenceSpeed, 0f, maximumRate));
        }
    }

    private bool IsUnladen => !CooperativeCarry && !CooperativeHose &&
        (!_interaction || (!_interaction.HeldObject && !_interaction.IsPickingUp &&
            !_interaction.IsPlacingDown && !_interaction.ExternalInteractionLocked));

    private float DesiredMoveSpeed() => (IsUnladen ? UnladenMoveSpeed : _moveSpeed) * _speedModifier;

    private bool DashBlocked => _movementLocked || IsKnockedBack || CooperativeCarry || CooperativeHose ||
        (_interaction && (_interaction.ExternalInteractionLocked || _interaction.Interacting ||
                          _interaction.IsPickingUp || _interaction.IsPlacingDown || _interaction.HeldObject));

    private bool TryStartDash()
    {
        if (_dashRemaining > 0f || _dashCooldownRemaining > 0f || DashBlocked) return false;
        _dashDirection = new Vector3(_inputVector.x, 0f, _inputVector.y);
        if (_dashDirection.sqrMagnitude < .01f) _dashDirection = transform.forward;
        _dashDirection.y = 0f;
        _dashDirection.Normalize();
        _dashRemaining = _dashDuration;
        _dashCooldownRemaining = _dashDuration + _dashCooldown;
        if (_animator) _animator.SetBool(DashingId, true);
        return true;
    }

    private void FixedUpdate()
    {
        _dashCooldownRemaining = Mathf.Max(0f, _dashCooldownRemaining - Time.fixedDeltaTime);
        if (_dashRemaining > 0f && DashBlocked)
            _dashRemaining = 0f;
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
        if (_dashRemaining > 0f)
        {
            _dashRemaining = Mathf.Max(0f, _dashRemaining - Time.fixedDeltaTime);
            SetVelocity(_dashDirection * _dashSpeed);
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(_dashDirection),
                _rotationSpeed * Time.fixedDeltaTime));
            return;
        }

        Vector3 direction = new Vector3(_inputVector.x, 0f, _inputVector.y);
        Vector3 current = GetVelocity();
        Vector3 targetVelocity = direction * DesiredMoveSpeed();
        Vector3 horizontal = new Vector3(current.x, 0f, current.z);
        Vector3 velocity = Vector3.MoveTowards(horizontal, targetVelocity,
            (targetVelocity.sqrMagnitude > horizontal.sqrMagnitude ? _acceleration : _deceleration) * Time.fixedDeltaTime);
        velocity.y = GetVelocity().y;

        bool moving = direction.sqrMagnitude > 0.001f;
        float rotationSpeed = _rotationSpeed;
        if (_lookTarget)
        {
            direction = _lookTarget.position - _rb.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) { ApplyMovement(velocity, _rb.rotation); return; }
            float angle = Vector3.Angle(_rb.rotation * Vector3.forward, direction);
            if (!(moving || angle > 30f) || angle <= 5f) { ApplyMovement(velocity, _rb.rotation); return; }
            rotationSpeed *= 0.5f;
        }
        else if (!moving) { ApplyMovement(velocity, _rb.rotation); return; }

        Quaternion target = Quaternion.LookRotation(direction);
        ApplyMovement(velocity, Quaternion.Slerp(_rb.rotation, target, rotationSpeed * Time.fixedDeltaTime));
    }

    private void ApplyMovement(Vector3 velocity, Quaternion rotation)
    {
        if (_interaction && _interaction.HeldObject &&
            _interaction.HeldObject.TryGetComponent<HInteractions.StableCarryAttachment>(out var attachment))
        {
            Vector3 delta = velocity * Time.fixedDeltaTime;
            attachment.ConstrainMovement(ref delta, ref rotation);
            velocity = delta / Time.fixedDeltaTime;
        }
        SetVelocity(velocity);
        _rb.MoveRotation(rotation);
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
        _dashRemaining = 0f;
        StopHorizontalMovement();
        if (_animator) _animator.SetBool(WalkingId, false);
        if (_animator) _animator.SetBool(DashingId, false);
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
