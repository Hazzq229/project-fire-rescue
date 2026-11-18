using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public enum ScenePropGripKind { BoxBounds, BoxSurface }

    /// <summary>Authored on the visual object, before the runtime adapter creates its carry root.</summary>
    [DisallowMultipleComponent]
    public sealed class ScenePropCarrySettings : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float weightKg = 5f;
        [SerializeField] private bool requiresTwoPlayers;
        [Tooltip("Box Bounds: kontak pada bounds. Box Surface: kontak pada mesh readable, memakai solver furnitur yang sudah ada.")]
        [SerializeField] private ScenePropGripKind gripKind;

        public float WeightKg => Mathf.Max(0f, weightKg);
        public bool RequiresTwoPlayers => requiresTwoPlayers;
        public ScenePropGripKind GripKind => gripKind;
    }
}
