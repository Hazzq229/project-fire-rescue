using UnityEngine;
using NaughtyAttributes;
using HInteractions;
using System;
using HGame.Objects;

namespace HPlayer
{
    [RequireComponent(typeof(Rigidbody))] 
    public class InteractionController : MonoBehaviour, IObjectHolder
    {
        [Header("Hold Settings")]
        [SerializeField, Required] private Transform handTransform;
        [SerializeField, Required] private Collider handTrigger;
        [SerializeField] private int heldObjectLayer;
        
        [Header("Physics Joint Settings")]
        [Tooltip("Kekuatan pegas (makin tinggi makin kaku)")][SerializeField] private float jointSpring = 1500f;
        [Tooltip("Peredam getaran agar biar tidak memantul berlebihan")][SerializeField] private float jointDamper = 100f;
        [Tooltip("Kekuatan memutar objek")][SerializeField] private float rotateForce = 200f;   

        [Header("Throw Settings")]
        [SerializeField] private float throwForce = 10f;

        [field: SerializeField, ReadOnly] public Liftable HeldObject { get; private set; } = null;
        [field: Header("Input")]
        [field: SerializeField, ReadOnly] public bool Interacting { get; private set; } = false;

        [SerializeField] private ThirdPersonPlayerController playerController;
        public event Action OnInteractionStart;
        public event Action OnInteractionEnd;

        private Liftable currentCandidate;
        private SpringJoint grabJoint;
        [SerializeField] private Rigidbody playerRb;

        public Interactable SelectedObject => currentCandidate != null ? currentCandidate : HeldObject;

        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            if(!playerController) playerController = GetComponent<ThirdPersonPlayerController>();
        }

        private void OnEnable()
        {
            OnInteractionStart += ChangeHeldObject;
        }

        private void OnDisable()
        {
            OnInteractionStart -= ChangeHeldObject;
        }

        private void Update()
        {
            UpdateInput();
        }

        private void FixedUpdate()
        {
            if (HeldObject)
            {
                RotateHeldObjectPhysics();
            }
        }

        #region - Input -

        private void UpdateInput()
        {
            // Input System lama (bisa diganti New Input System sesuai script sebelumnya)
            bool interacting = Input.GetMouseButton(0);
            
            if (interacting != Interacting)
            {
                Interacting = interacting;
                if (interacting)
                    OnInteractionStart?.Invoke();
                else
                    OnInteractionEnd?.Invoke();
            }

            if (Input.GetMouseButtonDown(1)) 
            {
                if (HeldObject is HGame.Objects.HoseNozzle nozzle)
                {
                    nozzle.ToggleShooting();
                }
            }
        }

        #endregion

        #region - Trigger Detection -

        private void OnTriggerEnter(Collider other)
        {
            if (HeldObject) return;

            if (other.TryGetComponent(out Liftable liftable))
            {
                currentCandidate = liftable;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (currentCandidate && other.TryGetComponent(out Liftable liftable) && liftable == currentCandidate)
                currentCandidate = null;
        }

        #endregion

        #region - Hold System (Physics Based) -

        // Fungsi ini menggantikan UpdateHeldObjectPosition yang lama
        private void RotateHeldObjectPhysics()
        {
            if (HeldObject == null || HeldObject.Rigidbody == null) return;

            Rigidbody objRb = HeldObject.Rigidbody;

            // 1. Tentukan rotasi target (sesuai arah tangan player)
            Quaternion targetRotation = handTransform.rotation * Quaternion.Euler(HeldObject.LiftDirectionOffset);

            // 2. Hitung perbedaan rotasi
            // Ini teknik merubah Quaternion diff menjadi Vector3 angular velocity
            Quaternion rotationDiff = targetRotation * Quaternion.Inverse(objRb.rotation);
            rotationDiff.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);

            // Normalisasi sudut (biar handle > 180 derajat)
            if (angleInDegrees > 180f) angleInDegrees -= 360f;

            // Kunci: Hanya putar jika sudutnya signifikan (optimasi)
            if (Mathf.Abs(angleInDegrees) > 1f) 
            {
                // Konversi derajat ke radian untuk fisika
                Vector3 angularDisplacement = rotationAxis * (angleInDegrees * Mathf.Deg2Rad);
                
                // Terapkan Torque (Gaya putar)
                // Kita kurangi dengan angularVelocity sekarang supaya gerakan tidak overshoot (Damping)
                Vector3 torque = (angularDisplacement * rotateForce) - (objRb.angularVelocity * 10f);
                
                objRb.AddTorque(torque, ForceMode.Acceleration);
            }
        }

        private void ChangeHeldObject()
        {
            if (HeldObject)
                DropObject(HeldObject, throwObject: true); // Tambah fitur lempar
            else if (currentCandidate)
                PickUpObject(currentCandidate);
        }

        private void PickUpObject(Liftable obj)
        {
            if (obj == null) return;
            if (!obj.CanBePickedUp()) return;

            HeldObject = obj;
            currentCandidate = null;
            
            // prepare liftable object
            obj.PickUp(this, heldObjectLayer);

            // calculate anchor point
            Vector3 finalAnchorPos = obj.GetGrabPoint(handTransform.position);

            // Physics joint setup
            grabJoint = gameObject.AddComponent<SpringJoint>();
            grabJoint.autoConfigureConnectedAnchor = false;
            
            // hand's anchor
            grabJoint.anchor = transform.InverseTransformPoint(handTransform.position);
            
            // object's anchor
            grabJoint.connectedAnchor = obj.transform.InverseTransformPoint(finalAnchorPos);
            grabJoint.connectedBody = obj.Rigidbody;

            // tuning joint behavior
            grabJoint.spring = jointSpring;
            grabJoint.damper = jointDamper;
            grabJoint.enableCollision = false;
            
            // configure joint distance
            grabJoint.maxDistance = 0f;
            grabJoint.minDistance = 0f;
            grabJoint.tolerance = 0.025f;

            float penalty = obj.SpeedPenalty;
            Transform targetLook = obj.ForceFaceObject ? obj.transform : null;

            playerController.SetMovementState(penalty, targetLook);
        }

        private void DropObject(Liftable obj, bool throwObject = false)
        {
            if (obj == null) return;

            // Hancurkan Joint (Lepas pegangan)
            if (grabJoint != null)
            {
                Destroy(grabJoint);
            }

            // Fitur Lempar (Momentum)
            if (throwObject && playerRb)
            {
                // Gabungkan velocity player + arah hadap
                Vector3 throwVec = playerRb.velocity + (transform.forward * throwForce);
                obj.Rigidbody.velocity = throwVec;
            }

            playerController.SetMovementState(0f, null);

            HeldObject = null;
            obj.Drop(this);
        }

        #endregion
    }
}