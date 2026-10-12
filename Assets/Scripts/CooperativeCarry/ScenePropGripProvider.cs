using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public sealed class ScenePropGripProvider : GripPointProvider
    {
        private BoxCollider shape;
        private float gripY;
        private bool cooperative;
        public void Configure(BoxCollider collider, float height, bool twoPlayers)
        { shape = collider; gripY = height; cooperative = twoPlayers; }

        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b)
            => Points(null, out a, out b);
        public override bool TryGetGripPoints(InteractionController holder, out Vector3 a, out Vector3 b)
            => Points(holder, out a, out b);

        private bool Points(InteractionController holder, out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            if (!shape) return false;
            float end = -1f;
            if (cooperative)
            {
                var role = GetComponent<CooperativeCarryObject>().GetRole(holder);
                if (role == CarryRole.None) return false;
                end = role == CarryRole.Front ? 1f : -1f;
            }
            // Grip the reachable part of an end surface, not the distant corners of a wide table.
            float spread = Mathf.Min(.19f, shape.size.x * .5f);
            Vector3 center = shape.center + new Vector3(0f, gripY, end * shape.size.z * .5f);
            a = transform.TransformPoint(center + Vector3.right * end * spread);
            b = transform.TransformPoint(center - Vector3.right * end * spread);
            return true;
        }
    }
}
