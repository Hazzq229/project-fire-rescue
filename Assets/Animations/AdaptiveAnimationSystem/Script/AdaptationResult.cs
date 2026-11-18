namespace TA.AdaptiveAnimation
{
    /// <summary>Routes the shared inputs to existing pose, grip and load implementations.</summary>
    public readonly struct AdaptationResult
    {
        public InteractionContext Context { get; }
        public GripPointProvider GripProvider { get; }
        public bool UsesHosePose { get; }
        public bool HasLoad => Context.HasWeight;
        public float EffectiveWeightKg => Context.EffectiveWeightKg;
        /// <summary>Diagnostic route hint; interaction controllers own animation commands/events.</summary>
        public string AnimationStateHint { get; }
        // Compatibility for existing diagnostics and tests. This is not an Animator command.
        public string BaseAnimationState => AnimationStateHint;

        internal AdaptationResult(InteractionContext context, GripPointProvider provider,
            bool usesHosePose, string animationState)
        {
            Context = context; GripProvider = provider;
            UsesHosePose = usesHosePose; AnimationStateHint = animationState;
        }
    }
}
