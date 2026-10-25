using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>One visible road surface, with the original individual collision boxes.</summary>
    internal sealed class PavedRoutes
    {
        const float SurfaceHeight = .039f;
        const float Epsilon = .00001f;
        readonly List<Route> routes = new List<Route>();

        sealed class Route
        {
            public Vector3 Center, Size;
            public float Yaw;
            public List<Vector2> Corners;
        }

        public void Add(Vector3 center, Vector3 size, float yaw)
        {
            var corners = new List<Vector2>(4);
            var rotation = Quaternion.Euler(0, yaw, 0);
            // Counterclockwise in the x/z plane; every rectangle interior is left of its edges.
            var offsets = new[]
            {
                new Vector3(-size.x, 0, -size.z), new Vector3(size.x, 0, -size.z),
                new Vector3(size.x, 0, size.z), new Vector3(-size.x, 0, size.z)
            };
            foreach (var offset in offsets)
            {
                var point = center + rotation * (offset * .5f);
                corners.Add(new Vector2(point.x, point.z));
            }
            routes.Add(new Route { Center = center, Size = size, Yaw = yaw, Corners = corners });
        }

        public void Build(Transform parent, Material asphalt)
        {
            var root = new GameObject("Visitor paved routes");
            root.transform.SetParent(parent, false);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();

            for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
            {
                var route = routes[routeIndex];
                AddCollider(root.transform, route);
                var pieces = new List<List<Vector2>> { route.Corners };
                // Keep only the portion not already covered by any preceding route.
                for (int previous = 0; previous < routeIndex && pieces.Count != 0; previous++)
                {
                    var uncovered = new List<List<Vector2>>();
                    foreach (var piece in pieces)
                        SubtractRectangle(piece, routes[previous].Corners, uncovered);
                    pieces = uncovered;
                }

                foreach (var piece in pieces)
                {
                    int first = vertices.Count;
                    foreach (var point in piece)
                    {
                        vertices.Add(new Vector3(point.x, SurfaceHeight, point.y));
                        normals.Add(Vector3.up);
                        uv.Add(point / 3f);
                    }
                    for (int i = 1; i + 1 < piece.Count; i++)
                    {
                        if (Cross(piece[i] - piece[0], piece[i + 1] - piece[0]) <= Epsilon) continue;
                        // Clockwise in x/z gives upward-facing triangles in Unity's x/y/z space.
                        triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i);
                    }
                }
            }

            var mesh = new Mesh { name = "Non-overlapping paved route surface" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            root.AddComponent<PavedRouteMeshOwner>().Mesh = mesh;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = asphalt;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void AddCollider(Transform parent, Route route)
        {
            var go = new GameObject("Visitor paved route");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(route.Center.x, .009f, route.Center.z);
            go.transform.localRotation = Quaternion.Euler(0, route.Yaw, 0);
            go.transform.localScale = new Vector3(route.Size.x, .06f, route.Size.z);
            go.AddComponent<BoxCollider>();
            go.AddComponent<SurfaceTag>().MaterialId = "concrete";
        }

        static void SubtractRectangle(List<Vector2> polygon, List<Vector2> rectangle, List<List<Vector2>> result)
        {
            var remainder = polygon;
            for (int edge = 0; edge < rectangle.Count && remainder.Count >= 3; edge++)
            {
                Split(remainder, rectangle[edge], rectangle[(edge + 1) % rectangle.Count],
                    out var inside, out var outside);
                if (HasArea(outside)) result.Add(outside);
                remainder = inside;
            }
            // The remaining polygon is inside the whole rectangle and already has a visible surface.
        }

        static void Split(List<Vector2> polygon, Vector2 a, Vector2 b,
            out List<Vector2> inside, out List<Vector2> outside)
        {
            inside = new List<Vector2>();
            outside = new List<Vector2>();
            var edge = b - a;
            float length = edge.magnitude;
            var inward = new Vector2(-edge.y, edge.x) / length;
            Vector2 previous = polygon[polygon.Count - 1];
            double previousDistance = SignedDistance(previous, a, edge, length);
            SnapToEdge(ref previous, ref previousDistance, inward);
            foreach (var vertex in polygon)
            {
                Vector2 current = vertex;
                double distance = SignedDistance(current, a, edge, length);
                SnapToEdge(ref current, ref distance, inward);
                if ((previousDistance < 0 && distance > 0) || (previousDistance > 0 && distance < 0))
                {
                    float fraction = (float)(previousDistance / (previousDistance - distance));
                    var intersection = previous + (current - previous) * fraction;
                    Append(inside, intersection); Append(outside, intersection);
                }
                if (distance >= 0) Append(inside, current);
                if (distance <= 0) Append(outside, current);
                previous = current; previousDistance = distance;
            }
            RemoveClosingDuplicate(inside); RemoveClosingDuplicate(outside);
        }

        static double SignedDistance(Vector2 point, Vector2 origin, Vector2 edge, float length)
            => Cross(edge, point - origin) / length;

        static void SnapToEdge(ref Vector2 point, ref double distance, Vector2 inward)
        {
            if (System.Math.Abs(distance) > Epsilon) return;
            point -= inward * (float)distance;
            distance = 0;
        }

        static void Append(List<Vector2> polygon, Vector2 point)
        {
            if (polygon.Count == 0 || (polygon[polygon.Count - 1] - point).sqrMagnitude > Epsilon * Epsilon)
                polygon.Add(point);
        }

        static void RemoveClosingDuplicate(List<Vector2> polygon)
        {
            if (polygon.Count > 1 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude <= Epsilon * Epsilon)
                polygon.RemoveAt(polygon.Count - 1);
        }

        static bool HasArea(List<Vector2> polygon)
        {
            if (polygon.Count < 3) return false;
            double twiceArea = 0;
            for (int i = 1; i + 1 < polygon.Count; i++)
                twiceArea += Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
            return twiceArea > Epsilon;
        }

        static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
    }

    internal sealed class PavedRouteMeshOwner : MonoBehaviour
    {
        public Mesh Mesh;

        void OnDestroy()
        {
            if (!Mesh) return;
            if (Application.isPlaying) Destroy(Mesh);
            else DestroyImmediate(Mesh);
        }
    }
}
