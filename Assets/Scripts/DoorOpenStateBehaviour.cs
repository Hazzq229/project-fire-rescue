using UnityEngine;

namespace HPlayer
{
    // Nama dipertahankan agar referensi lama tidak hilang.
    // Add Behaviour pada KEDUA state DoorPush dan DoorPull.
    public class DoorOpenStateBehaviour : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layerIndex)
        {
            if (layerIndex != 0) return;
            var receiver = animator.GetComponent<DoorAnimationEvents>();
            if (receiver) receiver.StateEntered(info.fullPathHash);
        }
        public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layerIndex)
        {
            if (layerIndex != 0) return;
            var receiver = animator.GetComponent<DoorAnimationEvents>();
            if (receiver) receiver.StateExited(info.fullPathHash);
        }
    }
}
