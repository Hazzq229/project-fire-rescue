using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    public sealed class BoxGripProvider : GripPointProvider
    {
        [SerializeField] private BoxCollider boxCollider;
        [Tooltip("0 = bawah kardus; 1 = atas kardus.")]
        [SerializeField, Range(0f, 1f)] private float gripHeight = 0.5f;
        [Tooltip("0 = sisi -Z; 1 = sisi +Z kardus.")]
        [SerializeField, Range(0f, 1f)] private float gripDepth = 0.5f;

        [Header("Preset arah pegangan bersama")]
        [SerializeField] private SharedGripOrientationProfile sharedOrientationProfile;
        [SerializeField] private GripPresetSize gripPreset = GripPresetSize.Medium;
        [Tooltip("Opsional, derajat tambahan pada frame grip A (sisi -X collider). Awali nol.")]
        [SerializeField] private Vector3 additionalRotationA;
        [Tooltip("Opsional, derajat tambahan pada frame grip B (sisi +X collider). Awali nol.")]
        [SerializeField] private Vector3 additionalRotationB;
        public SharedGripOrientationProfile SharedOrientationProfile => sharedOrientationProfile;
        public GripPresetSize GripPreset => gripPreset;

        public bool RecordSharedFrames(Quaternion worldFrameA, Quaternion worldFrameB)
        {
            if (!boxCollider || !sharedOrientationProfile) return false;
            Quaternion inverseSpace = Quaternion.Inverse(boxCollider.transform.rotation);
            sharedOrientationProfile.Record(gripPreset,
                inverseSpace * worldFrameA * Quaternion.Inverse(Quaternion.Euler(additionalRotationA)),
                inverseSpace * worldFrameB * Quaternion.Inverse(Quaternion.Euler(additionalRotationB)));
            return true;
        }

        private void Reset() => boxCollider = GetComponent<BoxCollider>();

        public override bool TryGetGripPoints(out Vector3 pointA, out Vector3 pointB)
        {
            pointA = pointB = Vector3.zero;
            if (!boxCollider) return false;
            Vector3 size = boxCollider.size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return false;

            Vector3 center = boxCollider.center;
            float y = center.y + (gripHeight - 0.5f) * size.y;
            float z = center.z + (gripDepth - 0.5f) * size.z;
            pointA = boxCollider.transform.TransformPoint(
                new Vector3(center.x - size.x * 0.5f, y, z));
            pointB = boxCollider.transform.TransformPoint(
                new Vector3(center.x + size.x * 0.5f, y, z));
            return true;
        }

        public override string GripStyleId => "box-side";

        // +Z frame pegangan mengarah ke dalam kardus; +Y mengikuti atas kardus.
        // A pada sisi -X, B pada sisi +X. Asumsi skala positif tanpa shear.
        public override bool TryGetGripRotations(out Quaternion rotationA, out Quaternion rotationB)
        {
            rotationA = rotationB = Quaternion.identity;
            if (!boxCollider) return false;
            Quaternion boxRotation = boxCollider.transform.rotation;
            Quaternion localA = Quaternion.LookRotation(Vector3.right, Vector3.up);
            Quaternion localB = Quaternion.LookRotation(Vector3.left, Vector3.up);
            if (sharedOrientationProfile && !sharedOrientationProfile.TryGet(gripPreset, out localA, out localB))
                return false;
            rotationA = boxRotation * localA * Quaternion.Euler(additionalRotationA);
            rotationB = boxRotation * localB * Quaternion.Euler(additionalRotationB);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetGripPoints(out Vector3 a, out Vector3 b)) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(a, 0.025f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(b, 0.025f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
        }
    }
}
