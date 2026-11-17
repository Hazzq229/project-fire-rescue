using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace HGame.Objects
{
    public class DoorInteractable : ObjectInteractionTarget
    {
        public static readonly HashSet<DoorInteractable> ActiveDoors = new HashSet<DoorInteractable>();
        [SerializeField] private Rigidbody hingeBody;
        [Header("Player positions when CLOSED - children of DoorRoot")]
        [FormerlySerializedAs("interactionPoint")]
        [SerializeField] private Transform closedFrontPoint;
        [SerializeField] private Transform closedBackPoint;
        [Header("Swing and timing in SECONDS")]
        [SerializeField, Range(-160f, 160f)] private float openAngle = 90f;
        [SerializeField, Min(0.05f)] private float openDuration = 0.5f;
        [SerializeField, Min(0.05f)] private float closeDuration = 0.5f;
        [SerializeField, Min(.1f)] private float interactionReach = 1.1f;
        [SerializeField, Min(.1f)] private float closingClearance = .4f;

        public float InteractionReach => interactionReach;

        public float SelectionDistanceSquared(Vector3 position, int side)
        {
            var point = GetInteractionPoint(side);
            if (!point || (side == 0) != IsFrontSide(position)) return float.PositiveInfinity;
            Vector3 near = point.position - position;
            near.y = 0f;
            float best = near.sqrMagnitude;
            foreach (var shape in hingeBody.GetComponentsInChildren<Collider>())
            {
                if (!shape.enabled || shape.isTrigger) continue;
                Vector3 delta = shape.ClosestPoint(position) - position;
                if (Mathf.Abs(delta.y) > .75f) continue;
                delta.y = 0f;
                best = Mathf.Min(best, delta.sqrMagnitude);
            }
            return best;
        }

        public bool IsOpen { get; private set; }
        private bool moving;
        public override bool IsMoving => moving;
        public override bool ActiveState => IsOpen;
        public override int InteractionPointCount => 2;
        public override Transform GetInteractionPoint(int index) => GetInteractionPoint(index == 0);
        public override bool Available => isActiveAndEnabled && hingeBody && !IsMoving && !owner;
        private Quaternion closedRotation;
        private Quaternion openedRotation;
        private Quaternion fromRotation;
        private Quaternion targetRotation;
        private bool targetOpen;
        private bool reservedOpen;
        private bool reservedFront, openedFromFront;
        private Transform oppositeClosePoint;
        private Vector3 localLeafTip;
        private float leafRadius;
        private float elapsed;
        private float duration;
        private MonoBehaviour owner;

        private void Awake()
        {
            if (!hingeBody || !closedFrontPoint || !closedBackPoint)
            {
                Debug.LogError("Door: isi Hinge Body dan titik CLOSED FRONT/BACK.", this);
                enabled = false;
                return;
            }
            hingeBody.isKinematic = true;
            hingeBody.useGravity = false;
            hingeBody.interpolation = RigidbodyInterpolation.Interpolate;
            // Scene dimulai dengan pintu tertutup; DoorRoot tidak dipindahkan saat runtime.
            closedRotation = hingeBody.rotation;
            foreach (var shape in hingeBody.GetComponentsInChildren<BoxCollider>())
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = shape.center + Vector3.Scale(shape.size * .5f,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 offset = shape.transform.TransformPoint(corner) - hingeBody.position;
                    offset.y = 0f;
                    if (offset.magnitude <= leafRadius) continue;
                    leafRadius = offset.magnitude;
                    localLeafTip = Quaternion.Inverse(closedRotation) * offset;
                }
            if (leafRadius < .01f)
            {
                localLeafTip = Quaternion.Inverse(closedRotation) * (closedFrontPoint.position - hingeBody.position);
                localLeafTip.y = 0f;
                leafRadius = Mathf.Max(.5f, localLeafTip.magnitude);
            }
            oppositeClosePoint = new GameObject("OppositeSideClosePoint").transform;
            oppositeClosePoint.SetParent(transform, false);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (hingeBody && closedFrontPoint && closedBackPoint)
                ActiveDoors.Add(this);
        }

        public Transform GetInteractionPoint(bool front)
        {
            if (IsOpen && front != openedFromFront && oppositeClosePoint)
            {
                // The opposite operator stands beyond the open leaf's tip, outside its closing arc.
                Vector3 radial = hingeBody.rotation * localLeafTip;
                radial.y = 0f;
                radial.Normalize();
                Vector3 position = hingeBody.position + radial * (leafRadius + closingClearance);
                position.y = (front ? closedFrontPoint : closedBackPoint).position.y;
                oppositeClosePoint.SetPositionAndRotation(position, Quaternion.LookRotation(-radial));
                return oppositeClosePoint;
            }
            return front ? closedFrontPoint : closedBackPoint;
        }

        private bool IsFrontSide(Vector3 position)
        {
            Vector3 axis = closedBackPoint.position - closedFrontPoint.position;
            axis.y = 0f;
            Vector3 delta = position - (closedFrontPoint.position + closedBackPoint.position) * .5f;
            delta.y = 0f;
            return Vector3.Dot(delta, axis) <= 0f;
        }

        public override string AnimationFor(bool nextState, int pointIndex) =>
            nextState || (pointIndex == 0) != openedFromFront ? "DoorPush" : "DoorPull";

        public bool TryGetClosingWaypoint(Vector3 from, out Vector3 waypoint)
        {
            waypoint = from;
            bool front = IsFrontSide(from);
            if (!IsOpen || front == openedFromFront) return false;
            Vector3 radial = hingeBody.rotation * localLeafTip;
            radial.y = 0f;
            radial.Normalize();
            Vector3 normal = Vector3.Cross(Vector3.up, radial);
            float side = Vector3.Dot(from - hingeBody.position, normal) >= 0f ? 1f : -1f;
            waypoint = GetInteractionPoint(front).position + normal * side * (closingClearance + .2f);
            waypoint.y = from.y;
            return true;
        }

        public override bool TryReserve(MonoBehaviour requester, bool wantsOpen)
        {
            if (!requester || !Available || wantsOpen == IsOpen) return false;
            owner = requester;
            reservedOpen = wantsOpen;
            var body = requester.GetComponent<Rigidbody>();
            reservedFront = IsFrontSide(body ? body.position : requester.transform.position);
            return true;
        }

        public override void Release(MonoBehaviour requester)
        {
            if (owner == requester) owner = null;
        }

        public override bool PerformReservedAction(MonoBehaviour requester)
        {
            if (!isActiveAndEnabled || !hingeBody || !owner || owner != requester || IsMoving ||
                reservedOpen == IsOpen) return false;
            targetOpen = reservedOpen;
            if (targetOpen)
            {
                openedFromFront = reservedFront;
                Vector3 actor = (reservedFront ? closedFrontPoint : closedBackPoint).position - hingeBody.position;
                actor.y = 0f;
                Vector3 tip = closedRotation * localLeafTip;
                float angle = Mathf.Abs(openAngle);
                float positive = (Quaternion.AngleAxis(angle, Vector3.up) * tip - actor).sqrMagnitude;
                float negative = (Quaternion.AngleAxis(-angle, Vector3.up) * tip - actor).sqrMagnitude;
                openedRotation = Quaternion.AngleAxis(positive >= negative ? angle : -angle, Vector3.up) * closedRotation;
            }
            fromRotation = hingeBody.rotation;
            targetRotation = targetOpen ? openedRotation : closedRotation;
            duration = Mathf.Max(0.05f, targetOpen ? openDuration : closeDuration);
            elapsed = 0f;
            moving = true;
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
                moving = false;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ActiveDoors.Remove(this);
            owner = null;
            // Jika terhenti di tengah, kembalikan ke keadaan terakhir yang selesai.
            if (IsMoving && hingeBody) hingeBody.rotation = IsOpen ? openedRotation : closedRotation;
            moving = false;
        }

        private void OnDrawGizmosSelected()
        {
            DrawPoint(closedFrontPoint, Color.green);
            DrawPoint(closedBackPoint, Color.cyan);
            DrawPoint(oppositeClosePoint, Color.yellow);
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
