using UnityEngine;
using HPlayer;
using TA.AdaptiveAnimation;

namespace HInteractions
{
    // Mode opt-in untuk benda satu pemain. Tanpa komponen ini: joint lama tetap dipakai.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Liftable), typeof(Rigidbody))]
    [DefaultExecutionOrder(150)]
    public sealed class StableCarryAttachment : MonoBehaviour
    {
        [Tooltip("Posisi pusat objek relatif terhadap CarryAnchor. Mulai dari nol.")]
        [SerializeField] private Vector3 localCarryOffset = Vector3.zero;
        public void ConfigureOffset(Vector3 offset) => localCarryOffset = offset;
        public bool DynamicOnRelease { get; set; }
        private Transform torsoBone, pelvisBone, headBone, headEnd;

        private void CacheAnatomy()
        {
            var slot = owner.GetComponent<CharacterModelSlot>();
            Transform root = slot && slot.generatedVisuals ? slot.generatedVisuals.transform : owner.transform;
            foreach (var bone in root.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "spine.003") torsoBone = bone;
                if (bone.name == "spine.001") pelvisBone = bone;
                if (bone.name == "spine.006") headBone = bone;
                if (bone.name == "spine.006_end") headEnd = bone;
            }
        }

        // Grip controller calls this after the authored carry pose and before rig evaluation.
        // Move the object, keeping both contact points on its surface, rather than clipping IK targets.
        public bool FitGripReach(Vector3 leftPoint, Vector3 rightPoint,
            Vector3 leftShoulder, Vector3 rightShoulder, float leftReach, float rightReach)
        {
            if (!activeCarry || !owner || owner.IsPlacingDown) return false;
            Vector3 correction = Vector3.zero;
            for (int iteration = 0; iteration < 48; iteration++)
            {
                Vector3 left = leftPoint + correction - leftShoulder;
                if (left.magnitude > leftReach)
                    correction -= left.normalized * (left.magnitude - leftReach);
                Vector3 right = rightPoint + correction - rightShoulder;
                if (right.magnitude > rightReach)
                    correction -= right.normalized * (right.magnitude - rightReach);
                correction += ClearanceCorrection(correction);
            }
            // Reject impossible widths and keep correction local to the authored carry position.
            if (correction.magnitude > 1f ||
                Vector3.Distance(leftPoint + correction, leftShoulder) > leftReach + .001f ||
                Vector3.Distance(rightPoint + correction, rightShoulder) > rightReach + .001f)
                return false;
            float fraction = LimitObjectPose(transform.position + correction, transform.rotation);
            transform.position += correction * fraction;
            Physics.SyncTransforms();
            return fraction > .999f;
        }

        // Reset juga saat Enter Play Mode memakai Domain Reload OFF.
        private static bool applicationQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState() => applicationQuitting = false;

        private static bool SessionIsStopping
        {
            get
            {
                if (applicationQuitting || !Application.isPlaying) return true;
#if UNITY_EDITOR
                // Application.isPlaying masih dapat true ketika proses keluar dimulai.
                if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return true;
#endif
                return false;
            }
        }

        private void OnApplicationQuit() => applicationQuitting = true;

        private Rigidbody body;
        private InteractionController owner;
        private Transform anchor;
        private Transform originalParent;
        private Quaternion carryRotation;
        private bool captured;
        private bool activeCarry;
        private CarryMotionGuard motionGuard;
        private Rigidbody ownerBody;
        private bool savedKinematic;
        private bool savedGravity;
        private float savedDrag;
        private float savedAngularDrag;
        private RigidbodyInterpolation savedInterpolation;
        private CollisionDetectionMode savedCollisionMode;

        public bool CanBegin(InteractionController holder, Liftable item,
            Transform carryAnchor, out string reason)
        {
            reason = null;
            if (activeCarry || !item.SupportsStableSingleCarry || item.Holders.Count != 0)
                reason = "Mode ini hanya untuk objek Max Holders = 1 yang belum dipegang.";
            else if (!carryAnchor || carryAnchor.parent != holder.transform)
                reason = "CarryAnchor wajib diisi dan menjadi anak langsung root pemain.";
            else if (carryAnchor.IsChildOf(transform))
                reason = "CarryAnchor tidak boleh berada di dalam objek yang diangkat.";
            else if (!UniformPositiveAncestors(carryAnchor) || !UniformPositiveAncestors(transform.parent))
                reason = "Induk objek dan rantai induk CarryAnchor harus memiliki skala seragam positif. Skala kardus sendiri boleh berbeda X/Y/Z.";
            else if (GetComponentsInChildren<Rigidbody>(true).Length != 1 || GetComponents<Joint>().Length != 0)
                reason = "Gunakan pada kardus dengan satu Rigidbody tanpa Joint tambahan.";
            return reason == null;
        }

