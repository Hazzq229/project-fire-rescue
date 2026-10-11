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
    }
}
