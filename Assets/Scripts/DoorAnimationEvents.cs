using UnityEngine;

namespace HPlayer
{
    public class DoorAnimationEvents : MonoBehaviour
    {
        [SerializeField] private DoorInteractionController doorController;
        private void Awake()
        {
            if (!doorController) doorController = GetComponentInParent<DoorInteractionController>();
        }
        // One contact event per clip; the reserved object decides the action.
        public void Object_Contact()
        {
            if (doorController && doorController.isActiveAndEnabled) doorController.ActAtContact();
        }
        public void Door_Contact() => Object_Contact();
        // Alias untuk event lama. Jangan pasang alias DAN Door_Contact pada satu clip.
        public void Door_Open() => Door_Contact();
        public void Door_Close() => Door_Contact();
        public void StateEntered(int stateHash)
        {
            if (doorController && doorController.isActiveAndEnabled) doorController.StateEntered(stateHash);
        }
        public void StateExited(int stateHash)
        {
            if (doorController) doorController.StateExited(stateHash);
        }
    }
}
