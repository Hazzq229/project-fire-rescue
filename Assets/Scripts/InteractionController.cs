using System;
using System.Collections.Generic;
using HInteractions;
using HGame.Objects;
using NaughtyAttributes;
using UnityEngine;

namespace HPlayer
{
    [RequireComponent(typeof(Rigidbody), typeof(LocalPlayerInput))]
    public class InteractionController : MonoBehaviour, IObjectHolder
    {
        [Header("Hold Settings")]
        [SerializeField, Required] private Transform handTransform;
        [SerializeField, Required] private Collider handTrigger;
        [SerializeField] private int heldObjectLayer;

        [Header("Independent Physics Anchor")]
        [Tooltip("Titik membawa pada root player, bukan tulang tangan IK. Jika kosong, memakai Hand Transform lama.")]
        [SerializeField] private Transform carryAnchor;
        private Transform PhysicsAnchor => carryAnchor ? carryAnchor : handTransform;

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

        private LocalPlayerInput localInput;

        [field: SerializeField, ReadOnly]
        public Liftable HeldObject { get; private set; }
        [field: SerializeField, ReadOnly]
        public bool Interacting { get; private set; }
        public bool IsPickingUp { get; private set; }
        public bool IsPlacingDown { get; private set; }
        public Liftable PendingPickupObject => IsPickingUp && !IsPlacingDown ? pendingObject : null;
        private Vector3 placementStart, placementTarget;
        private float placementContactTime = .763f;
        private bool placementWasKinematic;
        private TA.AdaptiveAnimation.CarryMotionGuard placementGuard;
        private Rigidbody[] placementBodies;
        private Quaternion placementRotation;
        private bool placementCancelled;
        public Vector3 PlacementPosition { get; private set; }
        public bool ExternalInteractionLocked { get; set; }

        [SerializeField] private ThirdPersonPlayerController playerController;
        [SerializeField] private Rigidbody playerRb;
        public event Action OnInteractionStart;
        public event Action OnInteractionEnd;

        private readonly HashSet<Collider> candidates = new HashSet<Collider>();
        private Liftable currentCandidate;
        private Liftable pendingObject;
        private SpringJoint grabJoint;
        private StableCarryAttachment stableCarry;
        private bool pickupEntered;
        private bool grabEventReceived;
        private float pickupRequestedAt;
        private static readonly int PickupId = Animator.StringToHash("Pickup");
        private static readonly int PickupStateId = Animator.StringToHash("Base Layer.Pickup");

        public Interactable SelectedObject =>
            currentCandidate != null ? currentCandidate : HeldObject;
        public TA.AdaptiveAnimation.CarryRole CurrentCarryRole =>
            HeldObject is TA.AdaptiveAnimation.CooperativeHoseGrip hoseGrip ? hoseGrip.Role :
            HeldObject is TA.AdaptiveAnimation.CooperativeCarryObject coop
                ? coop.GetRole(this) : TA.AdaptiveAnimation.CarryRole.None;

        private void Awake()
        {
            // Controller, movement, dan Rigidbody harus berada pada root yang sama.
            playerRb = GetComponent<Rigidbody>();
            localInput = GetComponent<LocalPlayerInput>();
            if (!playerController) playerController = GetComponent<ThirdPersonPlayerController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            RefreshCandidate();
            UpdatePlacement();
            if (IsPickingUp && !pickupEntered &&
                Time.time - pickupRequestedAt > pickupStartTimeout)
            {
                Debug.LogWarning("Pickup tidak mulai. Periksa transition, Trigger Pickup, " +
                    "PickupStateBehaviour dan PickupAnimationEvents.", this);
                FinishPickup();
            }

            if (!localInput || !localInput.IsReady)
            {
                EndInteraction();
                return;
            }
            if (localInput.GrabPressed) BeginInteraction();
            if (localInput.ThrowPressed) ThrowHeldObject();
            if (!localInput.GrabHeld) EndInteraction();
            if (localInput.ToolPressed && !(HeldObject is TA.AdaptiveAnimation.CooperativeHoseGrip))
            {
                if (IsPickingUp || ExternalInteractionLocked)
                {
                    Debug.Log(
                        "[Hose Input] Tunggu pickup selesai atau interaksi lain berakhir.",
                        this);
                }
                else if (HeldObject == null)
                {
                    Debug.Log("[Hose Input] Player belum memegang objek.", this);
                }
                else if (HeldObject.TryGetComponent<
                    HGame.Objects.HoseWaterController>(out var waterController))
                {
                    waterController.ToggleShooting();
                }
                else
                {
                    Debug.LogWarning(
                        $"[Hose Input] Objek {HeldObject.name} tidak memiliki " +
                        "HoseWaterController pada GameObject yang sama.",
                        HeldObject);
                }
            }
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

            if (IsPlacingDown) return;

            // Dibaca ulang sehingga seluruh pemegang mengikuti perubahan jumlah holder.
            if (playerController)
                playerController.SetMovementState(HeldObject.SpeedPenalty,
                    HeldObject.ForceFaceObject ? HeldObject.transform : null);

            if (stableCarry)
            {
                // Tidak mengarahkan badan ke objek yang mengikuti badan itu sendiri.
                if (playerController) playerController.SetMovementState(HeldObject.SpeedPenalty, null);
                return;
            }

            // Titik fisika terpisah dari tangan yang diarahkan oleh IK.
            if (grabJoint && PhysicsAnchor)
                grabJoint.anchor = transform.InverseTransformPoint(PhysicsAnchor.position);
            RotateHeldObjectPhysics();
        }

