namespace TA.AdaptiveAnimation
{
    /// <summary>
    /// Pure routing policy: reads the supplied snapshot and returns pose/grip/load selections.
    /// Does not read live controllers, mutate ownership, drive Animator or apply rig transforms.
    /// World grip positions are deliberately queried by the IK consumer after reach fitting.
    /// </summary>
    public static class ProceduralLayerProcessor
    {
        public static AdaptationResult Process(InteractionContext context)
        {
            var grip = context.HasActiveGripProvider && context.HeldObject
                ? context.GripProvider : null;
            bool hose = context.HeldObject is CooperativeHoseGrip;
            string animation = string.Empty;
            if (context.Source)
            {
                if (context.ObjectTarget) animation = string.IsNullOrEmpty(context.ObjectAnimation)
                    ? string.Empty : "Base Layer." + context.ObjectAnimation;
                else if (context.Phase == InteractionPhase.Preparing || context.Phase == InteractionPhase.Placing ||
                         context.IsPickingUp) animation = "Base Layer.Pickup";
                else if (context.HeldObject) animation = hose ? "Hose Layer.HoseHold" : "Carry Layer.CarryHold";
            }
            return new AdaptationResult(context, grip, hose, animation);
        }
    }
}
