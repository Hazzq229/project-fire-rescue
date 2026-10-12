using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [RequireComponent(typeof(CooperativeCarryObject), typeof(BoxCollider))]
    public sealed class CooperativeGripProvider : GripPointProvider
    {
        [SerializeField, Range(0f, 1f)] private float gripHeight = .5f;
        [SerializeField] private SharedGripOrientationProfile orientationProfile;
        [SerializeField] private GripPresetSize gripPreset = GripPresetSize.Medium;
        public override string GripStyleId => "box-side";
        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            return false; // A holder is required: never share one pair between two characters.
        }
        public override bool TryGetGripPoints(InteractionController holder, out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            var role = GetComponent<CooperativeCarryObject>().GetRole(holder);
            if (role == CarryRole.None) return false;
            var box = GetComponent<BoxCollider>();
            float sign = role == CarryRole.Front ? 1f : -1f;
            var center = box.center + new Vector3(0f, (gripHeight - .5f) * box.size.y, sign * box.size.z * .5f);
            // At +Z the player faces -Z, so anatomical left is object +X.
            a = transform.TransformPoint(center + Vector3.right * sign * box.size.x * .5f);
            b = transform.TransformPoint(center - Vector3.right * sign * box.size.x * .5f);
            return true;
        }
        public override bool TryGetGripRotations(InteractionController holder, out Quaternion a, out Quaternion b)
        {
            a = b = Quaternion.identity;
            var role = GetComponent<CooperativeCarryObject>().GetRole(holder);
            if (role == CarryRole.None || !orientationProfile || !orientationProfile.TryGet(gripPreset, out a, out b)) return false;
            var frame = transform.rotation * Quaternion.Euler(0f, role == CarryRole.Front ? 180f : 0f, 0f);
            a = frame * a;
            b = frame * b;
            return true;
        }
    }
}
