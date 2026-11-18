using HGame.Objects;
using HPhysic;
using HPlayer;
using UnityEngine;
using System.Collections.Generic;

namespace TA.AdaptiveAnimation
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class CooperativeFireHose : MonoBehaviour
    {
        [SerializeField] private CooperativeHoseGrip frontGrip, rearGrip;
        [SerializeField] private HoseWaterController water;
        [SerializeField] private PhysicCable cable;
        [Header("Pressure: hold to raise, release to bleed")]
        [SerializeField, Min(.01f)] private float pressureRise = .20f;
        [SerializeField, Min(.01f)] private float pressureFall = .45f;
        [SerializeField, Min(1f)] private float burstPressure = 1.25f;
        [SerializeField] private float knockbackSpeed = 7f;
        [SerializeField] private float knockbackDuration = .85f;
        [Header("Hose reel")]
        [SerializeField] private float minimumLength = 3f, maximumLength = 18f, reelSpeed = 1.5f;
        [Tooltip("Panjang penambahan/pengurangan selang setiap satu kali tombol ditekan (di-spam)")]
        [SerializeField, Min(0.05f)] private float reelStep = 0.5f;
        [Header("Formation and aiming")]
        [SerializeField] private float separation = 1.05f;
        [SerializeField, Min(1)] private int rearGripConnectorDistance = 3;
        [SerializeField] private float handHeight = .40f, handForward = .42f;
        [SerializeField] private float movementSpeed = 1.6f, aimSpeed = 80f;
        [SerializeField] private bool showHUD = true;
        [SerializeField] private LineRenderer supportSegment;
        private float yaw, pitch, pressure, burstNoticeUntil;
        private int reelDirection;
        private bool captured, releasing, savedKinematic, savedGravity;
        private CollisionDetectionMode savedCollision;
        private readonly List<(Collider hose, Collider player)> ignoredCollisions = new();
        private Rigidbody capturedRearPointRb;
        public float Pressure => pressure;
        public int ReelDirection => reelDirection;
        public int BurstCount { get; private set; }
        public float HoseLength => cable ? cable.CableLength : 0f;
        public float ReelStep
        {
            get => reelStep;
            set => reelStep = Mathf.Max(0.05f, value);
        }
        public InteractionController FrontPlayer => frontGrip ? frontGrip.Holder : null;
        public InteractionController RearPlayer => rearGrip ? rearGrip.Holder : null;
        public bool HasTeam => FrontPlayer && RearPlayer && FrontPlayer != RearPlayer;
        public Vector3 GroupVelocity { get; private set; }
        private Vector3 rearVelocity;
        public Vector3 GetPlayerVelocity(InteractionController player) =>
            HasTeam && player == RearPlayer ? rearVelocity : GroupVelocity;
        public Vector3 GetCarryEffort(InteractionController actor)
        {
            Vector3 effort = Vector3.Cross(Vector3.up, GetPlayerVelocity(actor)) * 1.4f;
            return effort / (1f + pressure * .75f);
        }
        public Vector3 AimDirection => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        public bool ReadyToSpray => HasTeam && !FrontPlayer.IsPickingUp && !RearPlayer.IsPickingUp;
        private Quaternion FlatRotation => Quaternion.Euler(0f, yaw, 0f);
        private Vector3 Forward => FlatRotation * Vector3.forward;
        private Vector3 RearStance() => FrontPlayer ? FrontPlayer.transform.position - Forward * separation : Vector3.zero;

        private void Awake()
        {
            if (rearGrip && rearGrip.transform.parent != null)
                rearGrip.transform.SetParent(null, true);
        }

        private void Start()
        {
            if (cable)
            {
                cable.FixedFrontConnectors = rearGripConnectorDistance;
                cable.FixedFrontDistance = separation;
                cable.SetCableLength(cable.CableLength);
            }
            FollowRearGrip();
        }

        public void Joined(CooperativeHoseGrip grip)
        {
            if (!captured)
            {
                var body = frontGrip.Rigidbody;
                savedKinematic = body.isKinematic; savedGravity = body.useGravity;
                savedCollision = body.collisionDetectionMode;
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true; body.useGravity = false;
                yaw = grip.Holder.transform.eulerAngles.y;
                pitch = 0f; captured = true;
                var connector = frontGrip.GetComponent<Connector>();
                if (connector && connector.IsConnected) connector.Disconnect();
            }
            grip.Holder.GetComponent<ThirdPersonPlayerController>().CooperativeHose = this;
            grip.Holder.GetComponent<Rigidbody>().velocity = Vector3.zero;
            foreach (var hoseCollider in cable.GetComponentsInChildren<Collider>())
                foreach (var playerCollider in grip.Holder.GetComponentsInChildren<Collider>())
                    if (!hoseCollider.isTrigger && !playerCollider.isTrigger &&
                        !Physics.GetIgnoreCollision(hoseCollider, playerCollider))
                    {
                        Physics.IgnoreCollision(hoseCollider, playerCollider, true);
                        ignoredCollisions.Add((hoseCollider, playerCollider));
                    }
            AttachNozzle();
        }

        private void Update()
        {
            if (!captured) return;
            var rear = RearPlayer ? RearPlayer.GetComponent<LocalPlayerInput>() : null;
            if (rear && rear.IsReady)
            {
                if (rear.HoseExtendPressed) ApplyReelStep(RearPlayer, 1);
                if (rear.HoseRetractPressed) ApplyReelStep(RearPlayer, -1);
            }
            else if (!HasTeam)
            {
                var solo = FrontPlayer ? FrontPlayer.GetComponent<LocalPlayerInput>() : null;
                if (solo && solo.IsReady)
                {
                    if (solo.HoseExtendPressed) ApplyReelStep(FrontPlayer, 1);
                    if (solo.HoseRetractPressed) ApplyReelStep(FrontPlayer, -1);
                }
            }
        }

        public void ApplyReelStep(InteractionController actor, int direction)
        {
            if (!cable || actor.IsPickingUp) return;
            if (HasTeam && actor != RearPlayer) return;
            direction = direction > 0 ? 1 : -1;
            reelDirection = 0; // Hentikan mode toggle terus-menerus jika sempat aktif

            float current = cable.CableLength;
            if (direction > 0)
            {
                float wanted = Mathf.Clamp(current + reelStep, minimumLength, maximumLength);
                cable.SetCableLength(wanted);
            }
            else
            {
                float rearDist = RearPlayer
                    ? Vector3.Distance(cable.SourcePosition, RearPlayer.GetComponent<Rigidbody>().position)
                    : (FrontPlayer ? Mathf.Max(0f, Vector3.Distance(cable.SourcePosition, frontGrip.transform.position) - separation) : 0f);
                float lower = Mathf.Max(minimumLength, rearDist + separation + .25f);
                float wanted = Mathf.Clamp(current - reelStep, lower, maximumLength);
                cable.SetCableLength(wanted);
            }
            if (RearPlayer) FollowRearGrip();
        }

        public void ToggleReel(InteractionController actor, int direction)
        {
            if (!HasTeam || actor != RearPlayer || actor.IsPickingUp) return;
            direction = direction > 0 ? 1 : -1;
            reelDirection = reelDirection == direction ? 0 : direction;
        }

        private void FixedUpdate()
        {
            GroupVelocity = Vector3.zero;
            rearVelocity = Vector3.zero;
            if (!captured) { FollowRearGrip(); return; }
            if ((FrontPlayer && !FrontPlayer.isActiveAndEnabled) || (RearPlayer && !RearPlayer.isActiveAndEnabled))
            { ReleaseTeam(); return; }
            var leader = FrontPlayer ? FrontPlayer : RearPlayer;
            if (!leader) { ReleaseTeam(); return; }

            // Aiming (FrontPlayer steers nozzle direction)
            var frontInput = FrontPlayer ? FrontPlayer.GetComponent<LocalPlayerInput>() : null;
            if (FrontPlayer && !FrontPlayer.IsPickingUp)
            {
                Vector2 aim = frontInput ? frontInput.HoseAim : Vector2.zero;
                ApplyAim(FrontPlayer, aim, Time.fixedDeltaTime);
            }

            // Movement: Solo or Cooperative Anchor System
            if (!HasTeam)
            {
                if (!leader.IsPickingUp)
                {
                    var mover = leader.GetComponent<ThirdPersonPlayerController>();
                    var leaderInput = leader.GetComponent<LocalPlayerInput>();
                    Vector2 move = mover ? mover.RequestedMove : (leaderInput ? leaderInput.MoveInput : Vector2.zero);
                    MoveSolo(leader, move);
                }
            }
            else
            {
                MoveCooperativeTeam();
            }

            AttachNozzle();
            var rearInput = RearPlayer ? RearPlayer.GetComponent<LocalPlayerInput>() : null;
            StepPressure(ReadyToSpray && rearInput && rearInput.ToolHeld, Time.fixedDeltaTime);
            StepReel(Time.fixedDeltaTime);
        }

        public void ApplyAim(InteractionController actor, Vector2 input, float dt)
        {
            if (actor != FrontPlayer || !actor || actor.IsPickingUp) return;
            input = Vector2.ClampMagnitude(input, 1f);
            yaw = Mathf.Repeat(yaw + input.x * aimSpeed * dt, 360f);
            pitch = Mathf.Clamp(pitch + input.y * aimSpeed * dt, -65f, 65f);
        }

        public void StepPressure(bool held, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, .1f);
            if (!ReadyToSpray) { pressure = 0f; if (water) water.SetCooperativeFlow(0f); return; }
            pressure = Mathf.Clamp(pressure + (held ? pressureRise : -pressureFall) * dt, 0f, burstPressure);
            if (pressure >= burstPressure)
            {
                var player = FrontPlayer.GetComponent<ThirdPersonPlayerController>();
                Vector3 away = -Forward;
                BurstCount++; burstNoticeUntil = Time.time + 3f;
                ReleaseTeam();
                // Slightly sideways keeps the recoiling front operator from immediately hitting the rear operator.
                player.ApplyHoseKnockback((away + FlatRotation * Vector3.right * .55f).normalized,
                    knockbackSpeed, knockbackDuration);
                return;
            }
            if (water) water.SetCooperativeFlow(pressure);
        }

        private void StepReel(float dt)
        {
            if (!HasTeam || !cable) { reelDirection = 0; return; }
            // Use RearPlayer body position (not rearGrip which may lag a frame) for distance check.
            float rearDist = RearPlayer
                ? Vector3.Distance(cable.SourcePosition, RearPlayer.GetComponent<Rigidbody>().position)
                : Mathf.Max(0f, Vector3.Distance(cable.SourcePosition, frontGrip.transform.position) - separation);
            float lower = Mathf.Max(minimumLength, rearDist + separation + .25f);
            float wanted = cable.CableLength + reelDirection * reelSpeed * dt;
            // Never shorten through the operators or force the nozzle to teleport.
            if (reelDirection < 0 && wanted < lower) { reelDirection = 0; return; }
            float length = Mathf.Clamp(wanted, minimumLength, maximumLength);
            cable.SetCableLength(length);
            if (length <= minimumLength || length >= maximumLength) reelDirection = 0;
            // Re-pin the rear cable point to Player 2's hand so the joint change doesn't
            // cause the hose to visually snap/detach around the rear grip.
            if (RearPlayer) FollowRearGrip();
        }

        private void MoveCooperativeTeam()
        {
            if (FrontPlayer.IsPickingUp || RearPlayer.IsPickingUp) return;

            var frontRb = FrontPlayer.GetComponent<Rigidbody>();
            var rearRb = RearPlayer.GetComponent<Rigidbody>();
            frontRb.velocity = Vector3.zero;
            rearRb.velocity = Vector3.zero;

            // Rotation of FrontPlayer with nozzle aim
            frontRb.MoveRotation(Quaternion.RotateTowards(frontRb.rotation, FlatRotation, aimSpeed * Time.fixedDeltaTime));

            var frontInputComp = FrontPlayer.GetComponent<LocalPlayerInput>();
            var frontMover = FrontPlayer.GetComponent<ThirdPersonPlayerController>();
            Vector2 frontMove = frontMover ? frontMover.RequestedMove : (frontInputComp ? frontInputComp.MoveInput : Vector2.zero);

            var rearInputComp = RearPlayer.GetComponent<LocalPlayerInput>();
            var rearMover = RearPlayer.GetComponent<ThirdPersonPlayerController>();
            Vector2 rearMove = rearMover ? rearMover.RequestedMove : (rearInputComp ? rearInputComp.MoveInput : Vector2.zero);

            const float inputThreshold = 0.05f;
            bool frontActive = frontMove.magnitude > inputThreshold;
            bool rearActive = rearMove.magnitude > inputThreshold;

            float maxTether = separation * 1.35f;

            // Case 1: Both Idle
            if (!frontActive && !rearActive)
            {
                GroupVelocity = Vector3.zero;
                rearVelocity = Vector3.zero;
                Vector3 toFront = frontRb.position - rearRb.position;
                toFront.y = 0f;
                if (toFront.sqrMagnitude > 0.01f)
                {
                    Quaternion lookFront = Quaternion.LookRotation(toFront.normalized, Vector3.up);
                    rearRb.MoveRotation(Quaternion.RotateTowards(rearRb.rotation, lookFront, aimSpeed * Time.fixedDeltaTime));
                }
                return;
            }

            // Case 2: Rear is Anchor (Rear Idle, Front Moving -> Front orbits / approaches / steps away up to maxTether)
            if (frontActive && !rearActive)
            {
                rearVelocity = Vector3.zero;
                Vector3 desiredFront = new Vector3(frontMove.x, 0f, frontMove.y) * movementSpeed;
                Vector3 deltaFront = desiredFront * Time.fixedDeltaTime;
                deltaFront = SafeDelta(frontRb, deltaFront);

                // Tether clamp relative to Rear (Anchor). Players can get as close as they want (no minDistance limit).
                Vector3 nextFrontPos = frontRb.position + deltaFront;
                Vector3 fromAnchor = nextFrontPos - rearRb.position;
                fromAnchor.y = 0f;
                float currentDist = Vector3.Distance(new Vector3(frontRb.position.x, 0f, frontRb.position.z), new Vector3(rearRb.position.x, 0f, rearRb.position.z));
                if (fromAnchor.magnitude > maxTether && fromAnchor.magnitude > currentDist)
                {
                    Vector3 clampedPos = rearRb.position + fromAnchor.normalized * maxTether;
                    deltaFront = clampedPos - frontRb.position;
                    deltaFront.y = 0f;
                    deltaFront = SafeDelta(frontRb, deltaFront);
                }

                // Front is constrained ONLY by tether to Rear (Cause #1).
                // Hydrant cable limit check on Front is removed as requested.

                frontRb.MovePosition(frontRb.position + deltaFront);
                GroupVelocity = deltaFront / Time.fixedDeltaTime;

                // Rear faces Front
                Vector3 toFront = (frontRb.position + deltaFront) - rearRb.position;
                toFront.y = 0f;
                if (toFront.sqrMagnitude > 0.01f)
                {
                    Quaternion lookFront = Quaternion.LookRotation(toFront.normalized, Vector3.up);
                    rearRb.MoveRotation(Quaternion.RotateTowards(rearRb.rotation, lookFront, aimSpeed * Time.fixedDeltaTime));
                }
                return;
            }

            // Case 3: Front is Anchor (Front Idle, Rear Moving -> Rear orbits / approaches / steps away up to maxTether)
            if (!frontActive && rearActive)
            {
                GroupVelocity = Vector3.zero;
                Vector3 desiredRear = new Vector3(rearMove.x, 0f, rearMove.y) * movementSpeed;
                Vector3 deltaRear = desiredRear * Time.fixedDeltaTime;
                deltaRear = SafeDelta(rearRb, deltaRear);

                // Tether clamp relative to Front (Anchor). Players can get as close as they want (no minDistance limit).
                Vector3 nextRearPos = rearRb.position + deltaRear;
                Vector3 fromAnchor = nextRearPos - frontRb.position;
                fromAnchor.y = 0f;
                float currentDist = Vector3.Distance(new Vector3(rearRb.position.x, 0f, rearRb.position.z), new Vector3(frontRb.position.x, 0f, frontRb.position.z));
                if (fromAnchor.magnitude > maxTether && fromAnchor.magnitude > currentDist)
                {
                    Vector3 clampedPos = frontRb.position + fromAnchor.normalized * maxTether;
                    deltaRear = clampedPos - rearRb.position;
                    deltaRear.y = 0f;
                    deltaRear = SafeDelta(rearRb, deltaRear);
                }

                // Cable length from hydrant check for Rear (Cause #2 applied to RearPlayer)
                if (cable)
                {
                    Vector3 rearPos = rearGrip ? rearGrip.transform.position : rearRb.position;
                    float maxRearCable = Mathf.Max(0.5f, cable.CableLength - separation);
                    float curHydrantDist = Vector3.Distance(cable.SourcePosition, rearPos);
                    float nextHydrantDist = Vector3.Distance(cable.SourcePosition, rearPos + deltaRear);
                    if (nextHydrantDist > maxRearCable - .1f && nextHydrantDist > curHydrantDist)
                    {
                        deltaRear = Vector3.zero;
                    }
                }

                rearRb.MovePosition(rearRb.position + deltaRear);
                rearVelocity = deltaRear / Time.fixedDeltaTime;

                // Rear faces movement direction
                if (desiredRear.sqrMagnitude > 0.01f)
                {
                    Quaternion lookDir = Quaternion.LookRotation(desiredRear.normalized, Vector3.up);
                    rearRb.MoveRotation(Quaternion.RotateTowards(rearRb.rotation, lookDir, aimSpeed * Time.fixedDeltaTime));
                }
                return;
            }

            // Case 4: Both Players Active (Both Moving)
            {
                Vector3 desiredFront = new Vector3(frontMove.x, 0f, frontMove.y) * movementSpeed;
                Vector3 desiredRear = new Vector3(rearMove.x, 0f, rearMove.y) * movementSpeed;

                Vector3 deltaFront = desiredFront * Time.fixedDeltaTime;
                Vector3 deltaRear = desiredRear * Time.fixedDeltaTime;

                deltaFront = SafeDelta(frontRb, deltaFront);
                deltaRear = SafeDelta(rearRb, deltaRear);

                // Cable length from hydrant check for Rear (Cause #2 applied to RearPlayer)
                if (cable)
                {
                    Vector3 rearPos = rearGrip ? rearGrip.transform.position : rearRb.position;
                    float maxRearCable = Mathf.Max(0.5f, cable.CableLength - separation);
                    float curHydrantDist = Vector3.Distance(cable.SourcePosition, rearPos);
                    float nextHydrantDist = Vector3.Distance(cable.SourcePosition, rearPos + deltaRear);
                    if (nextHydrantDist > maxRearCable - .1f && nextHydrantDist > curHydrantDist)
                    {
                        deltaRear = Vector3.zero;
                    }
                }

                // Tether clamp between both moving players. Players can approach each other freely.
                Vector3 nextFront = frontRb.position + deltaFront;
                Vector3 nextRear = rearRb.position + deltaRear;
                Vector3 between = nextFront - nextRear;
                between.y = 0f;
                float currentDist = Vector3.Distance(new Vector3(frontRb.position.x, 0f, frontRb.position.z), new Vector3(rearRb.position.x, 0f, rearRb.position.z));

                if (between.magnitude > maxTether && between.magnitude > currentDist)
                {
                    float excess = between.magnitude - maxTether;
                    Vector3 pullDir = between.normalized;

                    // If Rear is blocked by cable limit from moving further away from hydrant, Front absorbs the full pull
                    bool rearBlocked = false;
                    if (cable)
                    {
                        Vector3 rearPos = rearGrip ? rearGrip.transform.position : rearRb.position;
                        float maxRearCable = Mathf.Max(0.5f, cable.CableLength - separation);
                        float testDist = Vector3.Distance(cable.SourcePosition, rearPos + deltaRear + pullDir * (excess * 0.5f));
                        float curDist = Vector3.Distance(cable.SourcePosition, rearPos + deltaRear);
                        if (testDist > maxRearCable - .1f && testDist > curDist)
                            rearBlocked = true;
                    }

                    Vector3 adjustFront = rearBlocked ? -pullDir * excess : -pullDir * (excess * 0.5f);
                    Vector3 adjustRear = rearBlocked ? Vector3.zero : pullDir * (excess * 0.5f);

                    deltaFront += adjustFront; deltaFront.y = 0f; deltaFront = SafeDelta(frontRb, deltaFront);
                    deltaRear += adjustRear; deltaRear.y = 0f; deltaRear = SafeDelta(rearRb, deltaRear);
                }

                frontRb.MovePosition(frontRb.position + deltaFront);
                rearRb.MovePosition(rearRb.position + deltaRear);

                GroupVelocity = deltaFront / Time.fixedDeltaTime;
                rearVelocity = deltaRear / Time.fixedDeltaTime;

                // Rear rotates towards its movement direction
                if (desiredRear.sqrMagnitude > 0.01f)
                {
                    Quaternion lookDir = Quaternion.LookRotation(desiredRear.normalized, Vector3.up);
                    rearRb.MoveRotation(Quaternion.RotateTowards(rearRb.rotation, lookDir, aimSpeed * Time.fixedDeltaTime));
                }
            }
        }

        private void MoveSolo(InteractionController actor, Vector2 input)
        {
            var rb = actor.GetComponent<Rigidbody>();
            Vector3 delta = new Vector3(input.x, 0f, input.y) * movementSpeed * Time.fixedDeltaTime;
            delta = SafeDelta(rb, delta);
            if (cable && actor == FrontPlayer)
            {
                // Front: limited by total cable length from hydrant.
                float current = Vector3.Distance(cable.SourcePosition, frontGrip.transform.position);
                float next    = Vector3.Distance(cable.SourcePosition, frontGrip.transform.position + delta);
                if (next > cable.CableLength - .1f && next > current) delta = Vector3.zero;
            }
            else if (cable && actor == RearPlayer)
            {
                // Rear: limited by (cable length - front section) from hydrant.
                float maxRearCable = Mathf.Max(0.5f, cable.CableLength - separation);
                float current = Vector3.Distance(cable.SourcePosition, rb.position);
                float next    = Vector3.Distance(cable.SourcePosition, rb.position + delta);
                if (next > maxRearCable - .1f && next > current) delta = Vector3.zero;
            }
            rb.MovePosition(rb.position + delta);
            if (actor == FrontPlayer) GroupVelocity = delta / Time.fixedDeltaTime;
            else rearVelocity = delta / Time.fixedDeltaTime;
        }

        public void MoveTeam(Vector2 input) => MoveSolo(FrontPlayer ? FrontPlayer : RearPlayer, input);

        private Vector3 SafeDelta(Rigidbody body, Vector3 delta)
        {
            float length = delta.magnitude;
            if (length < .00001f) return Vector3.zero;
            foreach (var hit in body.SweepTestAll(delta.normalized, length + .02f, QueryTriggerInteraction.Ignore))
            {
                if ((cable && hit.transform.IsChildOf(cable.transform)) ||
                    (FrontPlayer && hit.transform.IsChildOf(FrontPlayer.transform)) ||
                    (RearPlayer && hit.transform.IsChildOf(RearPlayer.transform))) continue;
                if (Vector3.Dot(hit.normal, delta.normalized) >= -.01f) continue;
                length = Mathf.Min(length, Mathf.Max(0f, hit.distance - .02f));
            }
            return delta.normalized * length;
        }

        private void AttachNozzle()
        {
            var leader = FrontPlayer ? FrontPlayer : RearPlayer;
            if (!leader) return;
            Vector3 position = leader.GetComponent<Rigidbody>().position + Vector3.up * handHeight + Forward * handForward;
            if (!FrontPlayer) position += Forward * separation;
            frontGrip.Rigidbody.position = position;
            frontGrip.Rigidbody.rotation = Quaternion.Euler(-pitch, yaw, 0f);
            FollowRearGrip();
        }

        public Transform GetRearHoseAnchor()
        {
            if (!cable) return null;
            if (cable.Connectors != null && cable.Connectors.Count >= rearGripConnectorDistance)
            {
                int index = Mathf.Clamp(cable.Connectors.Count - rearGripConnectorDistance, 0, cable.Connectors.Count - 1);
                return cable.Connectors[index];
            }
            return GetRearHosePoint();
        }

        public Transform GetRearHosePoint()
        {
            if (!cable || cable.Points == null || cable.Points.Count == 0) return null;
            int index = Mathf.Clamp(cable.Points.Count - 1 - rearGripConnectorDistance, 0, cable.Points.Count - 1);
            return cable.Points[index];
        }

        private void FollowRearGrip()
        {
            if (!rearGrip) return;
            if (RearPlayer)
            {
                var rearRb = RearPlayer.GetComponent<Rigidbody>();
                Vector3 rearHandPos = rearRb.position + Vector3.up * handHeight + RearPlayer.transform.forward * handForward;
                if (rearGrip.Rigidbody)
                {
                    rearGrip.Rigidbody.position = rearHandPos;
                    rearGrip.Rigidbody.rotation = RearPlayer.transform.rotation;
                }
                else
                {
                    rearGrip.transform.position = rearHandPos;
                    rearGrip.transform.rotation = RearPlayer.transform.rotation;
                }

                Transform rearPoint = GetRearHosePoint();
                if (rearPoint && rearPoint.TryGetComponent<Rigidbody>(out var pointRb))
                {
                    // Release old captured body first if the point has changed.
                    if (capturedRearPointRb && capturedRearPointRb != pointRb)
                    {
                        capturedRearPointRb.isKinematic = false;
                        capturedRearPointRb = null;
                    }
                    capturedRearPointRb = pointRb;
                    pointRb.isKinematic = true;
                    pointRb.position = rearHandPos;
                    pointRb.rotation = RearPlayer.transform.rotation;
                }
                return;
            }

            // No RearPlayer: release pinned rear point so physics is free again.
            if (capturedRearPointRb)
            {
                capturedRearPointRb.isKinematic = false;
                capturedRearPointRb = null;
            }

            Transform anchor = GetRearHoseAnchor();
            if (anchor)
            {
                if (rearGrip.Rigidbody)
                {
                    rearGrip.Rigidbody.position = anchor.position;
                    rearGrip.Rigidbody.rotation = anchor.rotation;
                }
                else
                {
                    rearGrip.transform.position = anchor.position;
                    rearGrip.transform.rotation = anchor.rotation;
                }
            }
            else if (frontGrip)
            {
                Vector3 forward = captured ? Forward : Vector3.ProjectOnPlane(frontGrip.transform.forward, Vector3.up).normalized;
                Vector3 fallbackPos = frontGrip.transform.position - forward * separation;
                if (rearGrip.Rigidbody)
                {
                    rearGrip.Rigidbody.position = fallbackPos;
                    rearGrip.Rigidbody.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward);
                }
                else
                {
                    rearGrip.transform.position = fallbackPos;
                    rearGrip.transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward);
                }
            }
        }

        public void ReleaseTeam()
        {
            if (releasing) return;
            releasing = true;
            try
            {
                pressure = 0f; reelDirection = 0; GroupVelocity = rearVelocity = Vector3.zero;
                if (water) water.SetCooperativeFlow(0f);
                if (FrontPlayer) FrontPlayer.ReleaseHeldObject();
                if (RearPlayer) RearPlayer.ReleaseHeldObject();
                foreach (var pair in ignoredCollisions)
                    if (pair.hose && pair.player) Physics.IgnoreCollision(pair.hose, pair.player, false);
                ignoredCollisions.Clear();
                if (captured && frontGrip && frontGrip.Rigidbody)
                {
                    frontGrip.Rigidbody.isKinematic = savedKinematic;
                    frontGrip.Rigidbody.useGravity = savedGravity;
                    frontGrip.Rigidbody.collisionDetectionMode = savedCollision;
                }
                if (capturedRearPointRb)
                {
                    capturedRearPointRb.isKinematic = false;
                    capturedRearPointRb = null;
                }
                captured = false;
            }
            finally { releasing = false; }
        }
        private void OnDisable() { if (Application.isPlaying) ReleaseTeam(); }

        private void LateUpdate()
        {
            if (supportSegment) supportSegment.enabled = false;
            if (!RearPlayer) FollowRearGrip();
        }

        private void OnGUI()
        {
            if (!showHUD) return;
            GUILayout.BeginArea(new Rect(12, Screen.height - 195, 660, 183), GUI.skin.box);
            GUILayout.Label("FIRE HOSE • 2 PLAYER  |  Depan: " + (FrontPlayer ? FrontPlayer.name : "kosong") +
                "  |  Belakang: " + (RearPlayer ? RearPlayer.name : "kosong"));
            GUILayout.Label("Tekanan " + (pressure * 100f).ToString("F0") + "%  • Bahaya >100%, terlempar 125%  |  Selang " + HoseLength.ToString("F1") + " m");
            Rect bar = GUILayoutUtility.GetRect(1f, 12f, GUILayout.ExpandWidth(true));
            Color saved = GUI.color;
            GUI.color = Color.gray; GUI.DrawTexture(bar, Texture2D.whiteTexture);
            bar.width *= pressure / burstPressure;
            GUI.color = pressure > 1f ? Color.red : pressure > .8f ? Color.yellow : Color.cyan;
            GUI.DrawTexture(bar, Texture2D.whiteTexture); GUI.color = saved;
            GUILayout.Label("P1: F ambil/lepas • WASD jalan • IJKL arah • G tahan tekanan • R/T panjang/pendek");
            GUILayout.Label("P2: Right Ctrl ambil/lepas • Arrow jalan • - atas / P kiri / [ bawah / ] kanan • Enter tekanan • N/M panjang/pendek");
            GUILayout.Label(Time.time < burstNoticeUntil ? "TEKANAN BERLEBIH! Ambil kembali kedua pegangan." :
                !HasTeam ? "Ambil nozzle depan dan pegangan belakang; air membutuhkan dua pemain." :
                "Depan memimpin gerak dan arah. Belakang mengatur tekanan dan panjang selang (N/M tap/spam).");
            GUILayout.EndArea();
        }
    }
}
