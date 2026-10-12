using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public sealed class HoseGripProvider : GripPointProvider
    {
        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b)
        {
            a = transform.TransformPoint(new Vector3(-.14f, 0f, .02f));
            b = transform.TransformPoint(new Vector3(.14f, 0f, -.05f));
            return true;
        }
    }
}
