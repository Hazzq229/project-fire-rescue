using System.Collections.Generic;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    public sealed class BoxGripProvider : GripPointProvider
    {
        [SerializeField] private BoxCollider boxCollider;
        [Tooltip("0 = bawah kardus; 1 = atas kardus.")]
        [SerializeField, Range(0f, 1f)] private float gripHeight = 0.5f;
        [Tooltip("0 = sisi -Z; 1 = sisi +Z kardus.")]
        [SerializeField, Range(0f, 1f)] private float gripDepth = 0.5f;

        [Header("Preset arah pegangan bersama")]
        [SerializeField] private SharedGripOrientationProfile sharedOrientationProfile;
        [SerializeField] private GripPresetSize gripPreset = GripPresetSize.Medium;
        [Tooltip("Opsional, derajat tambahan pada frame grip A (sisi -X collider). Awali nol.")]
        [SerializeField] private Vector3 additionalRotationA;
        [Tooltip("Opsional, derajat tambahan pada frame grip B (sisi +X collider). Awali nol.")]
        [SerializeField] private Vector3 additionalRotationB;
        public SharedGripOrientationProfile SharedOrientationProfile => sharedOrientationProfile;
        public GripPresetSize GripPreset => gripPreset;

        [Tooltip("Maximum half spacing on a broad near face, in world metres.")]
        [SerializeField, Min(.05f)] private float maximumHalfSpacing = .14f;

        private struct SurfaceTriangle
        {
            public Vector3 a, b, c;
            public Bounds bounds;
        }
        private SurfaceTriangle[] surface = System.Array.Empty<SurfaceTriangle>();
        private Bounds surfaceBounds;
        private bool geometryUnavailable;
        private bool gripCached, cachedGripValid;
        private Vector3 cachedA, cachedB;
        private Vector4 cachedGripSettings;
        private readonly List<float> intersections = new List<float>();
        public bool HasSurfaceGeometry => surface.Length > 0 && !geometryUnavailable;

        private void Awake()
        {
            if (!boxCollider) boxCollider = GetComponent<BoxCollider>();
            // Imported demo visuals can be much larger than their hidden cube proxy.
            // Derive contact geometry from the visible mesh, preserving its authored scale.
            if (!boxCollider) return;
            bool found = false;
            Bounds bounds = default;
            var triangles = new List<SurfaceTriangle>();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : null;
                if (mesh)
                {
                    if (!mesh.isReadable)
                    {
                        geometryUnavailable = true;
                        Debug.LogWarning("BoxGripProvider requires Read/Write on visible mesh '" +
                            mesh.name + "' for actual surface contacts and clearance.", this);
                    }
                    else
                    {
                        Vector3[] vertices = mesh.vertices;
                        int[] indices = mesh.triangles;
                        Matrix4x4 toBox = boxCollider.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        for (int vertex = 0; vertex < vertices.Length; vertex++)
                            vertices[vertex] = toBox.MultiplyPoint3x4(vertices[vertex]);
                        for (int index = 0; index + 2 < indices.Length; index += 3)
                        {
                            Vector3 a = vertices[indices[index]], b = vertices[indices[index + 1]], c = vertices[indices[index + 2]];
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                            var triangleBounds = new Bounds(a, Vector3.zero);
                            triangleBounds.Encapsulate(b);
                            triangleBounds.Encapsulate(c);
                            triangles.Add(new SurfaceTriangle { a = a, b = b, c = c, bounds = triangleBounds });
                        }
                    }
                }
                var rendererBounds = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = rendererBounds.center + Vector3.Scale(rendererBounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = boxCollider.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (found)
            {
                boxCollider.center = bounds.center;
                boxCollider.size = bounds.size;
                gripDepth = 0f;
                if (bounds.size.y * transform.lossyScale.y < 1f) gripHeight = .85f;
                else gripHeight = .08f;
                surfaceBounds = bounds;
            }
            surface = triangles.ToArray();
        }

        public bool RecordSharedFrames(Quaternion worldFrameA, Quaternion worldFrameB)
        {
            if (!boxCollider || !sharedOrientationProfile) return false;
            Quaternion inverseSpace = Quaternion.Inverse(boxCollider.transform.rotation);
            sharedOrientationProfile.Record(gripPreset,
                inverseSpace * worldFrameA * Quaternion.Inverse(Quaternion.Euler(additionalRotationA)),
                inverseSpace * worldFrameB * Quaternion.Inverse(Quaternion.Euler(additionalRotationB)));
            return true;
        }

        private void Reset() => boxCollider = GetComponent<BoxCollider>();

        public override bool TryGetGripPoints(out Vector3 pointA, out Vector3 pointB)
        {
            pointA = pointB = Vector3.zero;
            if (!boxCollider) return false;
            Vector3 size = boxCollider.size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return false;

            Vector3 center = boxCollider.center;
            if (geometryUnavailable) return false;
            if (HasSurfaceGeometry)
            {
                var settings = new Vector4(gripHeight, maximumHalfSpacing,
                    Mathf.Abs(boxCollider.transform.lossyScale.x), gripDepth);
                if (!gripCached || cachedGripSettings != settings)
                {
                    cachedGripSettings = settings;
                    cachedGripValid = FindSurfaceGrips(out cachedA, out cachedB);
                    gripCached = true;
                }
                if (!cachedGripValid) return false;
                pointA = boxCollider.transform.TransformPoint(cachedA);
                pointB = boxCollider.transform.TransformPoint(cachedB);
                return true;
            }
            float y = center.y + (gripHeight - 0.5f) * size.y;
            float z = center.z + (gripDepth - 0.5f) * size.z;
            float halfWidth = size.x * .5f;
            // Broad furniture is held on its near face, not at unreachable outer corners.
            if (gripDepth <= .001f)
                halfWidth = Mathf.Min(halfWidth, maximumHalfSpacing / Mathf.Max(.0001f, boxCollider.transform.lossyScale.x));
            pointA = boxCollider.transform.TransformPoint(
                new Vector3(center.x - halfWidth, y, z));
            pointB = boxCollider.transform.TransformPoint(
                new Vector3(center.x + halfWidth, y, z));
            return true;
        }

        private bool FindSurfaceGrips(out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            float desiredY = Mathf.Lerp(surfaceBounds.min.y, surfaceBounds.max.y, gripHeight);
            float scaleX = Mathf.Max(.0001f, Mathf.Abs(boxCollider.transform.lossyScale.x));
            float startWidth = Mathf.Min(surfaceBounds.extents.x * .98f, maximumHalfSpacing / scaleX);
            float minimumWidth = Mathf.Min(startWidth, .14f / scaleX);
            // Search heights nearest the requested height first. A sign/pole above a broad
            // body must not produce contacts in the empty space beside that narrow detail.
            for (int widthStep = 0; widthStep <= 2; widthStep++)
            {
                float width = Mathf.Lerp(startWidth, minimumWidth, widthStep / 2f);
                for (int step = 0; step <= 80; step++)
                    for (int side = 0; side < (step == 0 ? 1 : 2); side++)
                    {
                        float y = desiredY + (side == 0 ? -1f : 1f) * step * surfaceBounds.size.y / 80f;
                        if (y < surfaceBounds.min.y || y > surfaceBounds.max.y) continue;
                        if (TrySurfaceRay(surfaceBounds.center.x - width, y, out a) &&
                            TrySurfaceRay(surfaceBounds.center.x + width, y, out b)) return true;
                    }
            }
            return false;
        }

        private bool TrySurfaceRay(float x, float y, out Vector3 point)
        {
            Vector3 origin = new Vector3(x, y, surfaceBounds.min.z - .01f);
            float nearest = float.PositiveInfinity;
            foreach (var triangle in surface)
            {
                if (x < triangle.bounds.min.x - .00001f || x > triangle.bounds.max.x + .00001f ||
                    y < triangle.bounds.min.y - .00001f || y > triangle.bounds.max.y + .00001f) continue;
                if (RayTriangle(origin, triangle, out float distance) && distance < nearest) nearest = distance;
            }
            point = origin + Vector3.forward * (float.IsPositiveInfinity(nearest) ? 0f : nearest);
            return !float.IsPositiveInfinity(nearest);
        }

        private static bool RayTriangle(Vector3 origin, SurfaceTriangle triangle, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = triangle.b - triangle.a, edge2 = triangle.c - triangle.a;
            Vector3 p = Vector3.Cross(Vector3.forward, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f) return false;
            float inverse = 1f / determinant;
            Vector3 offset = origin - triangle.a;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < -.00001f || u > 1.00001f) return false;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = q.z * inverse;
            if (v < -.00001f || u + v > 1.00001f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }

        // Returns a world-space object translation, not a change to the sphere or IK target.
        public Vector3 GetSurfaceClearance(Vector3 sphereCenter, float radius, Vector3 objectTranslation)
        {
            if (!HasSurfaceGeometry || radius <= 0f) return Vector3.zero;
            Transform space = boxCollider.transform;
            Vector3 localCenter = space.InverseTransformPoint(sphereCenter - objectTranslation);
            Vector3 scale = space.lossyScale;
            float minimumScale = Mathf.Max(.00001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            if (surfaceBounds.SqrDistance(localCenter) * minimumScale * minimumScale > radius * radius)
                return Vector3.zero;
            float nearestSquared = float.PositiveInfinity;
            Vector3 closest = Vector3.zero;
            foreach (var triangle in surface)
            {
                if (triangle.bounds.SqrDistance(localCenter) * minimumScale * minimumScale > nearestSquared) continue;
                Vector3 a = space.TransformPoint(triangle.a) + objectTranslation;
                Vector3 b = space.TransformPoint(triangle.b) + objectTranslation;
                Vector3 c = space.TransformPoint(triangle.c) + objectTranslation;
                Vector3 candidate = ClosestTrianglePoint(sphereCenter, a, b, c);
                float squared = (candidate - sphereCenter).sqrMagnitude;
                if (squared < nearestSquared) { nearestSquared = squared; closest = candidate; }
            }
            // Deduplicate shared-edge hits so a closed triangulated face counts once.
            intersections.Clear();
            foreach (var triangle in surface)
                if (RayTriangle(localCenter, triangle, out float distance) && distance > .00001f)
                    intersections.Add(distance);
            intersections.Sort();
            int crossings = 0;
            float previous = float.NegativeInfinity;
            foreach (float distance in intersections)
                if (distance - previous > .00001f) { crossings++; previous = distance; }
            bool inside = (crossings & 1) != 0;
            float separation = Mathf.Sqrt(nearestSquared);
            if (!inside && separation >= radius) return Vector3.zero;
            Vector3 away = closest - sphereCenter;
            if (separation < .00001f) away = space.forward;
            else away /= separation;
            return inside ? -away * (radius + separation) : away * (radius - separation);
        }

        private static Vector3 ClosestTrianglePoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float inverse = 1f / (va + vb + vc);
            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }

        public override string GripStyleId => "box-side";

        // +Z frame pegangan mengarah ke dalam kardus; +Y mengikuti atas kardus.
        // A pada sisi -X, B pada sisi +X. Asumsi skala positif tanpa shear.
        public override bool TryGetGripRotations(out Quaternion rotationA, out Quaternion rotationB)
        {
            rotationA = rotationB = Quaternion.identity;
            if (!boxCollider) return false;
            Quaternion boxRotation = boxCollider.transform.rotation;
            Quaternion localA = Quaternion.LookRotation(Vector3.right, Vector3.up);
            Quaternion localB = Quaternion.LookRotation(Vector3.left, Vector3.up);
            if (sharedOrientationProfile && !sharedOrientationProfile.TryGet(gripPreset, out localA, out localB))
                return false;
            rotationA = boxRotation * localA * Quaternion.Euler(additionalRotationA);
            rotationB = boxRotation * localB * Quaternion.Euler(additionalRotationB);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetGripPoints(out Vector3 a, out Vector3 b)) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(a, 0.025f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(b, 0.025f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
        }
    }
}
