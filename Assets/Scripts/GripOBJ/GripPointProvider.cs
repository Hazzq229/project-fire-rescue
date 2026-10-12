using UnityEngine;

namespace TA.AdaptiveAnimation
{
    // Format posisi pegangan yang dapat dipakai bentuk objek lain nanti.
    public abstract class GripPointProvider : MonoBehaviour
    {
        // Provider lama tetap bekerja tanpa orientasi.
        public virtual string GripStyleId => string.Empty;
        public virtual bool TryGetGripRotations(out Quaternion rotationA, out Quaternion rotationB)
        {
            rotationA = rotationB = Quaternion.identity;
            return false;
        }

        public abstract bool TryGetGripPoints(out Vector3 pointA, out Vector3 pointB);

        // Cooperative providers select a different pair for each holder's role.
        public virtual bool TryGetGripPoints(HPlayer.InteractionController holder,
            out Vector3 a, out Vector3 b) => TryGetGripPoints(out a, out b);
        public virtual bool TryGetGripRotations(HPlayer.InteractionController holder,
            out Quaternion a, out Quaternion b) => TryGetGripRotations(out a, out b);
    }
}
