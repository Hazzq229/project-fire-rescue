using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class ThirdPersonPlayerController : MonoBehaviour
{
    private InputSystem_Actions _playerInput;
    private Rigidbody _rb;
    [SerializeField] private Animator _animator;

    [Header("Movement Settings")]
    [SerializeField] private float _moveSpeed = 8f;
    [SerializeField] private float _rotationSpeed = 15f;
    [Header("Carry Movement Feel Settings")]
    private float _speedModifier = 1f;
    private Transform _lookTarget = null;

    private Vector2 _inputVector;
    private bool _isMoving;

    // --- TAMBAHAN UNTUK CAMERA-RELATIVE ---
    private Vector3 _currentMoveDirection;
    private Transform _mainCameraTransform; // Cache camera transform biar lebih ringan

    private void Awake()
    {
        _playerInput = new InputSystem_Actions();
        _rb = GetComponent<Rigidbody>();
        
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        
        _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        
        // Cache main camera di awal
        if (Camera.main != null)
            _mainCameraTransform = Camera.main.transform;
        else
            Debug.LogWarning("Main Camera tidak ditemukan! Pastikan objek kamera memiliki tag 'MainCamera'.");
        
        // Setup Input Events
        _playerInput.Player.Move.started += OnMovementInput;
        _playerInput.Player.Move.performed += OnMovementInput;
        _playerInput.Player.Move.canceled += OnMovementInput;
    }

    private void OnEnable() => _playerInput.Player.Enable();
    private void OnDisable() => _playerInput.Player.Disable();

    private void OnMovementInput(InputAction.CallbackContext context)
    {
        _inputVector = context.ReadValue<Vector2>();
        _isMoving = _inputVector.x != 0 || _inputVector.y != 0;
    }

    private void Update()
    {
        HandleAnimation();
    }

    private void FixedUpdate()
    {
        CalculateCameraRelativeDirection(); // Hitung arah kamera dulu
        MovePlayer();
        RotatePlayer();
    }

    public void SetMovementState(float penalty, Transform lookTarget)
    {
        _speedModifier = Mathf.Clamp01(1f - penalty);
        _lookTarget = lookTarget;
    }

    // --- METHOD BARU: Menghitung arah berdasarkan kamera ---
    private void CalculateCameraRelativeDirection()
    {
        if (_mainCameraTransform == null) return;

        Vector3 camForward = _mainCameraTransform.forward;
        Vector3 camRight = _mainCameraTransform.right;

        // Abaikan sumbu Y agar perhitungan gerak tetap di permukaan datar
        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();

        // Gabungkan input dengan arah kamera
        _currentMoveDirection = (camForward * _inputVector.y + camRight * _inputVector.x).normalized;
    }

    private void MovePlayer()
    {
        // Gunakan _currentMoveDirection, bukan lagi dari _inputVector.x/y langsung
        Vector3 targetVelocity = _currentMoveDirection * _moveSpeed * _speedModifier;
        
        // mengambil Velocity Y yang lama (Gravitasi) agar karakter tidak melayang
        targetVelocity.y = _rb.velocity.y; 

        // Set velocity Rigidbody
        _rb.velocity = targetVelocity;
    }

    private void RotatePlayer()
    {
        if (_lookTarget != null)
        {
            Vector3 directionToObj = _lookTarget.position - transform.position;
            directionToObj.y = 0;

            if(directionToObj.sqrMagnitude > 0.001f)
            {
                float angleDifference = Vector3.Angle(transform.forward, directionToObj);
                
                bool shouldRotate = (_isMoving || angleDifference > 30f) && angleDifference > 5f;

                if(shouldRotate)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToObj);    
                    float smoothSpeed = _rotationSpeed * 0.5f;
                    _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, smoothSpeed * Time.fixedDeltaTime));
                }
            }
        }
        else if (_isMoving)
        {
            // Arah tujuan hadap diubah menjadi _currentMoveDirection
            if(_currentMoveDirection.sqrMagnitude > 0.001f)
            {
                // Hitung rotasi target
                Quaternion targetRotation = Quaternion.LookRotation(_currentMoveDirection);
                
                // Gunakan MoveRotation untuk memutar Rigidbody secara fisik
                Quaternion nextRotation = Quaternion.Slerp(_rb.rotation, targetRotation, _rotationSpeed * Time.fixedDeltaTime);
                _rb.MoveRotation(nextRotation);
            }
        }
    }

    private void HandleAnimation()
    {
        if (_animator == null) return;

        bool isWalking = _animator.GetBool("isWalking");
        
        if (_isMoving && !isWalking)
            _animator.SetBool("isWalking", true);
        else if (!_isMoving && isWalking)
            _animator.SetBool("isWalking", false);
    }
}