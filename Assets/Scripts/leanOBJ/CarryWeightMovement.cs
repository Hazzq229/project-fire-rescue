using UnityEngine;
using HInteractions;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarryAnimationLoad))]
    public sealed class CarryWeightMovement : MonoBehaviour
    {
        [SerializeField] private CarryWeightSpeedProfile speedProfile;
        public void Configure(CarryWeightSpeedProfile profile) => speedProfile = profile;

        [Header("Hasil pembacaan terakhir saat dipegang - jangan diedit")]
        [SerializeField] private string status = "Belum dibaca oleh Liftable";
        [SerializeField] private float evaluatedWeightKg;
        [SerializeField] private float speedPercent = 100f;
        [SerializeField] private float evaluatedPenalty;

        public bool TryGetSpeedPenalty(out float penalty)
        {
            penalty = 0f;
            if (!isActiveAndEnabled) return false;
            var liftable = GetComponent<Liftable>();
            var load = GetComponent<CarryAnimationLoad>();
            if (!liftable || !liftable.SupportsStableSingleCarry)
            {
                status = "Tahap ini hanya untuk objek satu pemegang";
                return false;
            }
            if (!speedProfile || !load || !load.isActiveAndEnabled)
            {
                status = "Profil atau bobot tidak aktif: memakai Default Speed Penalty";
                return false;
            }
            if (!speedProfile.TryEvaluateMultiplier(load.WeightKg, out float multiplier))
            {
                status = "Kurva tidak valid: memakai Default Speed Penalty";
                return false;
            }
            evaluatedWeightKg = load.WeightKg;
            speedPercent = multiplier * 100f;
            evaluatedPenalty = penalty = 1f - multiplier;
            status = "Kecepatan mengikuti bobot animasi";
            return true;
        }
    }
}
