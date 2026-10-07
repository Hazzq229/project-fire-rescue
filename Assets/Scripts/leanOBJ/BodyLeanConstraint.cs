using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace TA.AdaptiveAnimation
{
    [Serializable]
    public struct BodyLeanData : IAnimationJobData
    {
        // Nama field lama dipertahankan agar referensi dan controller kompatibel.
        public Transform chest;
        [SyncSceneToStream, Range(-BodyLeanConstraint.MaxSupportedAngle, BodyLeanConstraint.MaxSupportedAngle)] public float angleDegrees;

        [Tooltip("Opsional: spine.001. Harus merupakan leluhur Chest.")]
        public Transform lowerSpine;
        [Tooltip("Opsional: spine.004. Harus merupakan keturunan Chest.")]
        public Transform neck;
        [Tooltip("Porsi sudut pada punggung bawah. 0.6 = 60%; sisanya pada dada.")]
        [SyncSceneToStream, Range(0f, 1f)] public float lowerSpineShare;
        [Tooltip("Koreksi leher berlawanan terhadap sudut input. 0 = tanpa koreksi; mulai 0.8.")]
        [SyncSceneToStream, Range(0f, 1f)] public float neckCompensation;

        public bool IsValid()
        {
            if (!chest) return false;
            if (lowerSpine && (lowerSpine == chest || !chest.IsChildOf(lowerSpine))) return false;
            if (neck && (neck == chest || neck == lowerSpine || !neck.IsChildOf(chest))) return false;
            return true;
        }

        public void SetDefaultValues()
        {
            chest = lowerSpine = neck = null;
            angleDegrees = 0f;
            lowerSpineShare = 0.6f;
            neckCompensation = 0.8f;
        }
    }

    public struct BodyLeanJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle chest;
        public ReadWriteTransformHandle lowerSpine;
        public ReadWriteTransformHandle neck;
        public bool hasLowerSpine;
        public bool hasNeck;
        public FloatProperty angle;
        public FloatProperty lowerShare;
        public FloatProperty neckCorrection;
        public FloatProperty jobWeight { get; set; }

        public void ProcessRootMotion(AnimationStream stream) { }

        private static void AddLocalX(AnimationStream stream, ReadWriteTransformHandle bone, float degrees)
        {
            bone.SetLocalRotation(stream,
                bone.GetLocalRotation(stream) * Quaternion.AngleAxis(degrees, Vector3.right));
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            float influence = Mathf.Clamp01(jobWeight.Get(stream));
            if (influence <= 0f)
            {
                if (hasLowerSpine) AnimationRuntimeUtils.PassThrough(stream, lowerSpine);
                AnimationRuntimeUtils.PassThrough(stream, chest);
                if (hasNeck) AnimationRuntimeUtils.PassThrough(stream, neck);
                return;
            }

            float degrees = Mathf.Clamp(angle.Get(stream), -BodyLeanConstraint.MaxSupportedAngle, BodyLeanConstraint.MaxSupportedAngle) * influence;
            float share = hasLowerSpine ? Mathf.Clamp01(lowerShare.Get(stream)) : 0f;

            // Diproses dari induk ke anak. Jumlah sudut lokal tetap sebesar input.
            if (hasLowerSpine) AddLocalX(stream, lowerSpine, degrees * share);
            AddLocalX(stream, chest, degrees * (1f - share));
            // Koreksi perkiraan pada sumbu lokal yang sudah dikalibrasi pengguna.
            // Bukan pengunci pandangan dunia: sumbu bisa berbeda saat pose berubah.
            if (hasNeck) AddLocalX(stream, neck, -degrees * Mathf.Clamp01(neckCorrection.Get(stream)));
        }
    }

    public class BodyLeanBinder : AnimationJobBinder<BodyLeanJob, BodyLeanData>
    {
        public override BodyLeanJob Create(Animator animator, ref BodyLeanData data, Component component)
        {
            var job = new BodyLeanJob
            {
                chest = ReadWriteTransformHandle.Bind(animator, data.chest),
                hasLowerSpine = data.lowerSpine != null,
                hasNeck = data.neck != null,
                angle = FloatProperty.Bind(animator, component,
                    ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(BodyLeanData.angleDegrees))),
                lowerShare = FloatProperty.Bind(animator, component,
                    ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(BodyLeanData.lowerSpineShare))),
                neckCorrection = FloatProperty.Bind(animator, component,
                    ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(BodyLeanData.neckCompensation)))
            };
            if (job.hasLowerSpine) job.lowerSpine = ReadWriteTransformHandle.Bind(animator, data.lowerSpine);
            if (job.hasNeck) job.neck = ReadWriteTransformHandle.Bind(animator, data.neck);
            return job;
        }
        public override void Destroy(BodyLeanJob job) { }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("TA/Adaptive Animation/Body Lean Constraint")]
    public sealed class BodyLeanConstraint : RigConstraint<BodyLeanJob, BodyLeanData, BodyLeanBinder>
    {
        public const float MaxSupportedAngle = 60f;

        public void SetAngle(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) degrees = 0f;
            var value = data;
            value.angleDegrees = Mathf.Clamp(degrees, -MaxSupportedAngle, MaxSupportedAngle);
            data = value;
        }
    }
}
