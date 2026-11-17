using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public sealed class HoseGripProvider : GripPointProvider
    {
        private bool IsRear => TryGetComponent<CooperativeHoseGrip>(out var grip) && grip.Role == CarryRole.Back;
        public Vector3 LocalGripCenter => new Vector3(0f, 0f, IsRear ? -.10f : -.20f);
        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b)
        {
            Vector3 halfSpacing = Vector3.right * (IsRear ? .10f : .07f);
            a = transform.TransformPoint(LocalGripCenter - halfSpacing);
            b = transform.TransformPoint(LocalGripCenter + halfSpacing);
            return true;
        }
    }
}
