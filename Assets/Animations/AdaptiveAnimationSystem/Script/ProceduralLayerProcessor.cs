namespace TA.AdaptiveAnimation
{
    /// <summary>
    /// Keeps the existing calibrated providers/curves as the implementation of adaptation.
    /// World grip positions are deliberately queried by the IK consumer after reach fitting.
    /// </summary>
    public static class ProceduralLayerProcessor
    {
        public static AdaptationResult Process(InteractionContext context)
        {
            var grip = context.HeldObject && context.GripProvider && context.GripProvider.isActiveAndEnabled
                ? context.GripProvider : null;
            bool hose = context.HeldObject is CooperativeHoseGrip;
            string animation = string.Empty;
            if (context.Source)
            {
                if (context.ObjectTarget) animation = string.IsNullOrEmpty(context.ObjectAnimation)
                    ? string.Empty : "Base Layer." + context.ObjectAnimation;
                else if (context.Phase == InteractionPhase.Preparing || context.Phase == InteractionPhase.Placing ||
                         (context.Actor && context.Actor.IsPickingUp)) animation = "Base Layer.Pickup";
                else if (context.HeldObject) animation = hose ? "Hose Layer.HoseHold" : "Carry Layer.CarryHold";
            }
            return new AdaptationResult(context, grip, hose, animation);
        }
    }
}
