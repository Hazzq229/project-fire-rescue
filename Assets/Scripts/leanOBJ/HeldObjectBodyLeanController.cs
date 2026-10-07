using UnityEngine;
using HPlayer;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(180)]
    [AddComponentMenu("TA/Adaptive Animation/Held Object Body Lean Controller")]
    public sealed class HeldObjectBodyLeanController : MonoBehaviour
    {
        [Header("Referensi")]
        [SerializeField] private InteractionController interactionController;
        [SerializeField] private BodyLeanConstraint bodyLean;

        [Header("Pemetaan bobot ke sudut tambahan")]
        [Tooltip("X = kg, Y = derajat. Default: 0/0, 10/5, 30/10, 50/15; di atas 50 tetap 15.")]
        [SerializeField] private AnimationCurve weightToAngle = CreateDefaultCurve();
        [Tooltip("Aktif: gunakan tanda sudut kurva apa adanya. Nonaktif: balik tanda sudut kurva.")]
        [SerializeField] private bool positiveXBendsForward = true;
        [Tooltip("Batas awal konservatif 5 derajat. Naikkan bertahap setelah kontak tangan diperiksa.")]
        [SerializeField, Range(0f, BodyLeanConstraint.MaxSupportedAngle)] private float maximumAngle = 5f;
        [SerializeField, Min(1f)] private float degreesPerSecond = 20f;

        [Header("Pengujian arah - matikan setelah selesai")]
        [SerializeField] private bool manualPreview;
        [SerializeField, Range(-BodyLeanConstraint.MaxSupportedAngle, BodyLeanConstraint.MaxSupportedAngle)] private float previewAngle = 3f;

        [Header("Pantauan saat Play - jangan diedit")]
        [SerializeField] private string status;
        [SerializeField] private float currentLoadKg;
        [SerializeField] private float targetAngle;
        [SerializeField] private float currentAngle;

        private static AnimationCurve CreateDefaultCurve()
        {
            // Tangent tiap ruas dibuat linear: perubahan bobot di antara titik juga berpengaruh.
            return new AnimationCurve(
                new Keyframe(0f, 0f, 0.5f, 0.5f),
                new Keyframe(10f, 5f, 0.5f, 0.25f),
                new Keyframe(30f, 10f, 0.25f, 0.25f),
                new Keyframe(50f, 15f, 0.25f, 0f))
            {
                preWrapMode = WrapMode.ClampForever,
                postWrapMode = WrapMode.ClampForever
            };
        }

        private void OnEnable()
        {
            currentAngle = targetAngle = currentLoadKg = 0f;
            if (bodyLean) bodyLean.SetAngle(0f);
        }

        private void Update()
        {
            targetAngle = currentLoadKg = 0f;
            if (!bodyLean || !bodyLean.data.chest)
            {
                if (bodyLean) bodyLean.SetAngle(0f);
                currentAngle = 0f;
                status = "Isi Body Lean dan Chest sebelum Play";
                return;
            }
            if (manualPreview)
            {
                targetAngle = previewAngle;
                status = "Uji manual: tidak membaca bobot";
            }
            else if (!interactionController || !interactionController.isActiveAndEnabled)
                status = "Interaction Controller tidak tersedia";
            else if (!interactionController.HeldObject || !interactionController.HeldObject.isActiveAndEnabled)
                status = "Tidak memegang objek: kembali ke pose dasar";
            else
            {
                var load = interactionController.HeldObject.GetComponent<CarryAnimationLoad>();
                if (!load || !load.isActiveAndEnabled)
                    status = "Objek tidak memiliki data bobot animasi aktif";
                else if (weightToAngle == null || weightToAngle.length == 0)
                    status = "Kurva bobot belum diisi";
                else
                {
                    currentLoadKg = load.WeightKg;
                    targetAngle = weightToAngle.Evaluate(currentLoadKg)
                        * (positiveXBendsForward ? 1f : -1f);
                    status = "Mengikuti bobot objek";
                }
            }
            if (float.IsNaN(targetAngle) || float.IsInfinity(targetAngle)) targetAngle = 0f;
            float limit = Mathf.Clamp(maximumAngle, 0f, BodyLeanConstraint.MaxSupportedAngle);
            targetAngle = Mathf.Clamp(targetAngle, -limit, limit);
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle,
                Mathf.Max(1f, degreesPerSecond) * Time.deltaTime);
            bodyLean.SetAngle(currentAngle);
        }

        private void OnDisable()
        {
            currentAngle = targetAngle = currentLoadKg = 0f;
            if (bodyLean) bodyLean.SetAngle(0f);
        }
    }
}
