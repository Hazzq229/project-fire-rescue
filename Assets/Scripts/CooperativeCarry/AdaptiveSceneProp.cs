using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public enum ScenePropSize { Small, Medium, Big }

    /// <summary>Inspectable classification on the normalized runtime carry root.</summary>
    public sealed class AdaptiveSceneProp : MonoBehaviour
    {
        [field: SerializeField] public ScenePropSize Category { get; private set; }
        [field: SerializeField] public Vector3 Dimensions { get; private set; }
        [field: SerializeField] public bool RequiresTwoPlayers { get; private set; }
        [field: SerializeField] public Transform Visual { get; private set; }
        public void Configure(ScenePropSize category, Vector3 dimensions, bool twoPlayers, Transform visual)
        { Category = category; Dimensions = dimensions; RequiresTwoPlayers = twoPlayers; Visual = visual; }
    }
}
