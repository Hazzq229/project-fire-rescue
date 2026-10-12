using System.Collections;
using HGame.Objects;
using UnityEngine;

namespace HPlayer
{
    [RequireComponent(typeof(Rigidbody), typeof(LocalPlayerInput))]
    [AddComponentMenu("TA/Adaptive Animation/Object Interaction Controller")]
    public class DoorInteractionController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private ThirdPersonPlayerController playerController;
        [SerializeField] private InteractionController interactionController;
        [SerializeField, Min(0.05f)] private float interactionDistance = 0.45f;
        private LocalPlayerInput localInput;
        [SerializeField] private string idleStatePath = "Base Layer.Idle 0";
        [SerializeField, Min(1f)] private float maxActionSeconds = 8f;
        [SerializeField, Min(.05f)] private float alignmentDuration = .2f;
        [Header("State names AND Trigger names must match")]
        [SerializeField] private string openFrontAnimation = "DoorPush";
        [SerializeField] private string openBackAnimation = "DoorPull";
        [SerializeField] private string closeFrontAnimation = "DoorPull";
        [SerializeField] private string closeBackAnimation = "DoorPush";

        public bool IsInteractingWithDoor { get; private set; }
        // Alias bagi pengguna kode versi lama: true juga ketika menutup.
        public bool IsOpeningDoor => IsInteractingWithDoor;
        // Legacy class/serialized references are retained for all existing door scenes.
        public bool IsInteractingWithObject => IsInteractingWithDoor;
        public ObjectInteractionTarget ActiveTarget => activeDoor;
        private Rigidbody body;
        private ObjectInteractionTarget activeDoor;
        private Transform selectedPoint;
        private bool entered, contactReceived, requested, stateExited;
        private float startedAt, requestedAt;
        private Coroutine alignRoutine;
        private int activeStateHash, activeTriggerHash;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            localInput = GetComponent<LocalPlayerInput>();
            if (!playerController) playerController = GetComponent<ThirdPersonPlayerController>();
            if (!interactionController) interactionController = GetComponent<InteractionController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (IsInteractingWithDoor)
            {
                if (!activeDoor || !activeDoor.isActiveAndEnabled || !animator ||
                    !animator.isActiveAndEnabled || !interactionController ||
                    !interactionController.isActiveAndEnabled || !playerController ||
                    !playerController.isActiveAndEnabled)
                {
                    CancelAction("Door: referensi atau komponen dinonaktifkan.");
                    return;
                }
                if (stateExited && !activeDoor.IsMoving) FinishAction();
                else if (requested && !entered && Time.time - requestedAt > 1.5f)
                    CancelAction("Door: state tidak masuk. Periksa Trigger, transition dan Behaviour.");
                else if (Time.time - startedAt > maxActionSeconds)
                    CancelAction("Door: aksi terlalu lama. Periksa transition keluar dan durasi pintu.");
                return;
            }
            if (localInput && localInput.ObjectInteractPressed)
                TryInteract();
        }

        private string AnimationFor(bool opening, bool front)
        {
            if (opening) return front ? openFrontAnimation : openBackAnimation;
            return front ? closeFrontAnimation : closeBackAnimation;
        }

        public void TryInteract()
        {
            if (!isActiveAndEnabled || IsInteractingWithDoor) return;
            if (!animator || !animator.runtimeAnimatorController || !animator.isActiveAndEnabled ||
                !playerController || !playerController.isActiveAndEnabled ||
                !interactionController || !interactionController.isActiveAndEnabled)
            {
                Debug.LogWarning("Door: isi Animator, Player Controller dan Interaction Controller.", this);
                return;
            }
            if (interactionController.HeldObject || interactionController.IsPickingUp ||
                interactionController.ExternalInteractionLocked || body.isKinematic) return;
            if (animator.IsInTransition(0)) return;

            ObjectInteractionTarget nearest = null;
            Transform pointFound = null;
            bool frontFound = true;
            int pointIndex = 0;
            float best = interactionDistance * interactionDistance;
            foreach (var door in ObjectInteractionTarget.ActiveTargets)
            {
                if (!door || !door.Available) continue;
                for (int side = 0; side < door.InteractionPointCount; side++)
                {
                    bool front = side == 0;
                    Transform point = door.GetInteractionPoint(side);
                    if (!point || Mathf.Abs(point.position.y - body.position.y) > 0.25f) continue;
                    Vector3 delta = point.position - body.position;
                    delta.y = 0f;
                    float distance = delta.sqrMagnitude;
                    if (distance > best) continue;
                    // Kandidat seberang dinding/daun pintu ditolak; tetap coba kandidat lain.
                    if (delta.magnitude > 0.01f && body.SweepTest(delta.normalized,
                        out RaycastHit hit, delta.magnitude, QueryTriggerInteraction.Ignore)) continue;
                    best = distance;
                    nearest = door;
                    pointFound = point;
                    frontFound = front;
                    pointIndex = side;
                }
            }
            if (!nearest) return;
            bool wantsOpen = !nearest.ActiveState;
            string animationName = nearest.AnimationFor(wantsOpen, pointIndex) ?? AnimationFor(wantsOpen, frontFound);
            if (string.IsNullOrWhiteSpace(animationName)) return;
            int stateHash = Animator.StringToHash("Base Layer." + animationName);
            int triggerHash = Animator.StringToHash(animationName);
            bool triggerFound = false;
            foreach (var p in animator.parameters)
                if (p.nameHash == triggerHash && p.type == AnimatorControllerParameterType.Trigger)
                    triggerFound = true;
            if (!triggerFound || !animator.HasState(0, stateHash) ||
                !animator.GetComponent<DoorAnimationEvents>())
            {
                Debug.LogWarning("Door: perlu state Base Layer." + animationName +
                    ", Trigger " + animationName + ", serta DoorAnimationEvents pada Animator.", this);
                return;
            }
            if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHash) return;
            if (!nearest.TryReserve(this, wantsOpen)) return;
            activeDoor = nearest;
            selectedPoint = pointFound;
            activeStateHash = stateHash;
            activeTriggerHash = triggerHash;
            IsInteractingWithDoor = true;
            entered = contactReceived = requested = stateExited = false;
            startedAt = Time.time;
            interactionController.ExternalInteractionLocked = true;
            playerController.SetMovementLocked(true);
            alignRoutine = StartCoroutine(AlignAndStart());
        }

        private IEnumerator AlignAndStart()
        {
            yield return new WaitForFixedUpdate();
            if (!activeDoor || !selectedPoint)
            {
                alignRoutine = null;
                FinishAction();
                yield break;
            }
            Vector3 destination = selectedPoint.position;
            destination.y = body.position.y;
            // Snapshot posisi/rotasi sebelum engsel mulai bergerak.
            // Player tidak mengikuti InteractionPoint yang berputar bersama engsel.
            Vector3 forward = selectedPoint.forward;
            forward.y = 0f;
            Vector3 start = body.position;
            Quaternion rotation = body.rotation;
            Quaternion target = forward.sqrMagnitude > .001f ? Quaternion.LookRotation(forward) : rotation;
            for (float elapsed = 0f; elapsed < alignmentDuration; elapsed += Time.fixedDeltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed + Time.fixedDeltaTime) / alignmentDuration));
                body.MovePosition(Vector3.Lerp(start, destination, t));
                body.MoveRotation(Quaternion.Slerp(rotation, target, t));
                yield return new WaitForFixedUpdate();
            }
            requested = true;
            requestedAt = Time.time;
            animator.ResetTrigger(activeTriggerHash);
            animator.SetTrigger(activeTriggerHash);
            alignRoutine = null;
        }

        public void StateEntered(int stateHash)
        {
            if (IsInteractingWithDoor && requested && stateHash == activeStateHash) entered = true;
        }

        public void ActAtContact()
        {
            if (!IsInteractingWithDoor || !entered || stateExited || contactReceived) return;
            contactReceived = true;
            if (!activeDoor || !activeDoor.PerformReservedAction(this))
                Debug.LogWarning("Object contact diterima tetapi aksi objek ditolak.", this);
        }

        public void StateExited(int stateHash)
        {
            if (!IsInteractingWithDoor || !entered || stateHash != activeStateHash) return;
            stateExited = true;
            if (!contactReceived)
                Debug.LogWarning("Animasi pintu selesai tanpa Door_Contact. Periksa event clip.", this);
            // Tunggu animasi DAN ayunan selesai sebelum membuka kontrol.
            if (!activeDoor || !activeDoor.IsMoving) FinishAction();
        }

        private void CancelAction(string reason)
        {
            Debug.LogWarning(reason, this);
            if (animator && animator.runtimeAnimatorController && animator.isActiveAndEnabled &&
                animator.HasState(0, Animator.StringToHash(idleStatePath)))
                animator.CrossFadeInFixedTime(idleStatePath, 0.1f, 0);
            FinishAction();
        }

        private void FinishAction()
        {
            if (!IsInteractingWithDoor) return;
            IsInteractingWithDoor = false;
            if (alignRoutine != null) StopCoroutine(alignRoutine);
            alignRoutine = null;
            if (activeDoor) activeDoor.Release(this);
            activeDoor = null;
            selectedPoint = null;
            if (interactionController) interactionController.ExternalInteractionLocked = false;
            if (playerController) playerController.SetMovementLocked(false);
            if (animator && animator.runtimeAnimatorController) animator.ResetTrigger(activeTriggerHash);
            entered = requested = stateExited = false;
        }

        private void OnDisable() => FinishAction();
    }
}