        private static bool UniformPositiveAncestors(Transform current)
        {
            for (; current; current = current.parent)
            {
                Vector3 s = current.localScale;
                if (s.x <= 0f || Mathf.Abs(s.x - s.y) > 0.0001f || Mathf.Abs(s.x - s.z) > 0.0001f)
                    return false;
            }
            return true;
        }

        // Dipanggil sebelum Liftable.PickUp mengubah drag, gravity dan interpolation.
        public void CaptureState()
        {
            body = GetComponent<Rigidbody>();
            originalParent = transform.parent;
            savedKinematic = body.isKinematic;
            savedGravity = body.useGravity;
            savedDrag = body.drag;
            savedAngularDrag = body.angularDrag;
            savedInterpolation = body.interpolation;
            savedCollisionMode = body.collisionDetectionMode;
            captured = true;
        }

        public void BeginCarry(InteractionController holder, Transform carryAnchor, Vector3 rotationOffset)
        {
            owner = holder;
            CacheAnatomy();
            anchor = carryAnchor;
            carryRotation = Quaternion.Euler(rotationOffset);
            if (!body.isKinematic)
            {
                SetVelocity(Vector3.zero);
                body.angularVelocity = Vector3.zero;
            }
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.useGravity = false;
            // Posisi visual mengikuti root pemain yang sudah di-interpolate.
            // Jangan interpolate lagi atau mengejar target melalui MovePosition/Lerp.
            body.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(anchor, true);
            activeCarry = true;
            ownerBody = holder.GetComponent<Rigidbody>();
            motionGuard = new CarryMotionGuard(body, ownerBody);
            ApplyPose();
        }

        // The player and held object share the same permitted movement. Clamping
        // only the object would let the player walk away from its hand contacts.
        public void ConstrainMovement(ref Vector3 delta, ref Quaternion rotation)
        {
            if (!activeCarry || !ownerBody || owner.IsPlacingDown) return;
            Quaternion turn = rotation * Quaternion.Inverse(ownerBody.rotation);
            Vector3 objectPosition = ownerBody.position + delta + turn * (body.position - ownerBody.position);
            float fraction = motionGuard.LimitFollowingPivot(new[] { objectPosition, ownerBody.position + delta },
                new[] { turn * body.rotation, rotation }, 0, 1);
            if (fraction < .999f && delta.sqrMagnitude > .0000001f)
            {
                // A blocked yaw must not trap the holder against a wall. Permit
                // a safe retreat/slide with the current orientation first.
                float straight = motionGuard.Limit(new[] { body.position + delta, ownerBody.position + delta },
                    new[] { body.rotation, ownerBody.rotation });
                if (straight > fraction + .001f)
                {
                    delta *= straight;
                    rotation = ownerBody.rotation;
                    return;
                }
            }
            delta *= fraction;
            rotation = Quaternion.Slerp(ownerBody.rotation, rotation, fraction);
        }

        private float LimitObjectPose(Vector3 position, Quaternion rotation)
        {
            return motionGuard == null || !ownerBody ? 1f : motionGuard.Limit(
                new[] { position, ownerBody.position }, new[] { rotation, ownerBody.rotation });
        }

        private void Update()
        {
            if (!activeCarry) return;
            if (!owner || !anchor || owner.HeldObject != GetComponent<Liftable>())
            {
                EndCarry(Vector3.zero);
                return;
            }
            ApplyPose(); // Sebelum HeldObjectGripController (execution order 200).
        }

