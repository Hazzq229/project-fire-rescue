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
        [SerializeField] private float handHeight = .40f, handForward = .42f;
        [SerializeField] private float movementSpeed = 1.6f, aimSpeed = 80f;
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
        public Vector3 GetPlayerVelocity(InteractionController player) =>
            HasTeam && player == RearPlayer ? rearVelocity : GroupVelocity;
        public Vector3 AimDirection => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        public bool ReadyToSpray => HasTeam && !FrontPlayer.IsPickingUp && !RearPlayer.IsPickingUp &&
            Vector3.Distance(RearPlayer.transform.position, RearStance()) < .18f;
        private Quaternion FlatRotation => Quaternion.Euler(0f, yaw, 0f);
        private Vector3 Forward => FlatRotation * Vector3.forward;
        private Vector3 RearStance() => FrontPlayer.transform.position - Forward * separation;

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
            AttachNozzle();
            if (HasTeam && !RearPlayer.IsPickingUp)
            {
                var rb = RearPlayer.GetComponent<Rigidbody>();
                var target = RearStance(); target.y = rb.position.y;
                Vector3 correction = SafeDelta(rb, Vector3.ClampMagnitude(target - rb.position, 2f * Time.fixedDeltaTime));
                rb.MovePosition(rb.position + correction);
                rearVelocity = correction / Time.fixedDeltaTime;
            }
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
            float distance = Vector3.Distance(cable.SourcePosition, frontGrip.transform.position);
            float lower = Mathf.Max(minimumLength, distance + .25f);
            float wanted = cable.CableLength + reelDirection * reelSpeed * dt;
            // Never shorten through the operators or force the nozzle to teleport.
            if (reelDirection < 0 && wanted < lower) { reelDirection = 0; return; }
            float length = Mathf.Clamp(wanted, minimumLength, maximumLength);
            cable.SetCableLength(length);
            if (length <= minimumLength || length >= maximumLength) reelDirection = 0;
        }

        private void MoveTeam(Vector2 input)
        {
            if (HasTeam && (RearPlayer.IsPickingUp || Vector3.Distance(RearPlayer.transform.position, RearStance()) > .3f)) return;
            var leader = FrontPlayer ? FrontPlayer : RearPlayer;
            Vector3 delta = new Vector3(input.x, 0f, input.y) * movementSpeed * Time.fixedDeltaTime;
            foreach (var player in new[] { FrontPlayer, RearPlayer })
                if (player) delta = SafeDelta(player.GetComponent<Rigidbody>(), delta);
            if (cable)
            {
                float current = Vector3.Distance(cable.SourcePosition, frontGrip.transform.position);
                float next = Vector3.Distance(cable.SourcePosition, frontGrip.transform.position + delta);
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
            Vector3 position = leader.GetComponent<Rigidbody>().position + Vector3.up * handHeight + Forward * handForward;
            if (!FrontPlayer) position += Forward * separation;
            frontGrip.Rigidbody.position = position;
            frontGrip.Rigidbody.rotation = Quaternion.Euler(-pitch, yaw, 0f);
            FollowRearGrip();
        }
        private void FollowRearGrip()
        {
            if (!frontGrip || !rearGrip) return;
            Vector3 forward = captured ? Forward : Vector3.ProjectOnPlane(frontGrip.transform.forward, Vector3.up).normalized;
            rearGrip.Rigidbody.position = frontGrip.transform.position - forward * separation;
            rearGrip.Rigidbody.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : Vector3.forward);
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
                captured = false;
            }
            finally { releasing = false; }
        }
        private void OnDisable() { if (Application.isPlaying) ReleaseTeam(); }

        private void LateUpdate()
        {
            if (!supportSegment || !frontGrip || !rearGrip) return;
            supportSegment.enabled = captured;
            if (!captured) return;
            supportSegment.positionCount = 2;
            supportSegment.SetPosition(0, rearGrip.transform.position);
            supportSegment.SetPosition(1, frontGrip.transform.position);
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
