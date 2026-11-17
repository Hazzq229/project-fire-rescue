using System;
using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Fits a pair of barrel contacts to the visible cylinder mesh.</summary>
    [DisallowMultipleComponent]
    public sealed class CylinderGripProvider : GripPointProvider
    {
        [SerializeField] private MeshFilter sourceMesh;
        [Tooltip("Cylinder length axis in the mesh's own coordinates: X=0, Y=1, Z=2.")]
        [SerializeField, Range(0, 2)] private int axis = 2;
        [SerializeField, Range(0f, 1f)] private float gripHeight = .35f;
        [SerializeField, Min(.01f)] private float maximumHalfSpacing = .14f;

        private struct Triangle { public Vector3 a, b, c; }
        private Triangle[] triangles = Array.Empty<Triangle>();
        private Mesh cachedMesh;
        private bool cachedReadable;
        private Bounds bounds;

        public override string GripStyleId => "cylinder-side";
        public bool HasSurfaceGeometry => EnsureGeometry();
        public Vector3 ObjectDimensions
        {
            get
            {
                if (!sourceMesh) sourceMesh = GetComponent<MeshFilter>();
                if (!sourceMesh || !sourceMesh.sharedMesh) return Vector3.zero;
                Vector3 scale = sourceMesh.transform.lossyScale;
                return Vector3.Scale(sourceMesh.sharedMesh.bounds.size,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            }
        }

        public void Configure(MeshFilter mesh, int lengthAxis, float height = .35f)
        {
            sourceMesh = mesh;
            axis = Mathf.Clamp(lengthAxis, 0, 2);
            gripHeight = Finite(height) ? Mathf.Clamp01(height) : .35f;
            cachedMesh = null;
            triangles = Array.Empty<Triangle>();
        }

        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b) =>
            TryGetGripPoints(null, out a, out b);

        public override bool TryGetGripPoints(InteractionController holder, out Vector3 a, out Vector3 b) =>
            Sample(holder, out a, out b, out _, out _, out _);

        public override bool TryGetGripRotations(out Quaternion a, out Quaternion b) =>
            TryGetGripRotations(null, out a, out b);

        public override bool TryGetGripRotations(InteractionController holder, out Quaternion a, out Quaternion b)
        {
            a = b = Quaternion.identity;
            if (!Sample(holder, out _, out _, out Vector3 normalA, out Vector3 normalB, out Vector3 lengthDirection))
                return false;
            // Same canonical frame as the box provider: +Z points into the object.
            a = Quaternion.LookRotation(-normalA, lengthDirection);
            b = Quaternion.LookRotation(-normalB, lengthDirection);
            return true;
        }

        private bool EnsureGeometry()
        {
            if (!sourceMesh) sourceMesh = GetComponent<MeshFilter>();
            if (!sourceMesh || !sourceMesh.sharedMesh) return false;
            var mesh = sourceMesh.sharedMesh;
            if (mesh == cachedMesh && mesh.isReadable == cachedReadable) return triangles.Length > 0;
            cachedMesh = mesh;
            cachedReadable = mesh.isReadable;
            triangles = Array.Empty<Triangle>();
            if (!cachedReadable) return false;
            bounds = mesh.bounds;
            if (!Finite(bounds.center) || !Finite(bounds.size) || bounds.size.sqrMagnitude < 1e-12f) return false;
            var vertices = mesh.vertices;
            var indices = mesh.triangles;
            var valid = new System.Collections.Generic.List<Triangle>(indices.Length / 3);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                if (!Finite(a) || !Finite(b) || !Finite(c) || Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-20f)
                    continue;
                valid.Add(new Triangle { a = a, b = b, c = c });
            }
            triangles = valid.ToArray();
            return triangles.Length > 0;
        }

        private bool Sample(InteractionController holder, out Vector3 a, out Vector3 b,
            out Vector3 normalA, out Vector3 normalB, out Vector3 lengthDirection)
        {
            a = b = normalA = normalB = lengthDirection = Vector3.zero;
            if (!EnsureGeometry() || !Finite(gripHeight) || !Finite(maximumHalfSpacing) || maximumHalfSpacing <= 0f)
                return false;
            int lengthAxis = Mathf.Clamp(axis, 0, 2);
            Vector3 localAxis = Vector3.zero; localAxis[lengthAxis] = 1f;
            Transform space = sourceMesh.transform;
            lengthDirection = space.TransformVector(localAxis).normalized;
            if (!Finite(lengthDirection) || lengthDirection.sqrMagnitude < .99f) return false;
            Vector3 localCenter = bounds.center;
            localCenter[lengthAxis] = Mathf.Lerp(bounds.min[lengthAxis], bounds.max[lengthAxis], Mathf.Clamp01(gripHeight));
            Vector3 center = space.TransformPoint(localCenter);
            Transform actorSpace = holder ? holder.transform : transform;
            // Aim at torso height rather than the actor's feet. A horizontal
            // cylinder otherwise puts both contacts underneath the barrel and
            // below the short stylized arms' reachable range.
            var body = holder ? holder.GetComponent<CapsuleCollider>() : null;
            Vector3 actorCenter = body ? body.bounds.center : actorSpace.position;
            Vector3 near = holder ? Vector3.ProjectOnPlane(actorCenter - center, lengthDirection) : Vector3.zero;
            if (near.sqrMagnitude < 1e-8f) near = Vector3.ProjectOnPlane(-actorSpace.forward, lengthDirection);
            if (near.sqrMagnitude < 1e-8f) near = Vector3.ProjectOnPlane(-actorSpace.up, lengthDirection);
            if (near.sqrMagnitude < 1e-8f) return false;
            near.Normalize();
            Vector3 tangent = Vector3.Cross(lengthDirection, near).normalized;
            if (Vector3.Dot(tangent, actorSpace.right) < 0f) tangent = -tangent;

            int radialA = (lengthAxis + 1) % 3, radialB = (lengthAxis + 2) % 3;
            Vector3 radiusA = Vector3.zero, radiusB = Vector3.zero;
            radiusA[radialA] = bounds.extents[radialA]; radiusB[radialB] = bounds.extents[radialB];
            float radius = Mathf.Min(space.TransformVector(radiusA).magnitude, space.TransformVector(radiusB).magnitude);
            if (!Finite(radius) || radius < .0001f || !Finite(center)) return false;
            float angle = Mathf.Asin(Mathf.Clamp(Mathf.Min(radius * .75f, maximumHalfSpacing) / radius, .01f, .9f));
            float castDistance = ObjectDimensions.magnitude * 2f + .01f;
            // A nonuniformly scaled barrel may have a wider tangent radius than
            // the initial estimate. Limit actual world spacing after surface hits.
            for (int attempt = 0; attempt < 8; attempt++, angle *= .65f)
            {
                Vector3 radialLeft = near * Mathf.Cos(angle) - tangent * Mathf.Sin(angle);
                Vector3 radialRight = near * Mathf.Cos(angle) + tangent * Mathf.Sin(angle);
                if (!SurfaceHit(center, radialLeft, castDistance, out a, out normalA) ||
                    !SurfaceHit(center, radialRight, castDistance, out b, out normalB)) continue;
                float spacing = Vector3.Distance(a, b);
                if (spacing <= .001f || spacing > maximumHalfSpacing * 2f + .0005f) continue;
                // Enforce anatomical ordering even for an off-center authored mesh.
                if (Vector3.Dot(b - a, actorSpace.right) < 0f)
                { var point = a; a = b; b = point; var normal = normalA; normalA = normalB; normalB = normal; }
                return true;
            }
            a = b = normalA = normalB = Vector3.zero;
            return false;
        }

        private bool SurfaceHit(Vector3 center, Vector3 outward, float distance, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            Transform space = sourceMesh.transform;
            Vector3 origin = space.InverseTransformPoint(center + outward * distance);
            // Keep the inverse-transformed direction unnormalized, so ray t is
            // still measured in world metres under nonuniform scale.
            Vector3 direction = space.InverseTransformVector(-outward);
            float nearest = distance + .0001f;
            Vector3 localNormal = Vector3.zero;
            bool found = false;
            foreach (var triangle in triangles)
            {
                Vector3 e1 = triangle.b - triangle.a, e2 = triangle.c - triangle.a;
                Vector3 p = Vector3.Cross(direction, e2);
                float determinant = Vector3.Dot(e1, p);
                if (Mathf.Abs(determinant) < 1e-12f) continue;
                float inverse = 1f / determinant;
                Vector3 offset = origin - triangle.a;
                float u = Vector3.Dot(offset, p) * inverse;
                if (u < -.00001f || u > 1.00001f) continue;
                Vector3 q = Vector3.Cross(offset, e1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < -.00001f || u + v > 1.00001f) continue;
                float hit = Vector3.Dot(e2, q) * inverse;
                if (hit < 0f || hit >= nearest) continue;
                nearest = hit;
                localNormal = Vector3.Cross(e1, e2).normalized;
                found = true;
            }
            if (!found) return false;
            point = space.TransformPoint(origin + direction * nearest);
            normal = space.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
            return Finite(point) && Finite(normal) && normal.sqrMagnitude > .99f;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
