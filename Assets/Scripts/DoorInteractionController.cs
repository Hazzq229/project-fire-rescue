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
        public string ActiveAnimationName { get; private set; }
        private Rigidbody body;
        private CapsuleCollider capsule;
        private ObjectInteractionTarget activeDoor;
        private Transform selectedPoint;
        private bool entered, contactReceived, requested, stateExited;
        private float startedAt, requestedAt;
        private Coroutine alignRoutine;
        private int activeStateHash, activeTriggerHash;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
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

            ObjectInteractionTarget nearest = null;
            Transform pointFound = null;
            bool frontFound = true;
            int pointIndex = 0;
            float best = float.PositiveInfinity;
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
                    var hingedDoor = door as DoorInteractable;
                    var lamp = door as LampInteractable;
                    float reach = hingedDoor ? hingedDoor.InteractionReach : lamp ? lamp.InteractionReach : interactionDistance;
                    if (hingedDoor) distance = hingedDoor.SelectionDistanceSquared(body.position, side);
                    if (distance > reach * reach || distance > best) continue;
                    Vector3 waypoint;
                    bool blocked = hingedDoor && hingedDoor.TryGetClosingWaypoint(body.position, out waypoint)
                        ? ApproachBlocked(body.position, waypoint, door) || ApproachBlocked(waypoint, point.position, door)
                        : ApproachBlocked(body.position, point.position, door);
                    if (blocked) continue;
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
            ActiveAnimationName = animationName;
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

        private bool ApproachBlocked(Vector3 from, Vector3 to, ObjectInteractionTarget target)
        {
            to.y = from.y;
            Vector3 delta = to - from;
            if (delta.magnitude <= .01f) return false;
            if (capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float halfSegment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
                Vector3 center = from + capsule.transform.TransformVector(capsule.center);
                foreach (var hit in Physics.CapsuleCastAll(center + Vector3.up * halfSegment,
                    center - Vector3.up * halfSegment, radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) continue;
                    if (target is DoorInteractable && hit.collider.transform.IsChildOf(target.transform)) continue;
                    if (Vector3.Dot(hit.normal, delta.normalized) < -.01f) return true;
                }
            }
            // Also detect thin walls touching the starting capsule.
            foreach (var hit in Physics.RaycastAll(from + Vector3.up * .3f,
                delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (target is DoorInteractable && hit.collider.transform.IsChildOf(target.transform)) continue;
                return true;
            }
            return false;
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
            var hingedDoor = activeDoor as DoorInteractable;
            Vector3 waypoint;
            Vector3[] path = hingedDoor && hingedDoor.TryGetClosingWaypoint(start, out waypoint)
                ? new[] { waypoint, destination } : new[] { destination };
            foreach (var position in path)
            {
                start = body.position;
                rotation = body.rotation;
                for (float elapsed = 0f; elapsed < alignmentDuration; elapsed += Time.fixedDeltaTime)
                {
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed + Time.fixedDeltaTime) / alignmentDuration));
                    body.MovePosition(Vector3.Lerp(start, position, t));
                    body.MoveRotation(Quaternion.Slerp(rotation, target, t));
                    yield return new WaitForFixedUpdate();
                }
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
            ActiveAnimationName = null;
            selectedPoint = null;
            if (interactionController) interactionController.ExternalInteractionLocked = false;
            if (playerController) playerController.SetMovementLocked(false);
            if (animator && animator.runtimeAnimatorController) animator.ResetTrigger(activeTriggerHash);
            entered = requested = stateExited = false;
        }

        private void OnDisable() => FinishAction();
    }
}
