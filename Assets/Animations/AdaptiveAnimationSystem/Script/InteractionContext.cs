using HGame.Objects;
using HInteractions;
using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>One player's current interaction inputs, without ownership or pose side effects.</summary>
    public readonly struct InteractionContext
    {
        public InteractionController Actor { get; }
        public Liftable HeldObject { get; }
        public Liftable PendingObject { get; }
        public ObjectInteractionTarget ObjectTarget { get; }
        public Component Source { get; }
        public AdaptiveObjectType ObjectType { get; }
        public Vector3 ObjectSize { get; }
        public float ObjectWeightKg { get; }
        public float EffectiveWeightKg { get; }
        public bool HasWeight { get; }
        public CarryRole Role { get; }
        public AdaptiveInteractionType InteractionType { get; }
        public InteractionPhase Phase { get; }
        public GripPointProvider GripProvider { get; }
        public string ObjectAnimation { get; }

        internal InteractionContext(InteractionController actor, Liftable held, Liftable pending,
            ObjectInteractionTarget target, Component source, AdaptiveObjectType objectType,
            Vector3 size, float weight, float effectiveWeight, bool hasWeight, CarryRole role,
            AdaptiveInteractionType interactionType, InteractionPhase phase,
            GripPointProvider provider, string objectAnimation)
        {
            Actor = actor; HeldObject = held; PendingObject = pending; ObjectTarget = target;
            Source = source; ObjectType = objectType; ObjectSize = size;
            ObjectWeightKg = weight; EffectiveWeightKg = effectiveWeight; HasWeight = hasWeight;
            Role = role; InteractionType = interactionType; Phase = phase;
            GripProvider = provider; ObjectAnimation = objectAnimation;
        }
    }
}
