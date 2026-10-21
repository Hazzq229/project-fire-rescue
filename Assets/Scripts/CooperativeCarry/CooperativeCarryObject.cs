using System.Collections.Generic;
using HInteractions;
using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public enum CarryRole { None, Front, Back }
    public enum CooperativeCarryState { Idle, WaitingForPartner, Aligning, Carrying }

    /// <summary>Opt-in, two-person carry with object-relative roles and one physics owner.</summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(BoxCollider), typeof(CarryAnimationLoad))]
    public sealed class CooperativeCarryObject : Liftable
    {
        [SerializeField] private CarryWeightSpeedProfile speedProfile;
        [Tooltip("Distance from the end of the collider to the player's root, in metres.")]
        [SerializeField, Min(.1f)] private float stanceGap = .12f;
        [SerializeField, Min(.1f)] private float joinDistance = 1.3f;
        [SerializeField] private float carryHeightAbovePlayer = .399f;
        [SerializeField] private float gripHeightAboveCenter;
        private bool dynamicOnRelease;
        public void ConfigureProp(CarryWeightSpeedProfile profile, float gripHeight)
        {
            speedProfile = profile;
            gripHeightAboveCenter = gripHeight;
            carryHeightAbovePlayer = .399f - gripHeight;
            dynamicOnRelease = true;
        }
        [SerializeField, Min(.1f)] private float alignmentSpeed = 1.5f;
        [Header("Solo heavy carry")]
        [SerializeField, Range(.01f, .3f)] private float soloSpeedMultiplier = .12f;
        [SerializeField, Range(0f, 35f)] private float maximumSoloTilt = 25f;
        [SerializeField, Min(1f)] private float levelingDegreesPerSecond = 40f;
        [SerializeField] private CooperativeCarryState state;
        [SerializeField] private InteractionController frontHolder;
        [SerializeField] private InteractionController backHolder;
        [SerializeField] private Vector3 groupVelocity;

        [Header("Cooperative Rotation Settings")]
        [SerializeField, Min(10f)] private float turnSpeed = 90f;
        private float currentYaw;

        private BoxCollider shape;
        private bool savedKinematic, savedGravity, releasing;
        private RigidbodyInterpolation savedInterpolation;
        private CollisionDetectionMode savedCollisionMode;
        private RigidbodyConstraints savedConstraints;
        private Quaternion levelRotation;
        private Vector3 soloPivot;
        private CarryRole firstRole;
        private Vector3 frontEffort, backEffort;
        private readonly List<(Collider a, Collider b)> ignoredCollisions = new();

        public CooperativeCarryState State => state;
        public Vector3 GroupVelocity => groupVelocity;
        public float LoadPerPlayerKg => Holders.Count == 0 ? 0f :
            GetComponent<CarryAnimationLoad>().WeightKg / Holders.Count;
        public bool HasConflictingInput => frontEffort.sqrMagnitude > .001f || backEffort.sqrMagnitude > .001f;
        public Vector3 GetEffortDirection(InteractionController player) =>
            player == frontHolder ? frontEffort : player == backHolder ? backEffort : Vector3.zero;
        public override bool ForceFaceObject => true;
        public override float SpeedPenalty
        {
            get
            {
                float multiplier = 1f;
                if (speedProfile) speedProfile.TryEvaluateMultiplier(LoadPerPlayerKg, out multiplier);
                if (Holders.Count == 1) multiplier *= soloSpeedMultiplier;
                return 1f - multiplier;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            maxHolders = 2;
            shape = GetComponent<BoxCollider>();
            state = CooperativeCarryState.Idle;
        }

        public CarryRole GetRole(InteractionController player) => !player ? CarryRole.None :
            player == frontHolder ? CarryRole.Front : player == backHolder ? CarryRole.Back : CarryRole.None;

        public CarryRole RoleAt(Vector3 position)
        {
            var box = shape ? shape : GetComponent<BoxCollider>();
            Vector3 center = PhysicsPoint(box.center);
            Vector3 forward = Rigidbody ? Rigidbody.rotation * Vector3.forward : transform.forward;
            return Vector3.Dot(position - center, forward) >= 0f ? CarryRole.Front : CarryRole.Back;
        }

        private Vector3 PhysicsPoint(Vector3 local) => Rigidbody ? Rigidbody.position +
            Rigidbody.rotation * Vector3.Scale(local, transform.lossyScale) : transform.TransformPoint(local);

        public Vector3 GetStance(CarryRole role, float playerY)
        {
            var box = shape ? shape : GetComponent<BoxCollider>();
            float sign = role == CarryRole.Front ? 1f : -1f;
            Vector3 point = PhysicsPoint(box.center + Vector3.forward * sign * box.size.z * .5f)
                + transform.forward * sign * stanceGap;
            point.y = playerY;
            return point;
        }

        public bool CanJoin(InteractionController player)
        {
            if (!player || !isActiveAndEnabled || player.HeldObject || Holders.Count >= 2) return false;
            Vector3 position = player.GetComponent<Rigidbody>().position;
            CarryRole role = RoleAt(position);
            if (role == CarryRole.Front ? frontHolder : backHolder) return false;
            return Vector3.Distance(position, GetStance(role, position.y)) <= joinDistance;
        }

        public override Vector3 GetGrabPoint(Vector3 handPosition) =>
            GetComponent<BoxCollider>().ClosestPoint(handPosition);

        public override void PickUp(IObjectHolder holder, int layer)
        {
            var player = holder as InteractionController;
            if (!CanJoin(player)) return;
            var role = RoleAt(player.GetComponent<Rigidbody>().position);
            if (role == CarryRole.Front) frontHolder = player; else backHolder = player;
            // Keep the interaction layer: the second player's trigger must still detect this object.
            base.PickUp(holder, gameObject.layer);
            player.GetComponent<ThirdPersonPlayerController>().CooperativeCarry = this;
            var rb = player.GetComponent<Rigidbody>();
            rb.velocity = Vector3.zero;
            foreach (var a in GetComponentsInChildren<Collider>())
                foreach (var b in player.GetComponentsInChildren<Collider>())
                    if (!a.isTrigger && !b.isTrigger && !Physics.GetIgnoreCollision(a, b))
                    {
                        Physics.IgnoreCollision(a, b, true);
                        ignoredCollisions.Add((a, b));
                    }
        }

        protected override void OnFirstPickup()
        {
            savedKinematic = Rigidbody.isKinematic;
            savedGravity = Rigidbody.useGravity;
            savedInterpolation = Rigidbody.interpolation;
            savedCollisionMode = Rigidbody.collisionDetectionMode;
            savedConstraints = Rigidbody.constraints;

            currentYaw = Rigidbody.rotation.eulerAngles.y;
            levelRotation = Quaternion.Euler(0f, currentYaw, 0f);

            firstRole = frontHolder ? CarryRole.Front : CarryRole.Back;
            if (!Rigidbody.isKinematic) { Rigidbody.velocity = Vector3.zero; Rigidbody.angularVelocity = Vector3.zero; }
            Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Rigidbody.isKinematic = true;
            Rigidbody.useGravity = false;
            Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            Rigidbody.constraints = RigidbodyConstraints.None;
            BeginSoloTilt();
        }

        private Vector3 EndLocal(CarryRole role) => shape.center + Vector3.up * gripHeightAboveCenter + Vector3.forward *
            (role == CarryRole.Front ? 1f : -1f) * shape.size.z * .5f;

        private void BeginSoloTilt()
        {
            var player = frontHolder ? frontHolder : backHolder;
            float sign = firstRole == CarryRole.Front ? 1f : -1f;
            Vector3 localPivot = shape.center + new Vector3(0f, -shape.size.y * .5f, -sign * shape.size.z * .5f);
            soloPivot = PhysicsPoint(localPivot);
            // The unheld bottom edge stays on its support plane while the held end rises to hand height.
            soloPivot.y = shape.bounds.min.y;
            float length = shape.size.z * transform.lossyScale.z;
            float halfHeight = (shape.size.y * .5f + gripHeightAboveCenter) * transform.lossyScale.y;
            float rise = player.GetComponent<Rigidbody>().position.y + carryHeightAbovePlayer + gripHeightAboveCenter - soloPivot.y;
            float radius = Mathf.Sqrt(length * length + halfHeight * halfHeight);
            float angle = (Mathf.Asin(Mathf.Clamp(rise / Mathf.Max(.001f, radius), -1f, 1f)) -
                Mathf.Atan2(halfHeight, length)) * Mathf.Rad2Deg;
            Quaternion tilt = levelRotation * Quaternion.Euler(-sign * Mathf.Clamp(angle, 0f, maximumSoloTilt), 0f, 0f);
            Rigidbody.rotation = tilt;
            Rigidbody.position = soloPivot - tilt * Vector3.Scale(localPivot, transform.lossyScale);
        }

        protected override void OnAnyPickup()
        {
            state = Holders.Count == 2 ? CooperativeCarryState.Aligning : CooperativeCarryState.WaitingForPartner;
            if (Holders.Count != 2) return;
            // Attach at the second grab event, not after the rest of the pickup clip.
            Vector3 end = PhysicsPoint(EndLocal(firstRole));
            Vector3 position = end - levelRotation * Vector3.Scale(EndLocal(firstRole), transform.lossyScale);
            position.y = (frontHolder.GetComponent<Rigidbody>().position.y +
                backHolder.GetComponent<Rigidbody>().position.y) * .5f + carryHeightAbovePlayer;
            Rigidbody.rotation = levelRotation;
            Rigidbody.position = position;
        }

        public override void Drop(IObjectHolder holder)
        {
            if (!Holders.Contains(holder)) return;
            var player = holder as InteractionController;
            if (player)
            {
                player.GetComponent<ThirdPersonPlayerController>().CooperativeCarry = null;
                player.GetComponent<Rigidbody>().velocity = Vector3.zero;
            }
            if (player == frontHolder) frontHolder = null;
            if (player == backHolder) backHolder = null;
            base.Drop(holder);
            if (releasing) return;
            // A release by either person puts the object down for both; no abandoned joint/role.
            releasing = true;
            try
            {
                var remaining = Holders.ToArray();
                foreach (var other in remaining)
                    if (other is InteractionController partner && partner) partner.ReleaseHeldObject();
            }
            finally { releasing = false; }
        }

        protected override void OnAllDropped()
        {
            foreach (var pair in ignoredCollisions)
                if (pair.a && pair.b) Physics.IgnoreCollision(pair.a, pair.b, false);
            ignoredCollisions.Clear();
            Rigidbody.isKinematic = dynamicOnRelease ? false : savedKinematic;
            Rigidbody.useGravity = dynamicOnRelease || savedGravity;
            Rigidbody.interpolation = savedInterpolation;
            Rigidbody.collisionDetectionMode = savedCollisionMode;
            // Release into a stable level pose; restore the object's original physics configuration.
            Rigidbody.rotation = levelRotation;
            Rigidbody.constraints = savedConstraints;
            state = CooperativeCarryState.Idle;
            groupVelocity = Vector3.zero;
            frontEffort = backEffort = Vector3.zero;
        }

        public static Vector2 CombineInput(Vector2 front, Vector2 back)
        {
            front = Vector2.ClampMagnitude(front, 1f);
            back = Vector2.ClampMagnitude(back, 1f);
            if (front.sqrMagnitude < .001f || back.sqrMagnitude < .001f || Vector2.Dot(front, back) <= 0f)
                return Vector2.zero;
            return (front + back) * .5f;
        }

        private void FixedUpdate()
        {
            groupVelocity = Vector3.zero;
            frontEffort = backEffort = Vector3.zero;
            if (Holders.Count == 0) return;
            foreach (var holder in Holders)
                if (holder is InteractionController placing && placing.IsPlacingDown) return;
            if (frontHolder) Align(frontHolder, CarryRole.Front);
            if (backHolder) Align(backHolder, CarryRole.Back);
            if (!frontHolder || !backHolder)
            {
                var solo = frontHolder ? frontHolder : backHolder;
                if (solo && !solo.IsPickingUp)
                    MoveSolo(solo.GetComponent<ThirdPersonPlayerController>().RequestedMove);
                return;
            }
            var front = frontHolder.GetComponent<ThirdPersonPlayerController>();
            var back = backHolder.GetComponent<ThirdPersonPlayerController>();
            var fr = frontHolder.GetComponent<Rigidbody>();
            var br = backHolder.GetComponent<Rigidbody>();
            if (frontHolder.IsPickingUp || backHolder.IsPickingUp) return;
            if (Quaternion.Angle(Rigidbody.rotation, levelRotation) > .1f)
            {
                // Pivot about the already held end, keeping that hand contact while the other end rises.
                Vector3 end = PhysicsPoint(EndLocal(firstRole));
                Quaternion next = Quaternion.RotateTowards(Rigidbody.rotation, levelRotation,
                    levelingDegreesPerSecond * Time.fixedDeltaTime);
                Vector3 position = end - next * Vector3.Scale(EndLocal(firstRole), transform.lossyScale);
                Rigidbody.MovePosition(Rigidbody.position + SafeDelta(Rigidbody, position - Rigidbody.position));
                Rigidbody.MoveRotation(next);
                return;
            }
            if (Vector3.Distance(fr.position, GetStance(CarryRole.Front, fr.position.y)) > .03f ||
                Vector3.Distance(br.position, GetStance(CarryRole.Back, br.position.y)) > .03f) return;
            float targetY = (fr.position.y + br.position.y) * .5f + carryHeightAbovePlayer;
            if (Mathf.Abs(Rigidbody.position.y - targetY) > .005f)
            {
                var lift = Vector3.up * (Mathf.MoveTowards(Rigidbody.position.y, targetY,
                    alignmentSpeed * Time.fixedDeltaTime) - Rigidbody.position.y);
                Rigidbody.MovePosition(Rigidbody.position + SafeDelta(Rigidbody, lift));
                return;
            }
            state = CooperativeCarryState.Carrying;
            MoveTogether(front.RequestedMove, back.RequestedMove);
        }

        private void MoveTogether(Vector2 frontInput, Vector2 backInput)
        {
            if (state != CooperativeCarryState.Carrying || !frontHolder || !backHolder) return;

            var frontLocalInput = frontHolder.GetComponent<LocalPlayerInput>();
            var backLocalInput = backHolder.GetComponent<LocalPlayerInput>();

            float totalTurnInput = 0f;
            if (frontLocalInput) totalTurnInput += frontLocalInput.HoseAim.x;
            if (backLocalInput) totalTurnInput += backLocalInput.HoseAim.x;

            totalTurnInput = Mathf.Clamp(totalTurnInput, -1f, 1f);

            if (Mathf.Abs(totalTurnInput) > 0.01f)
            {
                currentYaw = Mathf.Repeat(currentYaw + totalTurnInput * turnSpeed * Time.fixedDeltaTime, 360f);
                levelRotation = Quaternion.Euler(0f, currentYaw, 0f);
            }

            UpdateEffort(frontInput, backInput);
            var front = frontHolder.GetComponent<ThirdPersonPlayerController>();
            var back = backHolder.GetComponent<ThirdPersonPlayerController>();
            var fr = frontHolder.GetComponent<Rigidbody>();
            var br = backHolder.GetComponent<Rigidbody>();

            Rigidbody.MoveRotation(levelRotation);

            Vector2 input = CombineInput(frontInput, backInput);
            float speed = Mathf.Min(front.BaseMoveSpeed, back.BaseMoveSpeed) * (1f - SpeedPenalty);
            Vector3 delta = new Vector3(input.x, 0f, input.y) * speed * Time.fixedDeltaTime;
            delta = SafeDelta(Rigidbody, delta);
            delta = SafeDelta(fr, delta);
            delta = SafeDelta(br, delta);

            groupVelocity = delta / Time.fixedDeltaTime;

            Rigidbody.MovePosition(Rigidbody.position + delta);

            fr.MovePosition(GetStance(CarryRole.Front, fr.position.y) + delta);
            br.MovePosition(GetStance(CarryRole.Back, br.position.y) + delta);

            Quaternion frontTargetRot = levelRotation * Quaternion.Euler(0f, 180f, 0f);
            Quaternion backTargetRot = levelRotation;

            fr.MoveRotation(Quaternion.RotateTowards(fr.rotation, frontTargetRot, 180f * Time.fixedDeltaTime));
            br.MoveRotation(Quaternion.RotateTowards(br.rotation, backTargetRot, 180f * Time.fixedDeltaTime));
        }

        private void UpdateEffort(Vector2 frontInput, Vector2 backInput)
        {
            bool blocked = CombineInput(frontInput, backInput).sqrMagnitude < .001f;
            frontInput = Vector2.ClampMagnitude(frontInput, 1f);
            backInput = Vector2.ClampMagnitude(backInput, 1f);
            frontEffort = blocked ? new Vector3(frontInput.x, 0f, frontInput.y) : Vector3.zero;
            backEffort = blocked ? new Vector3(backInput.x, 0f, backInput.y) : Vector3.zero;
        }

        private void MoveSolo(Vector2 input)
        {
            if (Holders.Count != 1) return;
            var player = frontHolder ? frontHolder : backHolder;
            var rb = player.GetComponent<Rigidbody>();
            if (Vector3.Distance(rb.position, GetStance(GetRole(player), rb.position.y)) > .03f) return;
            input = Vector2.ClampMagnitude(input, 1f);
            float speed = player.GetComponent<ThirdPersonPlayerController>().BaseMoveSpeed * (1f - SpeedPenalty);
            Vector3 delta = new Vector3(input.x, 0f, input.y) * speed * Time.fixedDeltaTime;
            delta = SafeDelta(rb, SafeDelta(Rigidbody, delta));
            groupVelocity = delta / Time.fixedDeltaTime;
            soloPivot += delta;
            Rigidbody.MovePosition(Rigidbody.position + delta);
            rb.MovePosition(GetStance(GetRole(player), rb.position.y) + delta);
        }

        private void Align(InteractionController player, CarryRole role)
        {
            var rb = player.GetComponent<Rigidbody>();
            rb.velocity = Vector3.zero;
            Vector3 target = GetStance(role, rb.position.y);
            Vector3 delta = Vector3.ClampMagnitude(target - rb.position, alignmentSpeed * Time.fixedDeltaTime);
            rb.MovePosition(rb.position + SafeDelta(rb, delta));
            Quaternion rotation = levelRotation * Quaternion.Euler(0f, role == CarryRole.Front ? 180f : 0f, 0f);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, rotation, 180f * Time.fixedDeltaTime));
        }

        private Vector3 SafeDelta(Rigidbody body, Vector3 delta)
        {
            float distance = delta.magnitude;
            if (distance < .00001f) return Vector3.zero;
            foreach (var hit in body.SweepTestAll(delta / distance, distance + .02f, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) ||
                    (frontHolder && hit.collider.transform.IsChildOf(frontHolder.transform)) ||
                    (backHolder && hit.collider.transform.IsChildOf(backHolder.transform))) continue;
                // Tangential contact with the floor should not block horizontal movement.
                if (Vector3.Dot(hit.normal, delta / distance) >= -.01f) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .02f));
            }
            return delta.normalized * distance;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (frontHolder) frontHolder.ReleaseHeldObject();
            if (backHolder) backHolder.ReleaseHeldObject();
        }
    }
}
