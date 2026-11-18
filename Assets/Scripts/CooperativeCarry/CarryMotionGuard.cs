using System;
using System.Collections.Generic;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>
    /// Limits a rigid group's proposed poses by one common fraction. Callers apply
    /// that fraction to every body so an obstruction cannot separate held contacts.
    /// </summary>
    public sealed class CarryMotionGuard
    {
        private const float SampleTravel = .005f;
        private const float Skin = .0025f;
        private const float ContactTolerance = .002f;
        private const float ExistingOverlapTolerance = .00001f;

        private sealed class BodyState
        {
            public Rigidbody body;
            public ShapeState[] shapes;
            public Collider[] triggers;
            public bool boxOnly;
            public Vector3 position;
            public Quaternion rotation;
            public float radius;
        }

        private sealed class ShapeState
        {
            public Collider collider;
            public Vector3 localPosition, localCenter;
            public Quaternion localRotation;
            public float radius;
            public readonly Dictionary<Collider, float> initialDepths = new Dictionary<Collider, float>();
        }

        private readonly BodyState[] states;
        private Collider[] overlaps = new Collider[32];
        private readonly ICollection<Rigidbody> supportedLoads;

        public CarryMotionGuard(params Rigidbody[] bodies) : this(null, bodies) { }

        public CarryMotionGuard(ICollection<Rigidbody> supportedLoads, params Rigidbody[] bodies)
        {
            this.supportedLoads = supportedLoads;
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));
            states = new BodyState[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                var shapes = new List<ShapeState>();
                var triggers = new List<Collider>();
                bool boxOnly = true;
                if (body)
                    foreach (var collider in body.GetComponentsInChildren<Collider>(true))
                        if (collider.attachedRigidbody == body)
                        {
                            if (collider.isTrigger) triggers.Add(collider);
                            else if (collider.enabled)
                            {
                                shapes.Add(new ShapeState { collider = collider });
                                boxOnly &= collider is BoxCollider;
                            }
                        }
                states[i] = new BodyState { body = body, shapes = shapes.ToArray(), triggers = triggers.ToArray(), boxOnly = boxOnly };
            }
        }

        public float Limit(Vector3[] targetPositions, Quaternion[] targetRotations)
            => Limit(targetPositions, targetRotations, -1, -1);

        // A parented item follows the holder's pivot arc, not the straight chord
        // between its old/new root positions during a sharp character turn.
        public float LimitFollowingPivot(Vector3[] targetPositions, Quaternion[] targetRotations, int follower, int pivot)
            => Limit(targetPositions, targetRotations, follower, pivot);

        private float Limit(Vector3[] targetPositions, Quaternion[] targetRotations, int follower, int pivot)
        {
            if (targetPositions == null || targetRotations == null ||
                targetPositions.Length != states.Length || targetRotations.Length != states.Length)
                throw new ArgumentException("Provide one target position and rotation for each guarded body.");

            float travel = 0f;
            for (int i = 0; i < states.Length; i++)
            {
                if (!Finite(targetPositions[i]) || !Finite(targetRotations[i])) return 0f;
                var state = states[i];
                if (!state.body) continue;
                state.position = state.body.position;
                state.rotation = state.body.rotation;
                state.radius = 0f;
                // Read relative transforms each call: player child colliders may animate,
                // and render interpolation need not equal the Rigidbody's physics pose.
                var root = state.body.transform;
                var inverse = Quaternion.Inverse(root.rotation);
                foreach (var shape in state.shapes)
                {
                    shape.initialDepths.Clear();
                    if (!Active(shape.collider)) continue;
                    shape.localPosition = inverse * (shape.collider.transform.position - root.position);
                    shape.localRotation = inverse * shape.collider.transform.rotation;
                    shape.localCenter = inverse * (shape.collider.bounds.center - root.position);
                    shape.radius = shape.collider.bounds.extents.magnitude;
                    state.radius = Mathf.Max(state.radius, shape.localCenter.magnitude + shape.radius);
                    CaptureInitialOverlaps(state, shape);
                }
                travel = Mathf.Max(travel, Vector3.Distance(state.position, targetPositions[i]) +
                    Quaternion.Angle(state.rotation, targetRotations[i]) * Mathf.Deg2Rad * state.radius);
            }
            if (follower >= 0 && pivot >= 0)
                travel = Mathf.Max(travel, Vector3.Distance(states[pivot].position, targetPositions[pivot]) +
                    Quaternion.Angle(states[pivot].rotation, targetRotations[pivot]) * Mathf.Deg2Rad *
                    (Vector3.Distance(states[follower].position, states[pivot].position) + states[follower].radius));
            if (travel < .000001f) return 1f;

            float upper = TranslationLimit(targetPositions, targetRotations, follower, pivot);
            if (upper <= 0f) return 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(travel * upper / SampleTravel));
            float previous = 0f;
            for (int step = 1; step <= steps; step++)
            {
                float fraction = upper * step / steps;
                if (!Blocked(fraction, targetPositions, targetRotations, follower, pivot))
                {
                    previous = fraction;
                    continue;
                }
                float low = previous, high = fraction;
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    float middle = (low + high) * .5f;
                    if (Blocked(middle, targetPositions, targetRotations, follower, pivot)) high = middle;
                    else low = middle;
                }
                // Leave a skin rather than accumulating permitted overlap each frame.
                return Mathf.Max(0f, low - Skin / travel);
            }
            return upper;
        }

        private void CaptureInitialOverlaps(BodyState body, ShapeState shape)
        {
            Vector3 position = body.position + body.rotation * shape.localPosition;
            Quaternion rotation = body.rotation * shape.localRotation;
            int count = Query(body.position + body.rotation * shape.localCenter, shape.radius + Skin);
            for (int i = 0; i < count; i++)
            {
                var other = overlaps[i];
                if (Ignore(shape.collider, other)) continue;
                if (Physics.ComputePenetration(shape.collider, position, rotation, other,
                    other.transform.position, other.transform.rotation, out _, out float depth))
                    shape.initialDepths[other] = depth;
            }
        }

        private float TranslationLimit(Vector3[] targets, Quaternion[] rotations, int follower, int pivot)
        {
            float fraction = 1f;
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (!state.body || state.shapes.Length == 0) continue;
                if (i == follower && pivot >= 0 && Quaternion.Angle(states[pivot].rotation, rotations[pivot]) > .01f) continue;
                Vector3 delta = targets[i] - state.position;
                float distance = delta.magnitude;
                if (distance < .000001f) continue;
                Vector3 direction = delta / distance;
                // Rigidbody sweeps cast attached triggers too; Ignore only filters
                // target triggers. Box compounds can sweep their actual solids
                // directly, without disabling the interaction volumes or changing
                // the existing exact compound sweep for other collider types.
                bool hasTrigger = false;
                foreach (var trigger in state.triggers) hasTrigger |= Active(trigger);
                if (state.boxOnly && hasTrigger)
                {
                    foreach (var shape in state.shapes)
                    {
                        if (!Active(shape.collider)) continue;
                        var box = (BoxCollider)shape.collider;
                        Vector3 scale = box.transform.lossyScale;
                        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        foreach (var hit in Physics.BoxCastAll(state.position + state.rotation * shape.localCenter,
                            Vector3.Scale(box.size * .5f, scale), direction, state.rotation * shape.localRotation,
                            distance + Skin, ~0, QueryTriggerInteraction.Ignore))
                        {
                            // Initial overlaps are governed by the sampled depth
                            // checks, so an existing contact can still retreat.
                            if (Ignore(box, hit.collider) || shape.initialDepths.ContainsKey(hit.collider) ||
                                Vector3.Dot(hit.normal, direction) >= -.01f) continue;
                            fraction = Mathf.Min(fraction, Mathf.Max(0f, hit.distance - Skin) / distance);
                        }
                    }
                    continue;
                }
                foreach (var hit in state.body.SweepTestAll(direction, distance + Skin, QueryTriggerInteraction.Ignore))
                {
                    // Support contacts tangential to travel must not freeze the group.
                    if (Vector3.Dot(hit.normal, direction) >= -.01f) continue;
                    bool collidable = false;
                    foreach (var shape in state.shapes)
                        if (Active(shape.collider) && !Ignore(shape.collider, hit.collider))
                        { collidable = true; break; }
                    if (!collidable) continue;
                    fraction = Mathf.Min(fraction, Mathf.Max(0f, hit.distance - Skin) / distance);
                }
            }
            return fraction;
        }

        private bool Blocked(float fraction, Vector3[] positions, Quaternion[] rotations, int follower, int pivot)
        {
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (!state.body) continue;
                Vector3 rootPosition = Vector3.Lerp(state.position, positions[i], fraction);
                Quaternion rootRotation = Quaternion.Slerp(state.rotation, rotations[i], fraction);
                if (i == follower && pivot >= 0)
                {
                    Quaternion turn = Quaternion.Slerp(states[pivot].rotation, rotations[pivot], fraction) *
                        Quaternion.Inverse(states[pivot].rotation);
                    rootPosition = Vector3.Lerp(states[pivot].position, positions[pivot], fraction) +
                        turn * (state.position - states[pivot].position);
                }
                foreach (var shape in state.shapes)
                {
                    if (!Active(shape.collider)) continue;
                    Vector3 position = rootPosition + rootRotation * shape.localPosition;
                    Quaternion rotation = rootRotation * shape.localRotation;
                    int count = Query(rootPosition + rootRotation * shape.localCenter, shape.radius + Skin);
                    for (int hit = 0; hit < count; hit++)
                    {
                        var other = overlaps[hit];
                        if (Ignore(shape.collider, other)) continue;
                        if (!Physics.ComputePenetration(shape.collider, position, rotation, other,
                            other.transform.position, other.transform.rotation, out _, out float depth)) continue;
                        shape.initialDepths.TryGetValue(other, out float initialDepth);
                        if (depth > Mathf.Max(ContactTolerance, initialDepth + ExistingOverlapTolerance)) return true;
                    }
                }
            }
            return false;
        }

        private int Query(Vector3 center, float radius)
        {
            int count;
            // Retry a full buffer rather than silently missing a blocking collider.
            while ((count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, ~0,
                QueryTriggerInteraction.Ignore)) == overlaps.Length)
                Array.Resize(ref overlaps, overlaps.Length * 2);
            return count;
        }

        private bool Ignore(Collider own, Collider other)
        {
            if (!Active(other) || other.isTrigger || other == own) return true;
            // Loads on the tabletop remain solid in PhysX and ride on its surface.
            if (supportedLoads != null && other.attachedRigidbody && supportedLoads.Contains(other.attachedRigidbody)) return true;
            foreach (var state in states)
                if (state.body && other.transform.IsChildOf(state.body.transform)) return true;
            return Physics.GetIgnoreLayerCollision(own.gameObject.layer, other.gameObject.layer) ||
                Physics.GetIgnoreCollision(own, other);
        }

        private static bool Active(Collider collider) => collider && collider.enabled && collider.gameObject.activeInHierarchy;
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
