using System;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Builds visible steps from traveled distance and keeps the supporting shoe in world space.</summary>
    internal sealed class FreeWalkFootContacts
    {
        // Firefighter leg reach and the calibrated flat sole of the replacement rig.
        private const float StrideLength = .40f;
        private const float SupportFraction = .52f;
        private const float TurnRadius = .165f;
        private const float SoleToAnkle = .075f;
        private const float StanceDrop = .115f;
        private const float PivotLandingRadius = .07f;
        private const float MaxPivotDrop = .045f;
        private const float SwingLift = .085f;
        private const float GroundBlendSeconds = .16f;
        private const float StanceSmoothTime = .12f;
        private const float FirstStepSeconds = .09f;
        private const float SettleSeconds = .16f;
        private static readonly int Walking = Animator.StringToHash("isWalking");
        private static readonly int Idle = Animator.StringToHash("Idle 0");

        private sealed class Leg
        {
            public Transform thigh, shin, ankle, toe;
            public Vector3 restOffset;
            public Quaternion restShoe, restToe;
            public Pose planted, landing, shown, settleFrom;
            public bool swinging;

            public bool IsValid => thigh && shin && ankle && toe;
        }

        private readonly Transform visual;
        private readonly Vector3 visualRest;
        private readonly Leg[] legs;
        private bool calibrated, active, wasWalking, pivoting;
        private float phase, weight, lastYaw, extraDrop, idleDrop;
        private float groundBlend, stanceBlend, stanceVelocity, firstStep, settle;
        private int closingLeg;
        private Vector3 lastRoot;

        public FreeWalkFootContacts(Transform visual, CharacterModelAnimatorMirror.BonePair[] pairs)
        {
            this.visual = visual;
            visualRest = visual.localPosition;
            legs = new[] { FindLeg(pairs, ".L"), FindLeg(pairs, ".R") };
        }

        private static Leg FindLeg(CharacterModelAnimatorMirror.BonePair[] pairs, string suffix) => new Leg {
            thigh = FindBone(pairs, "thigh" + suffix), shin = FindBone(pairs, "shin" + suffix),
            ankle = FindBone(pairs, "foot" + suffix), toe = FindBone(pairs, "toe" + suffix)
        };

        private static Transform FindBone(CharacterModelAnimatorMirror.BonePair[] pairs, string name) =>
            Array.Find(pairs, p => p.target && p.target.name == name).target;

        public void Reset()
        {
            if (active && visual) visual.localPosition = visualRest;
            active = wasWalking = pivoting = false;
            weight = groundBlend = stanceBlend = stanceVelocity = extraDrop = 0f;
            settle = 1f;
            closingLeg = -1;
        }

        public void Apply(ThirdPersonPlayerController mover, Animator animator)
        {
            if (!legs[0].IsValid || !legs[1].IsValid)
            {
                Reset();
                return;
            }
            if (!mover.CanPlantWalkingFeet)
            {
                ReleaseIdleHeight(mover);
                return;
            }

            Transform actor = mover.transform;
            float ankleHeight = actor.position.y - mover.FreeWalkFloorOffset + SoleToAnkle;
            if (!calibrated)
            {
                // Sample the mirrored idle pose once. Rest axes differ between rigs.
                if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash != Idle || animator.IsInTransition(0)) return;
                foreach (var leg in legs)
                {
                    idleDrop = Mathf.Max(idleDrop, leg.ankle.position.y - ankleHeight + .008f);
                    leg.restOffset = actor.InverseTransformPoint(leg.ankle.position);
                    leg.restOffset.y = 0f;
                    leg.restShoe = Quaternion.Inverse(actor.rotation) * leg.ankle.rotation;
                    leg.restToe = leg.toe.localRotation;
                }
                calibrated = true;
            }

            Vector3 root = actor.position;
            float yaw = actor.eulerAngles.y;
            Vector3 travel = Vector3.ProjectOnPlane(root - lastRoot, Vector3.up);
            float yawDelta = Mathf.DeltaAngle(lastYaw, yaw);
            float turnDistance = Mathf.Abs(yawDelta) * Mathf.Deg2Rad * TurnRadius;
            bool walking = animator.GetBool(Walking);
            // After a teleport or a skipped swing, start with new contacts.
            if (active && travel.magnitude + turnDistance >= StrideLength * (1f - SupportFraction)) Reset();
            if (!active)
            {
                active = true;
                settle = 1f;
                closingLeg = -1;
                travel = Vector3.zero;
                turnDistance = 0f;
                yawDelta = 0f;
                foreach (var leg in legs)
                {
                    leg.planted = LandingPose(actor, leg, ankleHeight, Vector3.zero);
                    leg.shown = new Pose(leg.ankle.position, leg.ankle.rotation);
                    leg.swinging = false;
                }
            }

            lastRoot = root;
            lastYaw = yaw;
            groundBlend = Mathf.MoveTowards(groundBlend, 1f, Time.deltaTime / GroundBlendSeconds);
            weight = Mathf.SmoothStep(0f, 1f, groundBlend);
            float targetStance = walking ? 1f : 0f;
            stanceBlend = Mathf.SmoothDamp(stanceBlend, targetStance, ref stanceVelocity, StanceSmoothTime);
            if (Mathf.Abs(stanceBlend - targetStance) < .003f && Mathf.Abs(stanceVelocity) < .06f)
            {
                stanceBlend = targetStance;
                stanceVelocity = 0f;
            }
            float drop = weight * Mathf.Lerp(idleDrop, StanceDrop, stanceBlend);
            SetVisualDrop(drop);

            if (walking && !wasWalking)
            {
                // Resume the same swing if the player restarts before it lands.
                int swing = settle < 1f && closingLeg >= 0 ? closingLeg : FirstSwingLeg(actor, ankleHeight);
                phase = swing == 0 ? .75f : .25f;
                if (settle < 1f) legs[swing].planted = legs[swing].shown;
                foreach (var leg in legs) leg.swinging = false;
                firstStep = 0f;
                // This frame creates the first swing. Advance the gait from
                // subsequent travel, as in the established walking cycle.
                travel = Vector3.zero;
                turnDistance = yawDelta = 0f;
            }
            else if (!walking && wasWalking)
            {
                closingLeg = legs[0].swinging ? 0 : legs[1].swinging ? 1 : -1;
                settle = 0f;
                foreach (var leg in legs) leg.settleFrom = leg.shown;
                if (closingLeg >= 0) legs[closingLeg].planted = LandingPose(actor, legs[closingLeg], ankleHeight, Vector3.zero);
            }
            wasWalking = walking;

            // Yaw advances the steps through a near-stationary pivot as well as along a corner.
            float distance = travel.magnitude + turnDistance;
            pivoting = walking && (mover.IsReversingFreeWalk ||
                pivoting && Mathf.Abs(yawDelta) > 45f * Time.deltaTime);
            Pose left, right;
            if (walking)
            {
                firstStep = Mathf.MoveTowards(firstStep, 1f, Time.deltaTime / FirstStepSeconds);
                phase = Mathf.Repeat(phase + distance / StrideLength, 1f);
                Vector3 direction = distance > .0001f ? travel / distance : Vector3.zero;
                float yawPerMeter = distance > .0001f ? yawDelta / distance : 0f;
                left = PlanLeg(legs[0], phase, actor, ankleHeight, direction, yawPerMeter);
                right = PlanLeg(legs[1], Mathf.Repeat(phase + .5f, 1f), actor, ankleHeight, direction, yawPerMeter);
            }
            else
            {
                settle = Mathf.MoveTowards(settle, 1f, Time.deltaTime / SettleSeconds);
                left = PlanSettle(0);
                right = PlanSettle(1);
            }

            // Keep the planted world point fixed. Lower the visual hips only as
            // much as needed for one supporting leg to reach it during the pivot.
            float supportDrop = 0f;
            if (pivoting || stanceBlend < 1f)
            {
                float leftDrop = DropToReachSupport(legs[0], walking), rightDrop = DropToReachSupport(legs[1], walking);
                if (walking) supportDrop = Mathf.Min(leftDrop, rightDrop);
                else
                {
                    float both = Mathf.Max(leftDrop, rightDrop);
                    float supporting = closingLeg == 0 ? rightDrop : closingLeg == 1 ? leftDrop : both;
                    supportDrop = Mathf.Lerp(supporting, both, Mathf.SmoothStep(0f, 1f, settle));
                }
            }
            extraDrop = Mathf.Max(Mathf.Min(supportDrop, MaxPivotDrop) * weight,
                Mathf.MoveTowards(extraDrop, 0f, 0.35f * Time.deltaTime));
            if (extraDrop > 0f) SetVisualDrop(drop + extraDrop);
            ApplyLeg(legs[0], left, actor.forward);
            ApplyLeg(legs[1], right, actor.forward);
        }

        private void ReleaseIdleHeight(ThirdPersonPlayerController mover)
        {
            // Shoe IK ends with the state. Ease out only the new idle height,
            // leaving held-object poses free to reset immediately as before.
            float previous = weight;
            if (!active || wasWalking || previous <= 0f || mover.UsesCarryStrideDirection || mover.IsKnockedBack)
            {
                Reset();
                return;
            }

            groundBlend = Mathf.MoveTowards(groundBlend, 0f, Time.deltaTime / GroundBlendSeconds);
            weight = Mathf.SmoothStep(0f, 1f, groundBlend);
            if (weight <= 0f) Reset();
            else visual.localPosition = Vector3.LerpUnclamped(visualRest, visual.localPosition, weight / previous);
        }

        private int FirstSwingLeg(Transform actor, float height)
        {
            // If the previous stop left an uneven stance, move the foot that is
            // farther from its neutral position while the closer foot supports.
            float left = (legs[0].planted.position - LandingPose(actor, legs[0], height, Vector3.zero).position).sqrMagnitude;
            float right = (legs[1].planted.position - LandingPose(actor, legs[1], height, Vector3.zero).position).sqrMagnitude;
            return left > right + .0001f ? 0 : 1;
        }

        private Pose PlanSettle(int index)
        {
            Leg leg = legs[index];
            leg.swinging = settle < 1f && index == closingLeg;
            if (settle >= 1f) return leg.planted;
            float t = Mathf.SmoothStep(0f, 1f, settle);
            var position = Vector3.Lerp(leg.settleFrom.position, leg.planted.position, t);
            // Finish a short step rather than skimming the foot along the floor.
            if (leg.swinging) position.y += Mathf.Sin(t * Mathf.PI) * .025f;
            return new Pose(position, Quaternion.Slerp(leg.settleFrom.rotation, leg.planted.rotation, t));
        }

        private void SetVisualDrop(float drop) =>
            visual.localPosition = visualRest + visual.parent.InverseTransformVector(Vector3.down * drop);

        private static float DropToReachSupport(Leg leg, bool ignoreSwing)
        {
            if (ignoreSwing && leg.swinging) return float.PositiveInfinity;
            Vector3 hipToFoot = leg.thigh.position - leg.planted.position;
            float reach = Vector3.Distance(leg.thigh.position, leg.shin.position) +
                Vector3.Distance(leg.shin.position, leg.ankle.position) - .005f;
            float horizontalSquared = hipToFoot.x * hipToFoot.x + hipToFoot.z * hipToFoot.z;
            float verticalRoom = Mathf.Sqrt(Mathf.Max(0f, reach * reach - horizontalSquared));
            return Mathf.Max(0f, hipToFoot.y - verticalRoom);
        }

        private static void KeepPivotLandingNearHip(Leg leg)
        {
            Vector3 hip = leg.thigh.position;
            hip.y = leg.landing.position.y;
            leg.landing.position = hip + Vector3.ClampMagnitude(leg.landing.position - hip, PivotLandingRadius);
        }

        private static Pose LandingPose(Transform actor, Leg leg, float height, Vector3 lead)
        {
            Vector3 position = actor.TransformPoint(leg.restOffset) + lead;
            position.y = height;
            return new Pose(position, actor.rotation * leg.restShoe);
        }

        private Pose PlanLeg(Leg leg, float cycle, Transform actor, float height, Vector3 direction, float yawPerMeter)
        {
            bool swing = cycle >= SupportFraction;
            if (pivoting && (swing || leg.swinging)) KeepPivotLandingNearHip(leg);
            if (!swing && leg.swinging) leg.planted = leg.landing;

            Pose pose = leg.planted;
            if (swing)
            {
                float t = Mathf.InverseLerp(SupportFraction, 1f, cycle);
                // Predict the body pose at touchdown, then hold the landing point
                // steady as the shoe comes down.
                if (!leg.swinging || t < .8f)
                {
                    float left = (1f - cycle) * StrideLength;
                    Vector3 lead = direction * (left + StrideLength * SupportFraction * .5f);
                    Quaternion turn = Quaternion.AngleAxis(Mathf.Clamp(yawPerMeter * left, -25f, 25f), Vector3.up);
                    leg.landing = LandingPose(actor, leg, height, lead);
                    leg.landing.position += (turn * (actor.rotation * leg.restOffset)) - actor.rotation * leg.restOffset;
                    leg.landing.rotation = turn * leg.landing.rotation;
                    if (pivoting) KeepPivotLandingNearHip(leg);
                }
                float launch = Mathf.SmoothStep(0f, 1f, firstStep);
                float eased = Mathf.SmoothStep(0f, 1f, t) * launch;
                pose.position = Vector3.Lerp(pose.position, leg.landing.position, eased);
                pose.position.y += Mathf.Sin(t * Mathf.PI) * SwingLift * launch;
                pose.rotation = Quaternion.Slerp(pose.rotation, leg.landing.rotation, eased);
            }
            leg.swinging = swing;
            return pose;
        }

        private void ApplyLeg(Leg leg, Pose pose, Vector3 facing)
        {
            Quaternion clipShoe = leg.ankle.rotation;
            CharacterModelAnimatorMirror.SolveContact(leg.thigh, leg.shin, leg.ankle,
                Vector3.Lerp(leg.ankle.position, pose.position, weight), null, 0f, facing);
            leg.ankle.rotation = Quaternion.Slerp(clipShoe, pose.rotation, weight);
            leg.toe.localRotation = Quaternion.Slerp(leg.toe.localRotation, leg.restToe, weight);
            leg.shown = new Pose(leg.ankle.position, leg.ankle.rotation);
        }
    }
}