        private void ApplyPose()
        {
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            if (owner && owner.IsPlacingDown)
            {
                // The interaction's first holder owns the guarded placement pose.
                return;
            }
            transform.localPosition = localCarryOffset;
            transform.localRotation = carryRotation;
            if (TryGetComponent<BoxCollider>(out var box) && box.size.y * transform.lossyScale.y > 1f)
            {
                var grips = GetComponent<BoxGripProvider>();
                Vector3 beforeA = Vector3.zero, beforeB = Vector3.zero;
                bool hasGrip = grips && grips.TryGetGripPoints(out beforeA, out beforeB);
                Vector3 pivot = hasGrip ? (beforeA + beforeB) * .5f : transform.position;
                transform.rotation = Quaternion.AngleAxis(35f, owner.transform.right) * transform.rotation;
                if (hasGrip && grips.TryGetGripPoints(out var afterA, out var afterB))
                    transform.position += pivot - (afterA + afterB) * .5f;
                if (hasGrip && grips.TryGetGripPoints(out afterA, out afterB))
                    transform.position += owner.transform.right * Vector3.Dot(
                        owner.transform.position - (afterA + afterB) * .5f, owner.transform.right);
            }
            transform.position += ClearanceCorrection(Vector3.zero);
            Vector3 desiredPosition = transform.position;
            Quaternion desiredRotation = transform.rotation;
            transform.SetPositionAndRotation(startPosition, startRotation);
            Physics.SyncTransforms();
            float fraction = LimitObjectPose(desiredPosition, desiredRotation);
            transform.SetPositionAndRotation(Vector3.Lerp(startPosition, desiredPosition, fraction),
                Quaternion.Slerp(startRotation, desiredRotation, fraction));
            Physics.SyncTransforms();
        }

        private Vector3 ClearanceCorrection(Vector3 translation)
        {
            if (!owner || !TryGetComponent<BoxGripProvider>(out var provider)) return Vector3.zero;
            float scale = owner.transform.lossyScale.y;
            Vector3 correction = Vector3.zero;
            if (torsoBone) correction += provider.GetSurfaceClearance(torsoBone.position, .08f * scale, translation + correction);
            if (pelvisBone) correction += provider.GetSurfaceClearance(pelvisBone.position, .14f * scale, translation + correction);
            if (headBone && headEnd) correction += provider.GetSurfaceClearance(
                Vector3.Lerp(headBone.position, headEnd.position, .4f), .35f * scale, translation + correction);
            // Carry correction is applied to the object in front of the character.
            // A nearest triangle on the far side must not pull furniture through the torso.
            if (TryGetComponent<BoxCollider>(out var bodyShape) && bodyShape.size.y * transform.lossyScale.y > 1f)
                correction = owner.transform.forward * correction.magnitude;
            else
            {
                float backward = Vector3.Dot(correction, owner.transform.forward);
                if (backward < 0f) correction -= owner.transform.forward * backward;
                correction -= owner.transform.right * Vector3.Dot(correction, owner.transform.right);
            }
            return correction;
        }

        public void EndCarry(Vector3 releaseVelocity)
        {
            if (!activeCarry || !captured) return;
            // Saat sesi berakhir, Unity memulihkan/membongkar scene sendiri.
            // Jangan SetParent ketika hierarki sedang dinonaktifkan.
            if (SessionIsStopping) return;
            activeCarry = false;
            motionGuard = null; ownerBody = null;
            owner = null;
            anchor = null;
            transform.SetParent(originalParent ? originalParent : null, true);
            if (body)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
                body.isKinematic = DynamicOnRelease ? false : savedKinematic;
                body.useGravity = DynamicOnRelease || savedGravity;
                body.drag = savedDrag;
                body.angularDrag = savedAngularDrag;
                body.interpolation = savedInterpolation;
                body.collisionDetectionMode = savedCollisionMode;
                if (!body.isKinematic)
                {
                    SetVelocity(releaseVelocity);
                    body.angularVelocity = Vector3.zero;
                    body.WakeUp();
                }
            }
            captured = false;
            Physics.SyncTransforms();
        }

        private void SetVelocity(Vector3 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = velocity;
#else
            body.velocity = velocity;
#endif
        }

        private void OnDisable()
        {
            if (!activeCarry || SessionIsStopping) return;
            if (owner) owner.ReleaseHeldObject();
            if (activeCarry) EndCarry(Vector3.zero);
        }
    }
}
