using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace TA.AdaptiveAnimation
{
    /// <summary>Synchronizes a replacement model's skeleton with the gameplay skeleton.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterModelAnimatorMirror : MonoBehaviour
    {
        [Serializable]
        public struct BonePair
        {
            public Transform source;
            public Transform target;
        }

        [SerializeField] Animator sourceAnimator;
        [SerializeField] BonePair[] bonePairs;

        private struct RetargetedLimb
        {
            public TwoBoneIKConstraint constraint;
            public Rig rig;
            public Transform root, mid, tip;
        }

        private RetargetedLimb[] limbs;
        private FreeWalkFootContacts walkFeet;
        private Animator cachedAnimator;
        private float carryStrideYaw;

        private void OnEnable()
        {
            limbs = null;
            carryStrideYaw = 0f;
        }

        private void OnDisable() => walkFeet?.Reset();

        public void Configure(Animator source, Transform sourceSkeletonRoot, Transform replacementSkeletonRoot)
        {
            walkFeet?.Reset();
            walkFeet = null;
            sourceAnimator = source;
            limbs = null;
            if (!sourceSkeletonRoot || !replacementSkeletonRoot)
            {
                bonePairs = Array.Empty<BonePair>();
                return;
            }

            var replBones = replacementSkeletonRoot.GetComponentsInChildren<Transform>(true);
            var replMap = new Dictionary<string, Transform>(replBones.Length);
            foreach (var b in replBones)
            {
                if (!replMap.ContainsKey(b.name))
                    replMap.Add(b.name, b);
            }

            var srcBones = sourceSkeletonRoot.GetComponentsInChildren<Transform>(true);
            var pairs = new List<BonePair>(srcBones.Length);
            foreach (var s in srcBones)
            {
                if (replMap.TryGetValue(s.name, out var target))
                {
                    pairs.Add(new BonePair { source = s, target = target });
                }
            }

            bonePairs = pairs.ToArray();
        }

        void LateUpdate()
        {
            if (bonePairs == null || bonePairs.Length == 0) return;

            var mover = sourceAnimator ? sourceAnimator.GetComponent<ThirdPersonPlayerController>() : null;
            Vector2 lean = mover ? mover.WalkBodyLean : Vector2.zero;
            for (var i = 0; i < bonePairs.Length; i++)
            {
                var pair = bonePairs[i];
                if (pair.source && pair.target)
                {
                    pair.target.localRotation = pair.source.localRotation;
                    if (lean.sqrMagnitude > .0001f && pair.target.name == "spine.003")
                    {
                        pair.target.rotation = Quaternion.AngleAxis(lean.y, mover.transform.forward) *
                            Quaternion.AngleAxis(lean.x, mover.transform.right) * pair.target.rotation;
                    }
                }
            }

            ApplyCarryStrideDirection(mover);
            if (Application.isPlaying && mover && mover.FreeWalkFloorOffset > 0f && sourceAnimator.isActiveAndEnabled)
            {
                if (walkFeet == null) walkFeet = new FreeWalkFootContacts(transform, bonePairs);
                walkFeet.Apply(mover, sourceAnimator);
            }
            else walkFeet?.Reset();

            // Copying rotations alone cannot preserve world-space contacts on a model with
            // different rest proportions. Resolve the same active IK targets on its own bones.
            if (limbs == null || cachedAnimator != sourceAnimator) CacheLimbs();
            if (!sourceAnimator || !sourceAnimator.isActiveAndEnabled) return;
            foreach (var limb in limbs)
            {
                if (!limb.constraint || !limb.constraint.isActiveAndEnabled ||
                    !limb.rig || !limb.rig.isActiveAndEnabled || !limb.root || !limb.mid || !limb.tip)
                    continue;
                var data = limb.constraint.data;
                if (!data.target) continue;
                // Offset constraints require their bind-pose offsets; leave those to the
                // original rotation mirror instead of inventing a different contact target.
                if (data.maintainTargetPositionOffset || data.maintainTargetRotationOffset) continue;
                float influence = Mathf.Clamp01(limb.rig.weight * limb.constraint.weight);
                if (influence <= 0f) continue;
                float positionWeight = influence * data.targetPositionWeight;
                if (positionWeight > 0f)
                    SolveContact(limb.root, limb.mid, limb.tip, Vector3.Lerp(limb.tip.position, data.target.position, positionWeight),
                        data.hint, data.hintWeight * influence);
                limb.tip.rotation = Quaternion.Slerp(limb.tip.rotation, data.target.rotation,
                    influence * data.targetRotationWeight);
            }
        }

        private void ApplyCarryStrideDirection(ThirdPersonPlayerController mover)
        {
            if (!mover || !mover.UsesCarryStrideDirection) { carryStrideYaw = 0f; return; }
            Vector3 velocity = mover.LocomotionVelocity;
            float yaw = 0f;
            if (velocity.sqrMagnitude > .000025f)
            {
                // Reverse the cycle for backward steps, then orient its stride plane
                // toward travel without rotating the carrying torso or hand contacts.
                // Match the controller's directional classification even at low speeds.
                if (Vector3.Dot(velocity.normalized, mover.transform.forward) < -.1f) velocity = -velocity;
                yaw = Vector3.SignedAngle(mover.transform.forward, velocity, Vector3.up);
            }
            carryStrideYaw = Mathf.LerpAngle(carryStrideYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime / .10f));
            Quaternion rotation = Quaternion.AngleAxis(carryStrideYaw, Vector3.up);
            foreach (var pair in bonePairs)
                if (pair.target && (pair.target.name == "thigh.L" || pair.target.name == "thigh.R"))
                    pair.target.rotation = rotation * pair.target.rotation;
        }

        private void CacheLimbs()
        {
            cachedAnimator = sourceAnimator;
            var mapped = new Dictionary<Transform, Transform>();
            foreach (var pair in bonePairs)
                if (pair.source && pair.target) mapped[pair.source] = pair.target;
            var found = new List<RetargetedLimb>();
            if (sourceAnimator)
                foreach (var constraint in sourceAnimator.GetComponentsInChildren<TwoBoneIKConstraint>(true))
                {
                    var data = constraint.data;
                    if (!data.root || !data.mid || !data.tip ||
                        !mapped.TryGetValue(data.root, out var root) ||
                        !mapped.TryGetValue(data.mid, out var mid) ||
                        !mapped.TryGetValue(data.tip, out var tip)) continue;
                    found.Add(new RetargetedLimb {
                        constraint = constraint, rig = constraint.GetComponentInParent<Rig>(),
                        root = root, mid = mid, tip = tip
                    });
                }
            limbs = found.ToArray();
        }

        internal static void SolveContact(Transform root, Transform mid, Transform tip, Vector3 target,
            Transform hint, float hintWeight, Vector3 preferredBend = default)
        {
            Vector3 origin = root.position;
            float upper = Vector3.Distance(origin, mid.position);
            float lower = Vector3.Distance(mid.position, tip.position);
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (upper < .0001f || lower < .0001f || distance < .0001f) return;
            Vector3 direction = delta / distance;
            float solvedDistance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + .00001f,
                upper + lower - .00001f);
            Vector3 bend = Vector3.ProjectOnPlane(mid.position - origin, direction);
            if (preferredBend.sqrMagnitude > .001f)
                bend = Vector3.ProjectOnPlane(preferredBend, direction);
            if (hint && hintWeight > 0f)
            {
                Vector3 hinted = Vector3.ProjectOnPlane(hint.position - origin, direction);
                if (hinted.sqrMagnitude > .000001f)
                    bend = Vector3.Slerp(bend.normalized, hinted.normalized, Mathf.Clamp01(hintWeight));
            }
            if (bend.sqrMagnitude < .000001f)
                bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < .95f ? Vector3.up : Vector3.right);
            float along = (upper * upper + solvedDistance * solvedDistance - lower * lower) / (2f * solvedDistance);
            float away = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 elbow = origin + direction * along + bend.normalized * away;
            root.rotation = Quaternion.FromToRotation(mid.position - origin, elbow - origin) * root.rotation;
            Vector3 reachableTarget = origin + direction * solvedDistance;
            mid.rotation = Quaternion.FromToRotation(tip.position - mid.position,
                reachableTarget - mid.position) * mid.rotation;

            // Imported armatures may have non-uniform rest transforms. A few rotation-only
            // refinements remove residual analytical error without editing lengths or scale.
            for (int i = 0; i < 8 && (tip.position - reachableTarget).sqrMagnitude > .000001f; i++)
            {
                mid.rotation = Quaternion.FromToRotation(tip.position - mid.position,
                    reachableTarget - mid.position) * mid.rotation;
                root.rotation = Quaternion.FromToRotation(tip.position - root.position,
                    reachableTarget - root.position) * root.rotation;
            }
        }
    }
}
