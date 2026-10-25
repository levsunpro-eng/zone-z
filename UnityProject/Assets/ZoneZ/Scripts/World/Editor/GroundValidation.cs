#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZoneZ.Editor
{
    /// <summary>Geometry checks for ground depth fighting, road holes and decorative grass collision.</summary>
    internal static class GroundValidation
    {
        const float EdgeTolerance = .0002f;

        internal static void Run(ZooWorld world, List<string> errors)
        {
            var filters = world.GetComponentsInChildren<MeshFilter>();
            var soil = filters.FirstOrDefault(x => x.name == "Whole playable ground");
            var lawn = filters.FirstOrDefault(x => x.name == "Zoo grass field");
            if (!soil || !soil.sharedMesh || !lawn || !lawn.sharedMesh)
                errors.Add("Soil and lawn must both have visible ground geometry.");
            else
                Require(Top(lawn) - Top(soil) >= .05f,
                    "Lawn and soil top faces must be separated by at least 5 cm to prevent depth fighting.", errors);

            var routes = filters.Where(x => x.name == "Visitor paved routes").ToArray();
            Require(routes.Length == 1 && routes[0].sharedMesh,
                "Expected one visible paved route mesh.", errors);
            if (routes.Length == 1 && routes[0].sharedMesh)
                ValidateRoutes(routes[0], lawn, errors);

            // Runtime art is internal to Assembly-CSharp; editor validation must not depend on its type visibility.
            var grass = world.GetComponentsInChildren<MonoBehaviour>()
                .FirstOrDefault(x => x && x.GetType().Name == "GrassArt");
            Require(grass, "The lawn has no decorative grass.", errors);
            if (grass)
            {
                Require(grass.GetComponentsInChildren<Collider>(true).Length == 0,
                    "Decorative grass must not add collision or affect the navigation mesh.", errors);
                var grassMeshes = grass.GetComponentsInChildren<MeshFilter>();
                int vertices = grassMeshes.Where(x => x.sharedMesh).Sum(x => x.sharedMesh.vertexCount);
                Require(vertices > 0 && vertices <= 350000,
                    "Decorative grass must contain visible geometry within the 350k vertex budget.", errors);
                Require(grassMeshes.Any(x => x.sharedMesh && x.GetComponent<MeshRenderer>() &&
                    x.GetComponent<MeshRenderer>().enabled && x.GetComponent<MeshRenderer>().sharedMaterial),
                    "Decorative grass has no enabled renderer with a material.", errors);
            }
        }

        static void ValidateRoutes(MeshFilter filter, MeshFilter lawn, List<string> errors)
        {
            var colliders = filter.GetComponentsInChildren<BoxCollider>();
            Require(colliders.Length == 11, "Paved routes must preserve all 11 collision footprints.", errors);
            foreach (var collider in colliders)
            {
                var renderer = collider.GetComponent<Renderer>();
                Require(!renderer || !renderer.enabled,
                    "Route collision boxes must not also render overlapping road surfaces.", errors);
                var tag = collider.GetComponent<SurfaceTag>();
                Require(tag && tag.MaterialId == "concrete",
                    "Paved route collision must keep its concrete surface type.", errors);
            }

            var mesh = filter.sharedMesh;
            var positions = mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            var uv = mesh.uv;
            var indices = mesh.triangles;
            Require(positions.Length > 0 && indices.Length > 0,
                "Paved route mesh is empty.", errors);
            if (positions.Length == 0 || indices.Length == 0) return;
            float height = positions[0].y;
            Require(positions.All(p => Mathf.Abs(p.y - height) < .0001f),
                "Paved route triangles must share one level surface.", errors);
            if (lawn && lawn.sharedMesh)
                Require(height - Top(lawn) >= .025f,
                    "Paved routes need clearance above the lawn to prevent depth fighting.", errors);
            Require(colliders.All(c => Mathf.Abs(c.bounds.max.y - height) < .0001f),
                "Paved route rendering and collision top surfaces disagree.", errors);
            Require(uv.Length == positions.Length && positions.Select((p, i) =>
                    uv.Length == positions.Length && (uv[i] - new Vector2(p.x / 3f, p.z / 3f)).sqrMagnitude < .000001f).All(x => x),
                "Paved route UVs must remain continuous at junctions with a three-meter world-space tile.", errors);

            var triangles = new List<FootprintTriangle>();
            bool upward = true;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = positions[indices[i]];
                var b = positions[indices[i + 1]];
                var c = positions[indices[i + 2]];
                if (Vector3.Cross(b - a, c - a).y <= 0) { upward = false; continue; }
                triangles.Add(new FootprintTriangle(a, b, c));
            }
            Require(upward, "Every paved route triangle must face upward and have nonzero area.", errors);

            // Compare visible triangle coverage to independent physics footprints. Jitter avoids a grid
            // repeatedly landing on diagonals; samples near any edge are explicitly excluded.
            var footprints = colliders.Select(x => new CollisionFootprint(x)).ToArray();
            int samples = 0, mismatches = 0;
            Vector2 firstMismatch = default;
            var random = new System.Random(9413);
            for (float x = -73; x < 73; x += .83f)
                for (float z = -58; z < 58; z += .83f)
                {
                    var point = new Vector2(x + (float)random.NextDouble() * .7f,
                        z + (float)random.NextDouble() * .7f);
                    Sample(point, footprints, triangles, ref samples, ref mismatches, ref firstMismatch);
                }
            // Triangle centroids also exercise narrow pieces created at angled junctions.
            foreach (var triangle in triangles)
                Sample(triangle.Centroid, footprints, triangles, ref samples, ref mismatches, ref firstMismatch);
            Require(samples > 20000, "Too few unambiguous road coverage samples were checked.", errors);
            Require(mismatches == 0,
                $"Paved route coverage has {mismatches} holes, overlaps or extensions outside collision; first near x/z={firstMismatch}.", errors);
        }

        static void Sample(Vector2 point, CollisionFootprint[] footprints, List<FootprintTriangle> triangles,
            ref int samples, ref int mismatches, ref Vector2 firstMismatch)
        {
            bool expected = false, ambiguous = false;
            foreach (var footprint in footprints)
                expected |= footprint.Contains(point, ref ambiguous);
            int coverage = 0;
            foreach (var triangle in triangles)
                if (triangle.Contains(point, ref ambiguous)) coverage++;
            if (ambiguous) return;
            samples++;
            if (coverage == (expected ? 1 : 0)) return;
            if (mismatches++ == 0) firstMismatch = point;
        }

        readonly struct CollisionFootprint
        {
            readonly Matrix4x4 inverse;
            readonly Vector3 center, halfSize;
            readonly Vector2 tolerance;
            internal CollisionFootprint(BoxCollider collider)
            {
                inverse = collider.transform.worldToLocalMatrix;
                center = collider.center;
                halfSize = collider.size * .5f;
                var scale = collider.transform.lossyScale;
                tolerance = new Vector2(EdgeTolerance / Mathf.Abs(scale.x), EdgeTolerance / Mathf.Abs(scale.z));
            }
            internal bool Contains(Vector2 point, ref bool ambiguous)
            {
                var local = inverse.MultiplyPoint3x4(new Vector3(point.x, 0, point.y)) - center;
                float x = halfSize.x - Mathf.Abs(local.x), z = halfSize.z - Mathf.Abs(local.z);
                if (x < -tolerance.x || z < -tolerance.y) return false;
                if (x <= tolerance.x || z <= tolerance.y) { ambiguous = true; return false; }
                return true;
            }
        }

        readonly struct FootprintTriangle
        {
            readonly Vector2 a, b, c, minimum, maximum;
            readonly float abLength, bcLength, caLength;
            internal Vector2 Centroid => (a + b + c) / 3f;
            internal FootprintTriangle(Vector3 p, Vector3 q, Vector3 r)
            {
                a = new Vector2(p.x, p.z); b = new Vector2(q.x, q.z); c = new Vector2(r.x, r.z);
                minimum = Vector2.Min(a, Vector2.Min(b, c)); maximum = Vector2.Max(a, Vector2.Max(b, c));
                abLength = (b - a).magnitude; bcLength = (c - b).magnitude; caLength = (a - c).magnitude;
            }
            internal bool Contains(Vector2 point, ref bool ambiguous)
            {
                if (point.x < minimum.x - EdgeTolerance || point.y < minimum.y - EdgeTolerance ||
                    point.x > maximum.x + EdgeTolerance || point.y > maximum.y + EdgeTolerance) return false;
                // Upward triangles are clockwise in the x/z projection, so negate each edge cross product.
                double ab = -Cross(b - a, point - a) / abLength;
                double bc = -Cross(c - b, point - b) / bcLength;
                double ca = -Cross(a - c, point - c) / caLength;
                if (ab < -EdgeTolerance || bc < -EdgeTolerance || ca < -EdgeTolerance) return false;
                if (ab <= EdgeTolerance || bc <= EdgeTolerance || ca <= EdgeTolerance)
                { ambiguous = true; return false; }
                return true;
            }
            static double Cross(Vector2 p, Vector2 q) => (double)p.x * q.y - (double)p.y * q.x;
        }

        static float Top(MeshFilter filter)
            => filter.sharedMesh.vertices.Max(x => filter.transform.TransformPoint(x).y);
        static void Require(bool condition, string message, List<string> errors)
        { if (!condition) errors.Add(message); }
    }
}
#endif
