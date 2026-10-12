using HInteractions;
using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    // Each station is exclusive. Two separate stations express roles without binding them to P1/P2.
    [DisallowMultipleComponent]
    public sealed class CooperativeHoseGrip : Liftable
    {
        [SerializeField] private CooperativeFireHose hose;
        [SerializeField] private CarryRole role = CarryRole.Front;
        public CooperativeFireHose Hose => hose;
        public CarryRole Role => role;
        public InteractionController Holder => Holders.Count == 1 ? Holders[0] as InteractionController : null;
        public override float SpeedPenalty => .5f;
        protected override void Awake() { base.Awake(); maxHolders = 1; }
        public override void PickUp(IObjectHolder holder, int layer)
        {
            var player = holder as InteractionController;
            if (!hose || !hose.isActiveAndEnabled || !player || player.HeldObject ||
                player.ExternalInteractionLocked || !CanBePickedUp() ||
                player.GetComponent<ThirdPersonPlayerController>().IsKnockedBack) return;
            base.PickUp(holder, gameObject.layer);
            if (Holder == player) hose.Joined(this);
        }
        // The hose coordinator owns physics and restores the nozzle once both stations are released.
        protected override void OnFirstPickup() { }
        protected override void OnAllDropped() { }
        public override void Drop(IObjectHolder holder)
        {
            if (!Holders.Contains(holder)) return;
            if (holder is InteractionController player)
                player.GetComponent<ThirdPersonPlayerController>().CooperativeHose = null;
            base.Drop(holder);
            if (hose) hose.ReleaseTeam();
        }
        private void OnDisable()
        {
            if (Application.isPlaying && hose) hose.ReleaseTeam();
        }
    }
}
