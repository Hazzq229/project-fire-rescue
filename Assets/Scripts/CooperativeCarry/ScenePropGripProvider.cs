using HPlayer;
using System.Collections.Generic;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public sealed class ScenePropGripProvider : GripPointProvider
    {
        private BoxCollider shape;
        private float gripY;
        private bool cooperative;
        private bool surfaceRequired;
        private Vector3 backA, backB, frontA, frontB;
        public bool HasSurfaceGrips { get; private set; }
        public float GripHeight => gripY;
        public Vector3 SingleGripCenter => (backA + backB) * .5f;

        private struct Triangle { public Vector3 a, b, c; }

        public void Configure(BoxCollider collider, float height, bool twoPlayers, bool fitVisibleSurface = false)
        {
            shape = collider; gripY = height; cooperative = twoPlayers;
            surfaceRequired = fitVisibleSurface;
            float spread = Mathf.Min(.19f, shape.size.x * .5f);
            backA = shape.center + new Vector3(-spread, gripY, -shape.size.z * .5f);
            backB = shape.center + new Vector3(spread, gripY, -shape.size.z * .5f);
            frontA = shape.center + new Vector3(spread, gripY, shape.size.z * .5f);
            frontB = shape.center + new Vector3(-spread, gripY, shape.size.z * .5f);
            HasSurfaceGrips = fitVisibleSurface && FitSurface();
            if (fitVisibleSurface && !HasSurfaceGrips)
                Debug.LogWarning("No readable, reachable end-surface grip pair for " + name, this);
        }

        // Cache contact points in object space once. Physics convex hulls can bridge
        // the empty space between table legs, so use the original visible triangles.
        private bool FitSurface()
        {
            var triangles = new List<Triangle>();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : null;
                if (!mesh || !mesh.isReadable) return false;
                var vertices = mesh.vertices; var indices = mesh.triangles;
                var matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                    triangles.Add(new Triangle { a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]] });
            }
            if (triangles.Count == 0) return false;
            // Search near the old height first, but reject surfaces deep inside the
            // bounding volume. This selects the keyboard edge or tabletop edge.
            float initialWidth = Mathf.Min(.14f, shape.size.x * .45f);
            for (int widthStep = 0; widthStep < 3; widthStep++)
            {
                float width = initialWidth * (1f - widthStep * .15f);
                for (int step = 0; step <= 100; step++)
                for (int side = 0; side < (step == 0 ? 1 : 2); side++)
                {
                    float y = shape.center.y + gripY + (side == 0 ? -1f : 1f) * step * shape.size.y / 100f;
                    if (y < shape.center.y - shape.size.y * .5f || y > shape.center.y + shape.size.y * .5f) continue;
                    if (!Pair(triangles, -1f, width, y, out var ba, out var bb)) continue;
                    Vector3 fa = Vector3.zero, fb = Vector3.zero;
                    if (cooperative && !Pair(triangles, 1f, width, y, out fa, out fb)) continue;
                    backA = ba; backB = bb; frontA = fa; frontB = fb;
                    gripY = y - shape.center.y;
                    return true;
                }
            }
            return false;
        }

        private bool Pair(List<Triangle> triangles, float end, float width, float y, out Vector3 a, out Vector3 b)
        {
            bool foundA = SurfaceRay(triangles, shape.center.x + end * width, y, end, out a);
            bool foundB = SurfaceRay(triangles, shape.center.x - end * width, y, end, out b);
            return foundA && foundB;
        }

        private bool SurfaceRay(List<Triangle> triangles, float x, float y, float end, out Vector3 point)
        {
            float edgeZ = shape.center.z + end * shape.size.z * .5f;
            Vector3 origin = new Vector3(x, y, edgeZ + end * .01f);
            Vector3 direction = Vector3.back * end;
            float nearest = float.PositiveInfinity;
            foreach (var triangle in triangles)
            {
                Vector3 edge1 = triangle.b - triangle.a, edge2 = triangle.c - triangle.a;
                Vector3 p = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) < 1e-9f) continue;
                float inverse = 1f / determinant;
                Vector3 offset = origin - triangle.a;
                float u = Vector3.Dot(offset, p) * inverse;
                if (u < -.00001f || u > 1.00001f) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < -.00001f || u + v > 1.00001f) continue;
                float distance = Vector3.Dot(edge2, q) * inverse;
                if (distance >= 0f && distance < nearest) nearest = distance;
            }
            point = origin + direction * (float.IsPositiveInfinity(nearest) ? 0f : nearest);
            return nearest <= .06f;
        }

        public override bool TryGetGripPoints(out Vector3 a, out Vector3 b)
            => Points(null, out a, out b);
        public override bool TryGetGripPoints(InteractionController holder, out Vector3 a, out Vector3 b)
            => Points(holder, out a, out b);

        private bool Points(InteractionController holder, out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            if (!shape || (surfaceRequired && !HasSurfaceGrips)) return false;
            float end = -1f;
            if (cooperative)
            {
                var role = GetComponent<CooperativeCarryObject>().GetRole(holder);
                if (role == CarryRole.None) return false;
                end = role == CarryRole.Front ? 1f : -1f;
            }
            a = transform.TransformPoint(end > 0f ? frontA : backA);
            b = transform.TransformPoint(end > 0f ? frontB : backB);
            return true;
        }
    }
}
