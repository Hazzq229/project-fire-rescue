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
        public bool IsPickingUp { get; }
        public bool HasActiveGripProvider { get; }

        internal InteractionContext(InteractionController actor, Liftable held, Liftable pending,
            ObjectInteractionTarget target, Component source, AdaptiveObjectType objectType,
            Vector3 size, float weight, float effectiveWeight, bool hasWeight, CarryRole role,
            AdaptiveInteractionType interactionType, InteractionPhase phase,
            GripPointProvider provider, string objectAnimation, bool isPickingUp, bool hasActiveGripProvider)
        {
            Actor = actor; HeldObject = held; PendingObject = pending; ObjectTarget = target;
            Source = source; ObjectType = objectType; ObjectSize = size;
            ObjectWeightKg = weight; EffectiveWeightKg = effectiveWeight; HasWeight = hasWeight;
            Role = role; InteractionType = interactionType; Phase = phase;
            GripProvider = provider; ObjectAnimation = objectAnimation;
            IsPickingUp = isPickingUp; HasActiveGripProvider = hasActiveGripProvider;
        }

        internal bool SameInputs(in InteractionContext other) =>
            Actor == other.Actor && HeldObject == other.HeldObject && PendingObject == other.PendingObject &&
            ObjectTarget == other.ObjectTarget && Source == other.Source && ObjectType == other.ObjectType &&
            ObjectSize.Equals(other.ObjectSize) && ObjectWeightKg.Equals(other.ObjectWeightKg) &&
            EffectiveWeightKg.Equals(other.EffectiveWeightKg) && HasWeight == other.HasWeight &&
            Role == other.Role && InteractionType == other.InteractionType && Phase == other.Phase &&
            GripProvider == other.GripProvider && ObjectAnimation == other.ObjectAnimation &&
            IsPickingUp == other.IsPickingUp && HasActiveGripProvider == other.HasActiveGripProvider;
    }
}
