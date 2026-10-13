using HInteractions;
using HPlayer;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace TA.AdaptiveAnimation
{
    /// <summary>
    /// Menghubungkan HeldObject ke pasangan target IK yang sudah terpasang.
    /// Untuk Animator Update Mode Normal. Tidak menggerakkan objek/fisika.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class HeldObjectGripController : MonoBehaviour
    {
        [Header("Referensi - wajib diisi")]
        [SerializeField] private InteractionController interactionController;
        [SerializeField] private Rig carryRig;
        [SerializeField] private TwoBoneIKConstraint leftArmIK;
        [SerializeField] private TwoBoneIKConstraint rightArmIK;

        [Header("Pembagian tangan")]
        [Tooltip("Samakan dengan Swap Hands pada preview yang sudah berhasil.")]
        [SerializeField] private bool swapHands;

        [Header("Durasi perubahan weight (detik)")]
        [SerializeField, Min(0.01f)] private float blendInDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float blendOutDuration = 0.2f;

        [Header("Batas maksimum jangkauan lengan")]
        [Tooltip("0.98 = maksimal 98% panjang lengan. Jangan naikkan di atas 1.")]
        [SerializeField, Range(0.5f, 1f)] private float maximumReachRatio = 0.98f;
        [Tooltip("Jarak tambahan sebelum aktif kembali agar tidak berkedip di batas.")]
        [SerializeField, Range(0f, 0.15f)] private float reentryMargin = 0.03f;

        [Header("Pantauan saat Play - jangan diedit")]
        [SerializeField] private string status = "Belum berjalan";
        [SerializeField] private string heldObjectName = "Tidak ada";
        [SerializeField] private float currentWeight;
        [SerializeField] private float leftDistance;
        [SerializeField] private float rightDistance;
        [SerializeField] private float leftMaximumReach;
        [SerializeField] private float rightMaximumReach;

        private Liftable trackedObject;
        private GripPointProvider provider;
        private bool hasSavedTargets;
        private bool acceptedReach;
        private bool warnedSetup;
        private Vector3 savedLeftLocal;
        private Vector3 savedRightLocal;

        private void OnEnable()
        {
            trackedObject = null;
            provider = null;
            hasSavedTargets = false;
            acceptedReach = false;
            warnedSetup = false;
            currentWeight = 0f;
            if (carryRig) carryRig.weight = 0f;
        }

        private void OnDisable()
        {
            currentWeight = 0f;
            if (carryRig) carryRig.weight = 0f;
            status = "Pengendali nonaktif";
        }

        private void Update()
        {
            if (!ValidateSetup()) return;
            var left = leftArmIK.data;
            var right = rightArmIK.data;
            Liftable held = interactionController.isActiveAndEnabled
                ? interactionController.HeldObject : null;
            heldObjectName = held ? held.name : "Tidak ada";

            // Saat objek berubah, selesaikan pelepasan pose lama dahulu.
            // Ini mencegah target meloncat dari kardus lama ke kardus baru.
            if (held != trackedObject)
            {
                if (currentWeight > 0f)
                {
                    FadeOut(held ? "Beralih objek" : "Melepas pegangan", left, right);
                    return;
                }
                trackedObject = held;
                provider = held ? held.GetComponent<GripPointProvider>() : null;
                hasSavedTargets = false;
                acceptedReach = false;
            }

            if (!held || !held.isActiveAndEnabled)
            {
                FadeOut("Tidak memegang objek", left, right);
                return;
            }
            // Mendukung provider yang diaktifkan/dipasang kembali saat uji.
            if (!provider) provider = held.GetComponent<GripPointProvider>();
            if (!provider || !provider.isActiveAndEnabled)
            {
                FadeOut("Objek tanpa provider aktif", left, right);
                return;
            }
            if (!provider.TryGetGripPoints(out Vector3 a, out Vector3 b) ||
                !IsFinite(a) || !IsFinite(b))
            {
                FadeOut("Titik pegangan tidak valid", left, right);
                return;
            }

            Vector3 leftPoint = swapHands ? b : a;
            Vector3 rightPoint = swapHands ? a : b;
            float leftLength = ArmLength(left);
            float rightLength = ArmLength(right);
            float ratio = Mathf.Clamp(maximumReachRatio, 0.5f, 1f);
            leftMaximumReach = leftLength * ratio;
            rightMaximumReach = rightLength * ratio;
            leftDistance = Vector3.Distance(left.root.position, leftPoint);
            rightDistance = Vector3.Distance(right.root.position, rightPoint);

            // Setelah keluar jangkauan, perlu masuk sedikit lebih dekat untuk aktif.
            float activeRatio = acceptedReach ? ratio :
                Mathf.Max(0.1f, ratio - Mathf.Clamp(reentryMargin, 0f, 0.15f));
            if (leftLength < 0.0001f || rightLength < 0.0001f ||
                leftDistance > leftLength * activeRatio ||
                rightDistance > rightLength * activeRatio)
            {
                FadeOut("Pegangan di luar jangkauan", left, right);
                return;
            }

            acceptedReach = true;
            left.target.position = leftPoint;
            right.target.position = rightPoint;
            Transform playerSpace = interactionController.transform;
            savedLeftLocal = playerSpace.InverseTransformPoint(leftPoint);
            savedRightLocal = playerSpace.InverseTransformPoint(rightPoint);
            hasSavedTargets = true;
            currentWeight = Mathf.MoveTowards(currentWeight, 1f,
                Time.deltaTime / Mathf.Max(0.01f, blendInDuration));
            carryRig.weight = currentWeight;
            status = "Mengikuti objek dipegang";
        }

        private void FadeOut(string reason, TwoBoneIKConstraintData left,
            TwoBoneIKConstraintData right)
        {
            acceptedReach = false;
            // Posisi terakhir mengikuti ruang karakter, bukan objek yang dilempar.
            if (hasSavedTargets)
            {
                Transform playerSpace = interactionController.transform;
                left.target.position = playerSpace.TransformPoint(savedLeftLocal);
                right.target.position = playerSpace.TransformPoint(savedRightLocal);
            }
            currentWeight = Mathf.MoveTowards(currentWeight, 0f,
                Time.deltaTime / Mathf.Max(0.01f, blendOutDuration));
            carryRig.weight = currentWeight;
            status = reason;
        }

        private bool ValidateSetup()
        {
            string error = null;
            if (!interactionController || !carryRig || !leftArmIK || !rightArmIK)
                error = "Isi empat referensi wajib.";
            else if (leftArmIK == rightArmIK)
                error = "Left Arm IK dan Right Arm IK harus berbeda.";
            else
            {
                var left = leftArmIK.data;
                var right = rightArmIK.data;
                if (!ValidArm(left) || !ValidArm(right))
                    error = "Lengkapi rantai Root-Mid-Tip dan Target kedua IK.";
                else if (left.root == right.root || left.mid == right.mid ||
                    left.tip == right.tip || left.target == right.target)
                    error = "Kedua lengan tidak boleh memakai tulang/target yang sama.";
                else if (!leftArmIK.transform.IsChildOf(carryRig.transform) ||
                    !rightArmIK.transform.IsChildOf(carryRig.transform) ||
                    !left.target.IsChildOf(carryRig.transform) ||
                    !right.target.IsChildOf(carryRig.transform))
                    error = "Kedua IK dan Target harus berada di bawah Carry_Rig.";
                else if (left.target.IsChildOf(left.root) ||
                    left.target.IsChildOf(right.root) ||
                    right.target.IsChildOf(left.root) ||
                    right.target.IsChildOf(right.root) ||
                    left.target.IsChildOf(right.target) ||
                    right.target.IsChildOf(left.target))
                    error = "Target harus terpisah dari tulang dan dari target lainnya.";
            }

            if (error == null)
            {
                warnedSetup = false;
                return true;
            }
            status = error;
            currentWeight = 0f;
            acceptedReach = false;
            hasSavedTargets = false;
            if (carryRig) carryRig.weight = 0f;
            if (!warnedSetup)
            {
                Debug.LogWarning("HeldObjectGripController: " + error, this);
                warnedSetup = true;
            }
            return false;
        }

        private static bool ValidArm(TwoBoneIKConstraintData arm)
        {
            return arm.root && arm.mid && arm.tip && arm.target &&
                arm.root != arm.mid && arm.mid != arm.tip && arm.root != arm.tip &&
                arm.mid.IsChildOf(arm.root) && arm.tip.IsChildOf(arm.mid);
        }

        private static float ArmLength(TwoBoneIKConstraintData arm)
        {
            return Vector3.Distance(arm.root.position, arm.mid.position) +
                Vector3.Distance(arm.mid.position, arm.tip.position);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
