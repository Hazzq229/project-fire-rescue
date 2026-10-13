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
