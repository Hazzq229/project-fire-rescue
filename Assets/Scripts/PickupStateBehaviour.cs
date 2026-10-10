using UnityEngine;

namespace HPlayer
{
    // Tambahkan lewat Add Behaviour pada STATE Pickup, bukan Add Component.
    public class PickupStateBehaviour : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator,
            AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (layerIndex != 0) return;
            var receiver = animator.GetComponent<PickupAnimationEvents>();
            if (receiver) receiver.NotifyPickupEntered();
        }

        public override void OnStateExit(Animator animator,
            AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (layerIndex != 0) return;
            var receiver = animator.GetComponent<PickupAnimationEvents>();
            if (receiver) receiver.NotifyPickupExited();
        }
    }
}
