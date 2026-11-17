namespace TA.AdaptiveAnimation
{
    public enum AdaptiveObjectType { None, Unknown, Box, Cylinder, Survivor, Hose }
    public enum AdaptiveInteractionType { None, CarryObject, CarrySurvivor, ObjectInteraction, FireHoseInteraction }
    public enum InteractionPhase { None, Preparing, Carrying, Placing, ObjectInteraction }
}
