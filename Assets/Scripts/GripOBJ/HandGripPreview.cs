using UnityEngine;

namespace TA.AdaptiveAnimation
{
    // Uji posisi saja: aktif di Play Mode, Animator Update Mode = Normal.
    // Tidak mengubah fisika, input, animasi dasar, hint, atau weight rig.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class HandGripPreview : MonoBehaviour
    {
        [SerializeField] private GripPointProvider gripProvider;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;
        [Tooltip("Aktifkan jika tangan menuju sisi kardus yang berlawanan.")]
        [SerializeField] private bool swapHands;
        private bool reportedInvalid;

        private void Update()
        {
            if (!gripProvider || !gripProvider.isActiveAndEnabled ||
                !leftHandTarget || !rightHandTarget ||
                leftHandTarget == rightHandTarget ||
                leftHandTarget.IsChildOf(rightHandTarget) ||
                rightHandTarget.IsChildOf(leftHandTarget))
            {
                ReportInvalid();
                return;
            }

            if (!gripProvider.TryGetGripPoints(out Vector3 a, out Vector3 b))
            {
                ReportInvalid();
                return;
            }

            reportedInvalid = false;
            leftHandTarget.position = swapHands ? b : a;
            rightHandTarget.position = swapHands ? a : b;
        }

        private void ReportInvalid()
        {
            if (reportedInvalid) return;
            reportedInvalid = true;
            Debug.LogWarning("HandGripPreview: isi provider dengan BoxCollider valid " +
                "dan dua objek Target yang berbeda serta saling terpisah. " +
                "Gunakan objek target di Carry_Rig, bukan tulang tangan.", this);
        }
    }
}
