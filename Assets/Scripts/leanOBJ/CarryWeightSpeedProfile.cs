using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [CreateAssetMenu(fileName = "CarryWeightSpeedProfile", menuName = "TA/Adaptive Animation/Carry Weight Speed Profile")]
    public sealed class CarryWeightSpeedProfile : ScriptableObject
    {
        [Tooltip("X = kg, Y = pengali kecepatan. 1 = 100%, 0.85 = 85%.")]
        [SerializeField] private AnimationCurve weightToSpeedMultiplier = CreateDefaultCurve();
        [Tooltip("Batas pengali minimum; tidak membuat karakter berhenti total.")]
        [SerializeField, Range(0.01f, 1f)] private float minimumMultiplier = 0.1f;

        private static AnimationCurve CreateDefaultCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(10f, 1f, 0f, -0.0075f),
                new Keyframe(30f, 0.85f, -0.0075f, -0.0075f),
                new Keyframe(50f, 0.7f, -0.0075f, 0f))
            {
                preWrapMode = WrapMode.ClampForever,
                postWrapMode = WrapMode.ClampForever
            };
        }

        public bool TryEvaluateMultiplier(float weightKg, out float multiplier)
        {
            multiplier = 1f;
            if (weightToSpeedMultiplier == null || weightToSpeedMultiplier.length == 0 ||
                float.IsNaN(weightKg) || float.IsInfinity(weightKg)) return false;
            float value = weightToSpeedMultiplier.Evaluate(Mathf.Max(0f, weightKg));
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            float minimum = float.IsNaN(minimumMultiplier) || float.IsInfinity(minimumMultiplier)
                ? 0.1f : Mathf.Clamp(minimumMultiplier, 0.01f, 1f);
            multiplier = Mathf.Clamp(value, minimum, 1f);
            return true;
        }
    }
}