        public void BeginInteraction()
        {
            if (!isActiveAndEnabled || Interacting || ExternalInteractionLocked) return;
            Interacting = true;
            if (!IsPickingUp)
            {
                if (HeldObject) BeginPlaceDown();
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
            if (obj is TA.AdaptiveAnimation.CooperativeCarryObject coop && !coop.CanJoin(this)) return;
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
            if (IsPlacingDown)
            {
                CompletePlacement();
                return;
            }
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
            var retainedPlacement = IsPlacingDown ? HeldObject : null;
            // A refused/interrupted placement retains ownership. Never force an
            // obstructed object into its final pose or drop it from mid-air.
            if (IsPlacingDown && HeldObject && !(HeldObject is TA.AdaptiveAnimation.CooperativeCarryObject))
            {
                HeldObject.Rigidbody.isKinematic = placementWasKinematic;
                if (grabJoint) { grabJoint.spring = jointSpring; grabJoint.damper = jointDamper; }
            }
            IsPlacingDown = false;
            IsPickingUp = false;
            pickupEntered = false;
            pendingObject = null;
            placementGuard = null; placementBodies = null;
            if (animator && animator.runtimeAnimatorController) animator.ResetTrigger(PickupId);
            if (playerController) playerController.SetMovementLocked(false);
            if (retainedPlacement is TA.AdaptiveAnimation.CooperativeCarryObject team)
                team.ResumeAfterPlacement();
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
            // Recheck ownership before capturing attachment state or creating any joint.
            if (!obj || HeldObject || !obj.isActiveAndEnabled || !obj.CanBePickedUp() ||
                (obj.SupportsStableSingleCarry && obj.Holders.Count != 0)) return;
            StableCarryAttachment attachment = obj.GetComponent<StableCarryAttachment>();
            if (attachment && attachment.isActiveAndEnabled)
            {
                if (!attachment.CanBegin(this, obj, carryAnchor, out string reason))
                {
                    Debug.LogWarning("Stable carry: " + reason, obj);
                    return;
                }
                attachment.CaptureState();
            }
            else attachment = null;

            obj.PickUp(this, heldObjectLayer);
            // PickUp() milik Liftable tidak mengembalikan bool: cek daftar holder.
            if (!obj.Holders.Contains(this)) return;
            HeldObject = obj;
            currentCandidate = null;
            if (obj is TA.AdaptiveAnimation.CooperativeCarryObject || obj is TA.AdaptiveAnimation.CooperativeHoseGrip) return;
            if (attachment)
            {
                stableCarry = attachment;
                attachment.BeginCarry(this, carryAnchor, obj.LiftDirectionOffset);
                playerController.SetMovementState(obj.SpeedPenalty, null);
                return;
            }
            Vector3 anchor = obj.GetGrabPoint(handTransform.position);
            grabJoint = gameObject.AddComponent<SpringJoint>();
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.connectedBody = obj.Rigidbody;
            grabJoint.anchor = transform.InverseTransformPoint(PhysicsAnchor.position);
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
            if (HeldObject is TA.AdaptiveAnimation.CooperativeCarryObject || HeldObject is TA.AdaptiveAnimation.CooperativeHoseGrip) return;
            if (!HeldObject || !HeldObject.Rigidbody || !PhysicsAnchor) return;
            // Satu pengendali rotasi mencegah torque kedua pemain saling melawan.
            if (HeldObject.Holders.Count > 0 &&
                !ReferenceEquals(HeldObject.Holders[0], this)) return;
            Rigidbody objRb = HeldObject.Rigidbody;
            Quaternion target = PhysicsAnchor.rotation * Quaternion.Euler(HeldObject.LiftDirectionOffset);
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
            StableCarryAttachment attachment = stableCarry;
            stableCarry = null;
            Vector3 releaseVelocity = playerRb ? playerRb.GetPointVelocity(obj.transform.position) : Vector3.zero;
            HeldObject = null;
            // Callback Liftable selesai dahulu, lalu pulihkan keadaan sebelum pickup.
            obj.Drop(this);
            if (attachment) attachment.EndCarry(releaseVelocity);
            else if (IsPlacingDown && !(obj is TA.AdaptiveAnimation.CooperativeCarryObject))
                obj.Rigidbody.isKinematic = placementWasKinematic;
            // Jika masih dipegang partner, hanya lepas; jangan lempar objek bersama.
            if (throwObject && !(obj is TA.AdaptiveAnimation.CooperativeCarryObject) &&
                obj.Holders.Count == 0 && playerRb && obj.Rigidbody && !obj.Rigidbody.isKinematic)
            {
#if UNITY_6000_0_OR_NEWER
                obj.Rigidbody.linearVelocity = playerRb.linearVelocity + transform.forward * throwForce;
#else
                obj.Rigidbody.velocity = playerRb.velocity + transform.forward * throwForce;
#endif
            }
            if (playerController) playerController.SetMovementState(0f, null);
        }

        // Dipakai komponen attachment jika dinonaktifkan ketika sedang dibawa.
        public void ReleaseHeldObject()
        {
            if (HeldObject) DropObject(HeldObject);
        }

        public void ThrowHeldObject()
        {
            if (!isActiveAndEnabled || IsPickingUp || ExternalInteractionLocked || !HeldObject) return;
            if (HeldObject is TA.AdaptiveAnimation.CooperativeHoseGrip) { ReleaseHeldObject(); return; }
            if (HeldObject is TA.AdaptiveAnimation.CooperativeCarryObject) BeginPlaceDown();
            else DropObject(HeldObject, true);
        }

        public void BeginPlaceDown()
        {
            if (HeldObject is TA.AdaptiveAnimation.CooperativeHoseGrip && !IsPickingUp)
            { ReleaseHeldObject(); return; }
            if (!HeldObject || IsPickingUp || ExternalInteractionLocked || !animator ||
                !animator.HasState(0, PickupStateId)) return;
            var holders = HeldObject.Holders.ToArray();
            foreach (var holder in holders)
                if (holder is InteractionController partner && (partner.IsPickingUp || partner.ExternalInteractionLocked)) return;
            var owner = holders.Length > 0 ? holders[0] as InteractionController : null;
            if (!owner || !owner.TryPlanPlacement(HeldObject, out Vector3 target)) return;
            Quaternion rotation = HeldObject.Rigidbody.rotation;
            foreach (var holder in holders)
                if (holder is InteractionController partner) partner.BeginPlaceLocal(target, rotation);
        }

        private void BeginPlaceLocal(Vector3 target, Quaternion rotation)
        {
            IsPlacingDown = IsPickingUp = true;
            pickupEntered = grabEventReceived = false;
            pickupRequestedAt = Time.time;
            placementStart = PlacementPosition = HeldObject.Rigidbody.position;
            placementTarget = target;
            placementRotation = rotation;
            placementCancelled = false;
            placementWasKinematic = HeldObject.Rigidbody.isKinematic;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                foreach (var evt in clip.events)
                    if (evt.functionName == "Pickup_Grab") placementContactTime = evt.time / clip.length;
            if (grabJoint) { grabJoint.spring = 0f; grabJoint.damper = 0f; }
            if (!(HeldObject is TA.AdaptiveAnimation.CooperativeCarryObject))
                HeldObject.Rigidbody.isKinematic = true;
            playerController.SetMovementLocked(true);
            animator.ResetTrigger(PickupId);
            animator.SetTrigger(PickupId);
        }

        private bool TryPlanPlacement(Liftable item, out Vector3 target)
        {
            target = item.Rigidbody.position;
            var bounds = new Bounds(); bool foundShape = false;
            foreach (var col in item.GetComponentsInChildren<Collider>())
                if (col.enabled && !col.isTrigger && col.attachedRigidbody == item.Rigidbody)
                {
                    if (!foundShape) { bounds = col.bounds; foundShape = true; }
                    else bounds.Encapsulate(col.bounds);
                }
            if (!foundShape) return false;
            var bodies = new List<Rigidbody> { item.Rigidbody };
            foreach (var holder in item.Holders)
                if (holder is InteractionController actor && actor.playerRb) bodies.Add(actor.playerRb);
            placementBodies = bodies.ToArray();
            placementGuard = new TA.AdaptiveAnimation.CarryMotionGuard(placementBodies);
            placementRotation = item.Rigidbody.rotation;
            Vector3 offset = Vector3.zero;
            if (item.SupportsStableSingleCarry)
            {
                var capsule = GetComponent<CapsuleCollider>();
                Vector3 forward = transform.forward;
                float extent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
                float clearance = (capsule ? capsule.radius * transform.lossyScale.x : .3f) + extent + .04f;
                float distance = Vector3.Dot(bounds.center - transform.position, forward);
                offset = forward * Mathf.Max(0f, clearance - distance);
            }
            var offsets = new List<Vector3> { offset, Vector3.zero };
            foreach (float distance in new[] { .15f, .3f, .45f, .6f })
            {
                offsets.Add(offset + transform.right * distance);
                offsets.Add(offset - transform.right * distance);
            }
            foreach (var candidate in offsets)
            {
                if (!SupportedPlacement(item, bounds, candidate, out Vector3 point)) continue;
                if (PlacementFraction(point) < .9999f) continue;
                target = point;
                return true;
            }
            placementGuard = null; placementBodies = null;
            return false;
        }

        private bool SupportedPlacement(Liftable item, Bounds bounds, Vector3 offset, out Vector3 point)
        {
            point = item.Rigidbody.position + offset;
            float support = float.NegativeInfinity;
            // The footprint must have support, not only a center ray over an edge.
            foreach (var sample in new[] { Vector2.zero, new Vector2(-.9f, -.9f), new Vector2(-.9f, .9f),
                new Vector2(.9f, -.9f), new Vector2(.9f, .9f) })
            {
                var origin = new Vector3(bounds.center.x + offset.x + sample.x * bounds.extents.x,
                    bounds.max.y + .15f, bounds.center.z + offset.z + sample.y * bounds.extents.z);
                float highest = float.NegativeInfinity;
                foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(item.transform) || hit.collider.GetComponentInParent<InteractionController>() ||
                        hit.normal.y < .7f || hit.point.y > bounds.min.y + .05f) continue;
                    highest = Mathf.Max(highest, hit.point.y);
                }
                if (float.IsNegativeInfinity(highest)) return false;
                support = Mathf.Max(support, highest);
            }
            point.y = support + (item.Rigidbody.position.y - bounds.min.y) + .01f;
            return true;
        }

