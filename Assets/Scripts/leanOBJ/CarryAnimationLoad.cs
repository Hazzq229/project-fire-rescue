using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TA/Adaptive Animation/Carry Animation Load")]
    public sealed class CarryAnimationLoad : MonoBehaviour
    {
        [Tooltip("Bobot objek untuk adaptasi animasi. Tidak mengubah Rigidbody Mass.")]
        [SerializeField, Min(0f)] private float weightKg = 10f;
        public float WeightKg => float.IsNaN(weightKg) || float.IsInfinity(weightKg)
            ? 0f : Mathf.Max(0f, weightKg);
    }
}
