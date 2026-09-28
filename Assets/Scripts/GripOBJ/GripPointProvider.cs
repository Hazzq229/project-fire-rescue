using UnityEngine;

namespace TA.AdaptiveAnimation
{
    // Format posisi pegangan yang dapat dipakai bentuk objek lain nanti.
    public abstract class GripPointProvider : MonoBehaviour
    {
        public abstract bool TryGetGripPoints(out Vector3 pointA, out Vector3 pointB);
    }
}
