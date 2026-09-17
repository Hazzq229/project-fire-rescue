using UnityEngine;

namespace HPlayer
{
    // Pasang pada GameObject yang SAMA dengan Animator.
    [RequireComponent(typeof(Animator))]
    public class PickupAnimationEvents : MonoBehaviour
    {
        [SerializeField] private InteractionController interactionController;

        private void Awake()
        {
            if (!interactionController)
                interactionController = GetComponentInParent<InteractionController>();
        }

        // Animation Event pada frame tangan menyentuh objek.
        public void Pickup_Grab()
        {
            if (interactionController && interactionController.isActiveAndEnabled)
                interactionController.GrabPendingObject();
        }

        public void NotifyPickupEntered()
        {
            if (interactionController && interactionController.isActiveAndEnabled)
                interactionController.NotifyPickupEntered();
        }

        public void NotifyPickupExited()
        {
            if (interactionController)
                interactionController.NotifyPickupExited();
        }
    }
}