        private float PlacementFraction(Vector3 position)
        {
            if (placementGuard == null || placementBodies == null) return 0f;
            var positions = new Vector3[placementBodies.Length]; var rotations = new Quaternion[placementBodies.Length];
            for (int i = 0; i < placementBodies.Length; i++)
            { positions[i] = placementBodies[i].position; rotations[i] = placementBodies[i].rotation; }
            positions[0] = position; rotations[0] = placementRotation;
            return placementGuard.Limit(positions, rotations);
        }

        private void UpdatePlacement()
        {
            if (!IsPlacingDown || !HeldObject || !pickupEntered || grabEventReceived) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != PickupStateId) return;
            float t = Mathf.Clamp01(state.normalizedTime / Mathf.Max(.01f, placementContactTime));
            PlacementPosition = Vector3.Lerp(placementStart, placementTarget, Mathf.SmoothStep(0f, 1f, t));
            ApplyPlacementPosition();
        }

        private void ApplyPlacementPosition()
        {
            if (!HeldObject || HeldObject.Holders.Count == 0 || !ReferenceEquals(HeldObject.Holders[0], this)) return;
            float fraction = PlacementFraction(PlacementPosition);
            PlacementPosition = Vector3.Lerp(HeldObject.Rigidbody.position, PlacementPosition, fraction);
            HeldObject.transform.SetPositionAndRotation(PlacementPosition, placementRotation);
            HeldObject.Rigidbody.position = PlacementPosition;
            HeldObject.Rigidbody.rotation = placementRotation;
            Physics.SyncTransforms();
        }

        private void CompletePlacement()
        {
            if (!HeldObject || placementCancelled) return;
            var item = HeldObject;
            // Either animation may deliver its contact event first; one owner commits the shared pose.
            if (item.Holders.Count > 0 && item.Holders[0] is InteractionController first && first != this)
            {
                first.CompletePlacement();
                return;
            }
            PlacementPosition = placementTarget;
            ApplyPlacementPosition();
            if (Vector3.Distance(item.Rigidbody.position, placementTarget) > .005f)
            {
                foreach (var holder in item.Holders)
                    if (holder is InteractionController partner) partner.placementCancelled = true;
                return;
            }
            DropObject(item);
            if (!item.Rigidbody.isKinematic)
            {
                item.Rigidbody.velocity = Vector3.zero;
                item.Rigidbody.angularVelocity = Vector3.zero;
            }
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
