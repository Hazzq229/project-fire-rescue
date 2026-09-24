using System;
using System.Collections.Generic;
using HInteractions;
using HGame.Objects;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] private float jointSpring = 1500f;
        [SerializeField] private float jointDamper = 100f;
        [SerializeField] private float rotateForce = 200f;

        [Header("Throw Settings")]
        [SerializeField] private float throwForce = 10f;

        [Header("Pickup Animation")]
        [SerializeField] private Animator animator;
        [SerializeField, Min(0.1f)] private float maxPickupReach = 2.5f;
        [Tooltip("Batas menunggu Animator masuk ke state Pickup.")]
        [SerializeField, Min(0.1f)] private float pickupStartTimeout = 1f;

        [Header("Input")]
        [Tooltip("Matikan untuk pemain yang dikendalikan input terpisah.")]
        [SerializeField] private bool readMouseInput = true;

        [field: SerializeField, ReadOnly]
        public Liftable HeldObject { get; private set; }
        [field: SerializeField, ReadOnly]
        public bool Interacting { get; private set; }
        public bool IsPickingUp { get; private set; }

        public bool ExternalInteractionLocked { get; set; }

        [SerializeField] private ThirdPersonPlayerController playerController;
        [SerializeField] private Rigidbody playerRb;
        public event Action OnInteractionStart;
        public event Action OnInteractionEnd;

        private readonly HashSet<Collider> candidates = new HashSet<Collider>();
        private Liftable currentCandidate;
        private Liftable pendingObject;
        private SpringJoint grabJoint;
        private bool pickupEntered;
        private bool grabEventReceived;
        private float pickupRequestedAt;
        private static readonly int PickupId = Animator.StringToHash("Pickup");
        private static readonly int PickupStateId = Animator.StringToHash("Base Layer.Pickup");

        public Interactable SelectedObject =>
            currentCandidate != null ? currentCandidate : HeldObject;

        private void Awake()
        {
            // Controller, movement, dan Rigidbody harus berada pada root yang sama.
            playerRb = GetComponent<Rigidbody>();
            if (!playerController) playerController = GetComponent<ThirdPersonPlayerController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            RefreshCandidate();
            if (IsPickingUp && !pickupEntered &&
                Time.time - pickupRequestedAt > pickupStartTimeout)
            {
                Debug.LogWarning("Pickup tidak mulai. Periksa transition, Trigger Pickup, " +
                    "PickupStateBehaviour dan PickupAnimationEvents.", this);
                FinishPickup();
            }

            if (!readMouseInput) return;
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                EndInteraction();
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame) BeginInteraction();
            if (mouse.leftButton.wasReleasedThisFrame) EndInteraction();
            if (!IsPickingUp && mouse.rightButton.wasPressedThisFrame &&
                HeldObject is HoseNozzle nozzle)
                nozzle.ToggleShooting();
        }

        private void FixedUpdate()
        {
            if (!HeldObject)
            {
                // Bersihkan joint jika objek dihapus saat sedang dipegang.
                if (grabJoint) DestroyGrabJoint();
                if (playerController) playerController.SetMovementState(0f, null);
                return;
            }

            // Dibaca ulang sehingga seluruh pemegang mengikuti perubahan jumlah holder.
            if (playerController)
                playerController.SetMovementState(HeldObject.SpeedPenalty,
                    HeldObject.ForceFaceObject ? HeldObject.transform : null);

            // Tangan dapat bergerak karena animasi. Anchor mengikuti posisi terbarunya.
            if (grabJoint && handTransform)
                grabJoint.anchor = transform.InverseTransformPoint(handTransform.position);
            RotateHeldObjectPhysics();
        }

        public void BeginInteraction()
        {
            if (!isActiveAndEnabled || Interacting || ExternalInteractionLocked) return;
            Interacting = true;
            if (!IsPickingUp)
            {
                if (HeldObject) DropObject(HeldObject, true);
                else
                {
                    RefreshCandidate();
                    if (currentCandidate) BeginPickup(currentCandidate);
                }
                OnInteractionStart?.Invoke();
            }
        }

        public void EndInteraction()
        {
            if (!Interacting) return;
            Interacting = false;
            OnInteractionEnd?.Invoke();
        }

        private void BeginPickup(Liftable obj)
        {
            if (!obj || !obj.CanBePickedUp() || obj.Holders.Contains(this)) return;
            if (!handTransform || !handTrigger || !playerController || !animator ||
                !animator.isActiveAndEnabled || !animator.runtimeAnimatorController)
            {
                Debug.LogWarning("Lengkapi Hand Transform, Hand Trigger, Player Controller " +
                    "dan Animator pada InteractionController.", this);
                return;
            }
            bool hasTrigger = false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == PickupId && parameter.type == AnimatorControllerParameterType.Trigger)
                    hasTrigger = true;
            if (!hasTrigger || !animator.HasState(0, PickupStateId) ||
                !animator.GetComponent<PickupAnimationEvents>())
            {
                Debug.LogWarning("Perlu Trigger Pickup, state Base Layer.Pickup, " +
                    "dan komponen PickupAnimationEvents pada Animator.", this);
                return;
            }
            // Jangan masuk ulang saat state sebelumnya masih dievaluasi.
            if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == PickupStateId ||
                (animator.IsInTransition(0) &&
                 animator.GetNextAnimatorStateInfo(0).fullPathHash == PickupStateId)) return;

            pendingObject = obj;
            grabEventReceived = false;
            pickupEntered = false;
            IsPickingUp = true;
            pickupRequestedAt = Time.time;
            playerController.SetMovementLocked(true);
            animator.ResetTrigger(PickupId);
            animator.SetTrigger(PickupId);
        }

        public void NotifyPickupEntered()
        {
            if (IsPickingUp) pickupEntered = true;
        }

        // Dipanggil melalui Animation Event, bukan langsung ketika klik.
        public void GrabPendingObject()
        {
            if (!IsPickingUp || !pickupEntered || grabEventReceived) return;
            grabEventReceived = true;
            Liftable obj = pendingObject;
            pendingObject = null;
            // Cek ulang: bisa saja sudah diambil pemain lain sebelum frame kontak.
            if (!obj || !obj.isActiveAndEnabled || HeldObject || !obj.Rigidbody ||
                !obj.CanBePickedUp() || obj.Holders.Contains(this)) return;
            Vector3 point = obj.GetGrabPoint(handTransform.position);
            if (Vector3.Distance(handTransform.position, point) > maxPickupReach) return;
            PickUpObject(obj);
        }

        public void NotifyPickupExited()
        {
            if (!IsPickingUp) return;
            if (!grabEventReceived)
                Debug.LogWarning("Pickup selesai tanpa event Pickup_Grab. " +
                    "Pastikan event ada sebelum transisi keluar.", this);
            FinishPickup();
        }

        private void FinishPickup()
        {
            IsPickingUp = false;
            pickupEntered = false;
            pendingObject = null;
            if (animator && animator.runtimeAnimatorController) animator.ResetTrigger(PickupId);
            if (playerController) playerController.SetMovementLocked(false);
        }

        private void OnTriggerEnter(Collider other) => TrackCandidate(other);
        private void OnTriggerStay(Collider other) => TrackCandidate(other);
        private void OnTriggerExit(Collider other) => candidates.Remove(other);

        private void TrackCandidate(Collider other)
        {
            if (other.GetComponentInParent<Liftable>()) candidates.Add(other);
        }

        private void RefreshCandidate()
        {
            currentCandidate = null;
            candidates.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
            if (HeldObject || !handTransform) return;
            float nearest = float.PositiveInfinity;
            foreach (Collider col in candidates)
            {
                Liftable obj = col.GetComponentInParent<Liftable>();
                if (!obj || !obj.isActiveAndEnabled || !obj.CanBePickedUp() ||
                    obj.Holders.Contains(this)) continue;
                float distance = (col.ClosestPoint(handTransform.position) - handTransform.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                currentCandidate = obj;
            }
        }

        private void PickUpObject(Liftable obj)
        {
            obj.PickUp(this, heldObjectLayer);
            // PickUp() milik Liftable tidak mengembalikan bool: cek daftar holder.
            if (!obj.Holders.Contains(this)) return;
            HeldObject = obj;
            currentCandidate = null;
            Vector3 anchor = obj.GetGrabPoint(handTransform.position);
            grabJoint = gameObject.AddComponent<SpringJoint>();
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.connectedBody = obj.Rigidbody;
            grabJoint.anchor = transform.InverseTransformPoint(handTransform.position);
            grabJoint.connectedAnchor = obj.Rigidbody.transform.InverseTransformPoint(anchor);
            grabJoint.spring = jointSpring;
            grabJoint.damper = jointDamper;
            grabJoint.enableCollision = false;
            grabJoint.maxDistance = 0f;
            grabJoint.minDistance = 0f;
            grabJoint.tolerance = 0.025f;
            playerController.SetMovementState(obj.SpeedPenalty,
                obj.ForceFaceObject ? obj.transform : null);
        }

        private void RotateHeldObjectPhysics()
        {
            if (!HeldObject || !HeldObject.Rigidbody || !handTransform) return;
            // Satu pengendali rotasi mencegah torque kedua pemain saling melawan.
            if (HeldObject.Holders.Count > 0 &&
                !ReferenceEquals(HeldObject.Holders[0], this)) return;
            Rigidbody objRb = HeldObject.Rigidbody;
            Quaternion target = handTransform.rotation * Quaternion.Euler(HeldObject.LiftDirectionOffset);
            Quaternion difference = target * Quaternion.Inverse(objRb.rotation);
            difference.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) <= 1f) return;
            Vector3 torque = axis * (angle * Mathf.Deg2Rad) * rotateForce - objRb.angularVelocity * 10f;
            objRb.AddTorque(torque, ForceMode.Acceleration);
        }

        private void DropObject(Liftable obj, bool throwObject = false)
        {
            if (!obj) return;
            DestroyGrabJoint();
            HeldObject = null;
            obj.Drop(this);
            // Jika masih dipegang partner, hanya lepas; jangan lempar objek bersama.
            if (throwObject && obj.Holders.Count == 0 && playerRb && obj.Rigidbody)
            {
#if UNITY_6000_0_OR_NEWER
                obj.Rigidbody.linearVelocity = playerRb.linearVelocity + transform.forward * throwForce;
#else
                obj.Rigidbody.velocity = playerRb.velocity + transform.forward * throwForce;
#endif
            }
            if (playerController) playerController.SetMovementState(0f, null);
        }

        private void DestroyGrabJoint()
        {
            if (!grabJoint) return;
            grabJoint.spring = 0f;
            grabJoint.damper = 0f;
            Destroy(grabJoint);
            grabJoint = null;
        }

        private void OnDisable()
        {
            FinishPickup();
            EndInteraction();
            if (HeldObject) DropObject(HeldObject);
            else DestroyGrabJoint();
            candidates.Clear();
            currentCandidate = null;
        }
    }
}
