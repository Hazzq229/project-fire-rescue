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
        [Header("Formation and aiming")]
        [SerializeField] private float separation = 1.05f;
        [SerializeField] private float rearSideOffset = .35f;
        [SerializeField] private float handHeight = .40f, handForward = .42f;
        [SerializeField] private float rearHandForward = .26f;
        [SerializeField] private float movementSpeed = 1.6f, aimSpeed = 80f;
        [SerializeField, Range(0f, 65f)] private float maximumUpwardAim = 65f;
        [SerializeField] private bool showHUD = true;
        [SerializeField] private LineRenderer supportSegment;
        private float yaw, pitch, pressure, burstNoticeUntil;
        private int reelDirection;
        private bool captured, releasing, savedKinematic, savedGravity;
        private CollisionDetectionMode savedCollision;
        private readonly List<(Collider hose, Collider player)> ignoredCollisions = new();
        public float Pressure => pressure;
        public int ReelDirection => reelDirection;
        public int BurstCount { get; private set; }
        public float HoseLength => cable ? cable.CableLength : 0f;
        public InteractionController FrontPlayer => frontGrip ? frontGrip.Holder : null;
        public InteractionController RearPlayer => rearGrip ? rearGrip.Holder : null;
        public bool HasTeam => FrontPlayer && RearPlayer && FrontPlayer != RearPlayer;
        public Vector3 GroupVelocity { get; private set; }
        private Vector3 rearVelocity;
        private const float RearMoveSpeed = 1.2f;
        private SpringJoint[] cableJoints;
        private Vector3 carryOffset, carryOffsetVelocity;
        private float carryPhase, carryMotion, previousYaw, turnRate;
        public Vector3 CarryMotionOffset => carryOffset;
        private readonly List<(Rigidbody body, bool kinematic, bool gravity)> supportedPoints = new();
        public Vector3 GetPlayerVelocity(InteractionController player) =>
            HasTeam && player == RearPlayer ? rearVelocity : GroupVelocity;
        public Vector3 AimDirection => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        public bool ReadyToSpray => HasTeam && !FrontPlayer.IsPickingUp && !RearPlayer.IsPickingUp &&
            Vector3.Distance(RearPlayer.transform.position, RearStance()) < .18f;
        private Quaternion FlatRotation => Quaternion.Euler(0f, yaw, 0f);
        private Vector3 Forward => FlatRotation * Vector3.forward;
        private Vector3 RearStance() => FrontPlayer.GetComponent<Rigidbody>().position - Forward * separation +
            FlatRotation * Vector3.right * rearSideOffset;

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
                previousYaw = yaw;
                carryPhase = carryMotion = turnRate = 0f;
                carryOffset = carryOffsetVelocity = Vector3.zero;
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
            if (RearPlayer) CaptureCableSupports();
        }

        private void Update()
        {
            if (!captured) return;
            var rear = RearPlayer ? RearPlayer.GetComponent<LocalPlayerInput>() : null;
            if (!rear || !rear.IsReady) { reelDirection = 0; return; }
            if (rear.HoseExtendPressed) ToggleReel(RearPlayer, 1);
            if (rear.HoseRetractPressed) ToggleReel(RearPlayer, -1);
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
            var input = leader.GetComponent<LocalPlayerInput>();
            if (!leader.IsPickingUp)
            {
                Vector2 aim = FrontPlayer && input ? input.HoseAim : Vector2.zero;
                ApplyAim(leader, aim, Time.fixedDeltaTime);
                MoveTeam(input ? input.MoveInput : Vector2.zero);
            }
            if (HasTeam && !RearPlayer.IsPickingUp)
            {
                var rb = RearPlayer.GetComponent<Rigidbody>();
                var target = RearStance(); target.y = rb.position.y;
                target += GroupVelocity * Time.fixedDeltaTime;
                Vector3 correction = SafeDelta(rb, Vector3.ClampMagnitude(target - rb.position, RearMoveSpeed * Time.fixedDeltaTime));
                rb.MovePosition(rb.position + correction);
                rearVelocity = correction / Time.fixedDeltaTime;
            }
            UpdateCarryMotion(Time.fixedDeltaTime);
            AttachNozzle();
            UpdateCableSupports();
            foreach (var player in new[] { FrontPlayer, RearPlayer })
                if (player)
                {
                    var rb = player.GetComponent<Rigidbody>();
                    rb.velocity = Vector3.zero;
                    rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, FlatRotation, aimSpeed * Time.fixedDeltaTime));
                }
            var rearInput = RearPlayer ? RearPlayer.GetComponent<LocalPlayerInput>() : null;
            StepPressure(ReadyToSpray && rearInput && rearInput.ToolHeld, Time.fixedDeltaTime);
            StepReel(Time.fixedDeltaTime);
        }

        public void ApplyAim(InteractionController actor, Vector2 input, float dt)
        {
            if (actor != FrontPlayer || !actor || actor.IsPickingUp) return;
            input = Vector2.ClampMagnitude(input, 1f);
            float turnSpeed = aimSpeed;
            if (HasTeam)
            {
                var movement = actor.GetComponent<LocalPlayerInput>();
                float travelSpeed = movement ? Mathf.Clamp01(movement.MoveInput.magnitude) * movementSpeed : 0f;
                float radius = Mathf.Sqrt(separation * separation + rearSideOffset * rearSideOffset);
                // Reserve enough rear walking speed for both translation and its orbit.
                turnSpeed = Mathf.Min(turnSpeed, Mathf.Max(.1f, RearMoveSpeed - travelSpeed) / Mathf.Max(.1f, radius) * Mathf.Rad2Deg * .9f);
            }
            yaw = Mathf.Repeat(yaw + input.x * turnSpeed * dt, 360f);
            pitch = Mathf.Clamp(pitch + input.y * aimSpeed * dt, -65f, maximumUpwardAim);
        }

        private void UpdateCarryMotion(float dt)
        {
            float speed = Mathf.Max(GroupVelocity.magnitude, rearVelocity.magnitude * .4f);
            carryMotion = Mathf.MoveTowards(carryMotion, Mathf.Clamp01(speed / Mathf.Max(.1f, movementSpeed)), dt * 6f);
            // The calibrated running clip covers approximately .52 m per gait cycle.
            carryPhase = Mathf.Repeat(carryPhase + speed * dt * (Mathf.PI * 2f / .52f), Mathf.PI * 2f);
            turnRate = Mathf.DeltaAngle(previousYaw, yaw) / Mathf.Max(.001f, dt);
            previousYaw = yaw;
            float compliance = 1f / (1f + pressure * .75f);
            Vector3 right = FlatRotation * Vector3.right;
            Vector3 desired = Vector3.up * (Mathf.Sin(carryPhase * 2f) * .006f * carryMotion + Mathf.Sin(Time.time * 1.7f) * .002f);
            desired += right * (Mathf.Sin(carryPhase) * .006f * carryMotion - Mathf.Clamp(turnRate / 60f, -1f, 1f) * .006f);
            desired -= Vector3.ClampMagnitude(GroupVelocity * .008f, .008f);
            // Both grips and their real cable supports share this compliant motion.
            carryOffset = Vector3.SmoothDamp(carryOffset, desired * compliance, ref carryOffsetVelocity, .12f,
                Mathf.Infinity, dt);
        }

        public Vector3 GetCarryEffort(InteractionController actor)
        {
            Vector3 effort = Vector3.Cross(Vector3.up, GetPlayerVelocity(actor)) * 1.4f;
            effort += Forward * (Mathf.Sin(carryPhase) * 1.2f * carryMotion + Mathf.Clamp(turnRate / 60f, -1f, 1f) * .8f);
            return effort / (1f + pressure * .75f);
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
            float distance = RequiredCableLength(Vector3.zero);
            float lower = Mathf.Max(minimumLength, distance + .25f);
            float wanted = cable.CableLength + reelDirection * reelSpeed * dt;
            // Never shorten through the operators or force the nozzle to teleport.
            if (reelDirection < 0 && wanted < lower) { reelDirection = 0; return; }
            float length = Mathf.Clamp(wanted, minimumLength, maximumLength);
            cable.SetCableLength(length);
            FitSupportedJointLengths();
            if (length <= minimumLength || length >= maximumLength) reelDirection = 0;
        }

        private void MoveTeam(Vector2 input)
        {
            if (HasTeam && (RearPlayer.IsPickingUp || Vector3.Distance(RearPlayer.transform.position, RearStance()) > .3f)) return;
            var leader = FrontPlayer ? FrontPlayer : RearPlayer;
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 delta = new Vector3(input.x, 0f, input.y) * movementSpeed * Time.fixedDeltaTime;
            foreach (var player in new[] { FrontPlayer, RearPlayer })
                if (player) delta = SafeDelta(player.GetComponent<Rigidbody>(), delta);
            if (cable)
            {
                float current = RequiredCableLength(Vector3.zero);
                float next = RequiredCableLength(delta);
                if (next > cable.CableLength - .1f && next > current) delta = Vector3.zero;
            }
            var rb = leader.GetComponent<Rigidbody>();
            rb.MovePosition(rb.position + delta);
            GroupVelocity = delta / Time.fixedDeltaTime;
        }

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
            Quaternion rotation = Quaternion.Euler(-pitch, yaw, 0f);
            Vector3 position = leader.GetComponent<Rigidbody>().position + Vector3.up * handHeight + Forward * handForward + carryOffset;
            var provider = frontGrip.GetComponent<HoseGripProvider>();
            if (provider) position -= rotation * Vector3.Scale(provider.LocalGripCenter, frontGrip.transform.lossyScale);
            if (!FrontPlayer) position += Forward * separation;
            frontGrip.Rigidbody.position = position;
            frontGrip.Rigidbody.rotation = rotation;
            FollowRearGrip();
        }
        private void FollowRearGrip()
        {
            if (!frontGrip || !rearGrip) return;
            Vector3 forward = captured ? Forward : Vector3.ProjectOnPlane(frontGrip.transform.forward, Vector3.up).normalized;
            rearGrip.Rigidbody.position = RearPlayer
                ? RearPlayer.GetComponent<Rigidbody>().position + Vector3.up * handHeight + forward * rearHandForward + carryOffset
                : frontGrip.transform.position - forward * separation + Quaternion.LookRotation(forward) * Vector3.right * rearSideOffset;
            rearGrip.Rigidbody.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward);
        }

        // Lift real cable bodies at the support operator, with a straight span beside
        // the front operator and a short lead in front of the torso. The remaining
        // cable retains its springs, collisions and reel behaviour.
        private void CaptureCableSupports()
        {
            if (supportedPoints.Count != 0 || !cable || cable.Points == null || cable.Points.Count < 5) return;
            for (int i = cable.Points.Count - 4; i <= cable.Points.Count - 2; i++)
            {
                var body = cable.Points[i].GetComponent<Rigidbody>();
                supportedPoints.Add((body, body.isKinematic, body.useGravity));
                body.velocity = body.angularVelocity = Vector3.zero;
                body.isKinematic = true; body.useGravity = false;
            }
            cableJoints = cable.GetComponentsInChildren<SpringJoint>();
            UpdateCableSupports();
        }

        private void UpdateCableSupports()
        {
            if (!RearPlayer || supportedPoints.Count != 3) return;
            Vector3 right = FlatRotation * Vector3.right;
            Vector3 front = FrontPlayer ? FrontPlayer.GetComponent<Rigidbody>().position :
                RearPlayer.GetComponent<Rigidbody>().position + Forward * separation;
            Vector3 rear = RearPlayer.GetComponent<Rigidbody>().position;
            supportedPoints[0].body.position = rear + right * .46f + Forward * (rearHandForward + .12f) + Vector3.up * (handHeight - .13f) + carryOffset;
            supportedPoints[1].body.position = rearGrip.Rigidbody.position;
            supportedPoints[2].body.position = front + right * rearSideOffset + Forward * (handForward + .16f) + Vector3.up * handHeight + carryOffset;
            FitSupportedJointLengths();
        }

        private float SupportedSpanLength() => Vector3.Distance(supportedPoints[0].body.position, supportedPoints[1].body.position) +
            Vector3.Distance(supportedPoints[1].body.position, supportedPoints[2].body.position) +
            Vector3.Distance(supportedPoints[2].body.position, frontGrip.Rigidbody.position);

        private float RequiredCableLength(Vector3 translation)
        {
            if (supportedPoints.Count != 3) return Vector3.Distance(cable.SourcePosition, frontGrip.transform.position + translation);
            return Vector3.Distance(cable.SourcePosition, supportedPoints[0].body.position + translation) + SupportedSpanLength();
        }

        private void FitSupportedJointLengths()
        {
            if (supportedPoints.Count != 3 || cableJoints == null) return;
            float freeLength = Mathf.Max(.05f, (cable.CableLength - SupportedSpanLength()) / (cable.Points.Count - 4));
            foreach (var joint in cableJoints)
            {
                if (!joint || !joint.connectedBody || !joint.connectedBody.transform.IsChildOf(cable.transform)) continue;
                var body = joint.GetComponent<Rigidbody>();
                float length = body.isKinematic && joint.connectedBody.isKinematic
                    ? Vector3.Distance(body.position, joint.connectedBody.position) : freeLength;
                joint.minDistance = joint.maxDistance = length;
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
                foreach (var point in supportedPoints)
                    if (point.body) { point.body.isKinematic = point.kinematic; point.body.useGravity = point.gravity; }
                if (supportedPoints.Count > 0 && cable) cable.SetCableLength(cable.CableLength);
                supportedPoints.Clear();
                cableJoints = null;
                carryOffset = carryOffsetVelocity = Vector3.zero;
                carryMotion = turnRate = 0f;
                if (captured && frontGrip && frontGrip.Rigidbody)
                {
                    frontGrip.Rigidbody.isKinematic = savedKinematic;
                    frontGrip.Rigidbody.useGravity = savedGravity;
                    frontGrip.Rigidbody.collisionDetectionMode = savedCollision;
                }
                captured = false;
            }
            finally { releasing = false; }
        }
        private void OnDisable() { if (Application.isPlaying) ReleaseTeam(); }

        private void LateUpdate()
        {
            if (!supportSegment || !frontGrip || !rearGrip) return;
            // The physical cable now supplies the supported span; a second straight
            // renderer would cut through the operators and conceal the true contact.
            supportSegment.enabled = false;
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
                "Depan memimpin gerak dan arah. Belakang mengatur tekanan (tahan/lepas) dan toggle panjang selang.");
            GUILayout.EndArea();
        }
    }
}
