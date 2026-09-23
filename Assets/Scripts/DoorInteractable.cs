using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace HGame.Objects
{
    public class DoorInteractable : MonoBehaviour
    {
        public static readonly HashSet<DoorInteractable> ActiveDoors = new HashSet<DoorInteractable>();
        [SerializeField] private Rigidbody hingeBody;
        [Header("Player positions when CLOSED - children of DoorRoot")]
        [FormerlySerializedAs("interactionPoint")]
        [SerializeField] private Transform closedFrontPoint;
        [SerializeField] private Transform closedBackPoint;
        [Header("Player positions when OPEN - children of DoorHinge")]
        [SerializeField] private Transform openFrontPoint;
        [SerializeField] private Transform openBackPoint;
        [Header("Swing and timing in SECONDS")]
        [SerializeField, Range(-160f, 160f)] private float openAngle = 90f;
        [SerializeField, Min(0.05f)] private float openDuration = 0.5f;
        [SerializeField, Min(0.05f)] private float closeDuration = 0.5f;

        public bool IsOpen { get; private set; }
        public bool IsMoving { get; private set; }
        public bool Available => isActiveAndEnabled && hingeBody && !IsMoving && !owner;
        private Quaternion closedRotation;
        private Quaternion openedRotation;
        private Quaternion fromRotation;
        private Quaternion targetRotation;
        private bool targetOpen;
        private bool reservedOpen;
        private float elapsed;
        private float duration;
        private MonoBehaviour owner;

        private void Awake()
        {
            if (!hingeBody || !closedFrontPoint || !closedBackPoint || !openFrontPoint || !openBackPoint)
            {
                Debug.LogError("Door: isi Hinge Body dan keempat titik CLOSED/OPEN FRONT/BACK.", this);
                enabled = false;
                return;
            }
            hingeBody.isKinematic = true;
            hingeBody.useGravity = false;
            hingeBody.interpolation = RigidbodyInterpolation.Interpolate;
            // Scene dimulai dengan pintu tertutup; DoorRoot tidak dipindahkan saat runtime.
            closedRotation = hingeBody.rotation;
            openedRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
        }

        private void OnEnable()
        {
            if (hingeBody && closedFrontPoint && closedBackPoint && openFrontPoint && openBackPoint)
                ActiveDoors.Add(this);
        }

        public Transform GetInteractionPoint(bool front)
        {
            if (IsOpen) return front ? openFrontPoint : openBackPoint;
            return front ? closedFrontPoint : closedBackPoint;
        }

        public bool TryReserve(MonoBehaviour requester, bool wantsOpen)
        {
            if (!requester || !Available || wantsOpen == IsOpen) return false;
            owner = requester;
            reservedOpen = wantsOpen;
            return true;
        }

        public void Release(MonoBehaviour requester)
        {
            if (owner == requester) owner = null;
        }

        public bool PerformReservedAction(MonoBehaviour requester)
        {
            if (!isActiveAndEnabled || !hingeBody || owner != requester || IsMoving ||
                reservedOpen == IsOpen) return false;
            targetOpen = reservedOpen;
            fromRotation = hingeBody.rotation;
            targetRotation = targetOpen ? openedRotation : closedRotation;
            duration = Mathf.Max(0.05f, targetOpen ? openDuration : closeDuration);
            elapsed = 0f;
            IsMoving = true;
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsMoving || !hingeBody) return;
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            hingeBody.MoveRotation(Quaternion.Slerp(fromRotation, targetRotation,
                Mathf.SmoothStep(0f, 1f, t)));
            if (t >= 1f)
            {
                IsOpen = targetOpen;
                IsMoving = false;
            }
        }

        private void OnDisable()
        {
            ActiveDoors.Remove(this);
            owner = null;
            // Jika terhenti di tengah, kembalikan ke keadaan terakhir yang selesai.
            if (IsMoving && hingeBody) hingeBody.rotation = IsOpen ? openedRotation : closedRotation;
            IsMoving = false;
        }

        private void OnDrawGizmosSelected()
        {
            DrawPoint(closedFrontPoint, Color.green);
            DrawPoint(closedBackPoint, Color.cyan);
            DrawPoint(openFrontPoint, Color.yellow);
            DrawPoint(openBackPoint, Color.magenta);
        }

        private static void DrawPoint(Transform point, Color color)
        {
            if (!point) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(point.position, 0.45f);
            Gizmos.DrawRay(point.position, point.forward * 0.5f);
        }
    }
}
