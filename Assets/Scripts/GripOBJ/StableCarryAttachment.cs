using UnityEngine;
using HPlayer;

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
            ApplyPose();
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
            transform.localPosition = localCarryOffset;
            transform.localRotation = carryRotation;
            Physics.SyncTransforms();
        }

        public void EndCarry(Vector3 releaseVelocity)
        {
            if (!activeCarry || !captured) return;
            // Saat sesi berakhir, Unity memulihkan/membongkar scene sendiri.
            // Jangan SetParent ketika hierarki sedang dinonaktifkan.
            if (SessionIsStopping) return;
            activeCarry = false;
            owner = null;
            anchor = null;
            transform.SetParent(originalParent ? originalParent : null, true);
            if (body)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
                body.isKinematic = savedKinematic;
                body.useGravity = savedGravity;
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
