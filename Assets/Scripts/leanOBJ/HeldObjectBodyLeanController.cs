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
        [Tooltip("Batas pose akhir setelah exaggeration; kurva bobot tetap menjadi nilai dasar.")]
        [SerializeField, Range(0f, BodyLeanConstraint.MaxSupportedAngle)] private float maximumAngle = 30f;
        [SerializeField, Min(1f)] private float degreesPerSecond = 20f;
        [Header("Stylized load and pickup anticipation")]
        [SerializeField] private bool enableStylizedLoadPose = true;
        [SerializeField, Range(1f, 3f)] private float loadExaggeration = 1.6f;
        [SerializeField, Range(0f, 20f)] private float maximumHeadLookUp = 8f;
        [SerializeField, Range(0f, 20f)] private float anticipationAngle = 10f;
        [SerializeField, Min(.05f)] private float anticipationDuration = .25f;
        [SerializeField] private float currentStylizedAngle;
        [SerializeField] private float currentHeadLookUp;
        private HInteractions.Liftable anticipationObject;
        private float anticipationElapsed;
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
            ResetStylizedPose();
        }

        private void Update()
        {
            targetAngle = currentLoadKg = 0f;
            Vector3 targetEffort = Vector3.zero;
            bool loadPose = false;
            bool anticipation = false;
            float anticipationTarget = 0f;
            bool hasContext = AdaptiveAnimationManager.TryRead(interactionController, out var adaptation);
            var held = hasContext ? adaptation.Context.HeldObject : interactionController ? interactionController.HeldObject : null;
            var pending = hasContext ? adaptation.Context.PendingObject : interactionController ? interactionController.PendingPickupObject : null;
            var carryRole = hasContext ? adaptation.Context.Role : interactionController ? interactionController.CurrentCarryRole : CarryRole.None;
            if (pending != anticipationObject)
            {
                anticipationObject = pending;
                anticipationElapsed = 0f;
            }
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
            else if (enableStylizedLoadPose && pending && !held &&
                     pending is not CooperativeHoseGrip)
            {
                var load = pending.GetComponent<CarryAnimationLoad>();
                currentLoadKg = hasContext ? adaptation.EffectiveWeightKg : load && load.isActiveAndEnabled ? load.WeightKg : 0f;
                anticipationElapsed += Time.deltaTime;
                float preparation = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(anticipationElapsed / Mathf.Max(.05f, anticipationDuration)));
                anticipationTarget = -anticipationAngle * Mathf.Clamp01(currentLoadKg / 50f) * preparation;
                anticipation = true;
                status = "Antisipasi: membungkuk menyiapkan angkatan sebelum kontak";
            }
            else if (!held || !held.isActiveAndEnabled)
                status = "Tidak memegang objek: kembali ke pose dasar";
            else if (interactionController.IsPlacingDown)
                status = "Meletakkan: mengikuti gerak membungkuk dari clip pickup";
            else
            {
                var load = held.GetComponent<CarryAnimationLoad>();
                if (hasContext ? !adaptation.HasLoad : !load || !load.isActiveAndEnabled)
                    status = "Objek tidak memiliki data bobot animasi aktif";
                else if (weightToAngle == null || weightToAngle.length == 0)
                    status = "Kurva bobot belum diisi";
                else
                {
                    var coop = held as CooperativeCarryObject;
                    currentLoadKg = hasContext ? adaptation.EffectiveWeightKg : coop ? coop.LoadPerPlayerKg : load.WeightKg;
                    targetAngle = weightToAngle.Evaluate(currentLoadKg)
                        * (positiveXBendsForward ? 1f : -1f);
                    loadPose = enableStylizedLoadPose;
                    if (coop)
                    {
                        targetAngle = Mathf.Clamp(targetAngle, -cooperativeLoadAngleLimit, cooperativeLoadAngleLimit);
                        targetEffort = Vector3.Cross(Vector3.up, coop.GetEffortDirection(interactionController)) * conflictLeanDegrees;
                    }
                    status = "Mengikuti bobot objek";
                }
            }
            if (interactionController && held is CooperativeHoseGrip hoseGrip && hoseGrip.Hose)
            {
                targetAngle = hoseGrip.Hose.Pressure * (carryRole == CarryRole.Front ? 9f : 4f);
                targetEffort = hoseGrip.Hose.GetCarryEffort(interactionController);
                loadPose = false;
                status = "Menahan tekanan fire hose";
            }
            if (float.IsNaN(targetAngle) || float.IsInfinity(targetAngle)) targetAngle = 0f;
            bool knockedBack = interactionController &&
                interactionController.TryGetComponent<ThirdPersonPlayerController>(out var mover) && mover.IsKnockedBack;
            if (knockedBack)
            {
                targetAngle = -18f;
                loadPose = anticipation = false;
                status = "Dorongan balik fire hose";
            }
            float limit = Mathf.Clamp(maximumAngle, 0f, BodyLeanConstraint.MaxSupportedAngle);
            if (knockedBack) limit = Mathf.Max(limit, 18f);
            targetAngle = Mathf.Clamp(targetAngle, -limit, limit);
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle,
                Mathf.Max(1f, degreesPerSecond) * Time.deltaTime);
            float stylizedTarget = anticipation ? anticipationTarget : loadPose
                ? Mathf.Clamp(Mathf.Abs(targetAngle) * loadExaggeration, 0f,
                    held is CooperativeCarryObject
                        ? Mathf.Min(limit, cooperativeLoadAngleLimit) : limit) : 0f;
            currentStylizedAngle = Mathf.MoveTowards(currentStylizedAngle, stylizedTarget,
                Mathf.Max(1f, degreesPerSecond) * Time.deltaTime);
            float headTarget = loadPose ? maximumHeadLookUp * Mathf.Clamp01(currentLoadKg / 50f) : 0f;
            bool aimingHose = interactionController && held is CooperativeHoseGrip aimingGrip &&
                aimingGrip.Hose && carryRole == CarryRole.Front;
            if (aimingHose)
            {
                var hose = ((CooperativeHoseGrip)held).Hose;
                headTarget = Mathf.Clamp(6f + Mathf.Asin(hose.AimDirection.y) * Mathf.Rad2Deg * .6f, -20f, 20f);
            }
            currentHeadLookUp = Mathf.MoveTowards(currentHeadLookUp, headTarget,
                (aimingHose ? 90f : Mathf.Max(1f, degreesPerSecond)) * Time.deltaTime);
            // World-space backward rotation avoids assumptions about imported bone local X axes.
            Vector3 right = interactionController ? interactionController.transform.right : transform.right;
            bodyLean.SetStylizedPose(-right * currentStylizedAngle, -right * currentHeadLookUp);
            bodyLean.SetAngle(enableStylizedLoadPose && !manualPreview &&
                !(interactionController && held is CooperativeHoseGrip) && !knockedBack
                ? 0f : currentAngle);
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
            ResetStylizedPose();
        }

        private void ResetStylizedPose()
        {
            anticipationObject = null;
            anticipationElapsed = currentStylizedAngle = currentHeadLookUp = 0f;
            if (bodyLean) bodyLean.SetStylizedPose(Vector3.zero, Vector3.zero);
        }
    }
}
