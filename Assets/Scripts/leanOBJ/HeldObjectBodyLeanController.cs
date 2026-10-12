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
        [Header("Cooperative effort")]
        [SerializeField, Range(0f, 12f)] private float conflictLeanDegrees = 8f;
        [Tooltip("Keep cooperative grip reachable when one player bears the whole heavy load.")]
        [SerializeField, Range(0f, 60f)] private float cooperativeLoadAngleLimit = 25f;
        [SerializeField] private Vector3 currentEffortRotation;

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
            currentEffortRotation = Vector3.zero;
            if (bodyLean) bodyLean.SetEffort(Vector3.zero);
        }

        private void Update()
        {
            targetAngle = currentLoadKg = 0f;
            Vector3 targetEffort = Vector3.zero;
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
            else if (interactionController.IsPlacingDown)
                status = "Meletakkan: mengikuti gerak membungkuk dari clip pickup";
            else
            {
                var load = interactionController.HeldObject.GetComponent<CarryAnimationLoad>();
                if (!load || !load.isActiveAndEnabled)
                    status = "Objek tidak memiliki data bobot animasi aktif";
                else if (weightToAngle == null || weightToAngle.length == 0)
                    status = "Kurva bobot belum diisi";
                else
                {
                    var coop = interactionController.HeldObject as CooperativeCarryObject;
                    currentLoadKg = coop ? coop.LoadPerPlayerKg : load.WeightKg;
                    targetAngle = weightToAngle.Evaluate(currentLoadKg)
                        * (positiveXBendsForward ? 1f : -1f);
                    if (coop)
                    {
                        targetAngle = Mathf.Clamp(targetAngle, -cooperativeLoadAngleLimit, cooperativeLoadAngleLimit);
                        targetEffort = Vector3.Cross(Vector3.up, coop.GetEffortDirection(interactionController)) * conflictLeanDegrees;
                    }
                    status = "Mengikuti bobot objek";
                }
            }
            if (interactionController && interactionController.HeldObject is CooperativeHoseGrip hoseGrip && hoseGrip.Hose)
            {
                targetAngle = hoseGrip.Hose.Pressure * (hoseGrip.Role == CarryRole.Front ? 9f : 4f);
                status = "Menahan tekanan fire hose";
            }
            if (float.IsNaN(targetAngle) || float.IsInfinity(targetAngle)) targetAngle = 0f;
            if (interactionController && interactionController.TryGetComponent<ThirdPersonPlayerController>(out var mover) && mover.IsKnockedBack)
            {
                targetAngle = -18f;
                status = "Dorongan balik fire hose";
            }
            float limit = Mathf.Clamp(maximumAngle, 0f, BodyLeanConstraint.MaxSupportedAngle);
            targetAngle = Mathf.Clamp(targetAngle, -limit, limit);
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle,
                Mathf.Max(1f, degreesPerSecond) * Time.deltaTime);
            bodyLean.SetAngle(currentAngle);
            currentEffortRotation = Vector3.MoveTowards(currentEffortRotation, targetEffort,
                Mathf.Max(1f, degreesPerSecond) * Time.deltaTime);
            bodyLean.SetEffort(currentEffortRotation);
        }

        private void OnDisable()
        {
            currentAngle = targetAngle = currentLoadKg = 0f;
            if (bodyLean) bodyLean.SetAngle(0f);
            currentEffortRotation = Vector3.zero;
            if (bodyLean) bodyLean.SetEffort(Vector3.zero);
        }
    }
}
