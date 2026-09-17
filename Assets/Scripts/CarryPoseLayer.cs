using UnityEngine;

namespace HPlayer
{
    // Tambahkan pada root Player. Isi referensi Animator model secara manual.
    [DefaultExecutionOrder(100)]
    public class CarryPoseLayer : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private InteractionController interactionController;
        [SerializeField] private string carryLayerName = "Carry Layer";
        [SerializeField, Min(0f)] private float blendDuration = 0.12f;

        private int carryLayerIndex = -1;
        private bool initialized;
        private static readonly int PickupStateId = Animator.StringToHash("Base Layer.Pickup");

        private void Awake()
        {
            if (!interactionController)
                interactionController = GetComponent<InteractionController>();
        }

        private void Start()
        {
            if (!animator || !animator.runtimeAnimatorController || !interactionController)
            {
                Debug.LogError("CarryPoseLayer: isi Animator dan Interaction Controller.", this);
                enabled = false;
                return;
            }

            carryLayerIndex = animator.GetLayerIndex(carryLayerName);
            if (carryLayerIndex <= 0)
            {
                Debug.LogError("CarryPoseLayer: buat layer tambahan bernama Carry Layer. " +
                    "Jangan gunakan Base Layer untuk pose carry.", this);
                enabled = false;
                return;
            }

            int stateId = Animator.StringToHash(carryLayerName + ".CarryHold");
            if (!animator.HasState(carryLayerIndex, stateId))
            {
                Debug.LogError("CarryPoseLayer: buat state CarryHold langsung pada Carry Layer.", this);
                enabled = false;
                return;
            }

            animator.SetLayerWeight(carryLayerIndex, 0f);
            // Pose pertama dari clip CarryHold; state Speed harus 0.
            animator.Play(stateId, carryLayerIndex, 0f);
            initialized = true;
        }

        private void Update()
        {
            if (!initialized || !animator || !animator.isActiveAndEnabled) return;

            bool holding = interactionController && interactionController.isActiveAndEnabled &&
                           interactionController.HeldObject != null;
            bool pickupFinished = interactionController && !interactionController.IsPickingUp;

            // Mulai blend saat Pickup bertransisi keluar agar lengan tidak turun
            // ke Idle lebih dulu. Event Grab tetap hanya milik clip Pickup.
            bool leavingPickup = false;
            if (animator.IsInTransition(0))
            {
                leavingPickup = animator.GetCurrentAnimatorStateInfo(0).fullPathHash == PickupStateId &&
                                animator.GetNextAnimatorStateInfo(0).fullPathHash != PickupStateId;
            }

            float targetWeight = holding && (pickupFinished || leavingPickup) ? 1f : 0f;
            float weight = animator.GetLayerWeight(carryLayerIndex);
            float nextWeight = blendDuration <= 0f ? targetWeight :
                Mathf.MoveTowards(weight, targetWeight, Time.deltaTime / blendDuration);
            animator.SetLayerWeight(carryLayerIndex, nextWeight);
        }

        private void OnDisable()
        {
            if (initialized && animator && animator.runtimeAnimatorController &&
                carryLayerIndex > 0 && carryLayerIndex < animator.layerCount)
                animator.SetLayerWeight(carryLayerIndex, 0f);
        }
    }
}