using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>Opaque, bent meadow blades in small spatial batches; decoration never participates in collision.</summary>
    internal sealed class GrassArt : MonoBehaviour
    {
        const float CellSize = 12f;
        const float SampleSpacing = .84f;
        const float Clearance = .35f;
        const int MaxTuftsPerCell = 80;
        static Material[] materials;
        readonly List<Mesh> meshes = new List<Mesh>();

        // Call after architectural colliders have been created and Physics.SyncTransforms has run.
        public static void Build(Transform parent, Collider ground)
        {
            if (!parent || !ground) return;
            EnsureMaterials();
            var root = WorldArt.Group(parent, "Low meadow grass");
            var owner = root.gameObject.AddComponent<GrassArt>();
            var nearby = new Collider[32];
            int cell = 0;
            for (float z = -67f; z < 67f; z += CellSize)
                for (float x = -76f; x < 76f; x += CellSize)
                    owner.BuildCell(root, ground, nearby, x, z, cell++);
        }

        static void EnsureMaterials()
        {
            if (materials != null && materials[0]) return;
            materials = new[]
            {
                WorldArt.NewMaterial("Meadow grass green", new Color(.24f, .32f, .17f), 0, .05f),
                WorldArt.NewMaterial("Meadow grass light", new Color(.34f, .40f, .23f), 0, .05f),
                WorldArt.NewMaterial("Meadow grass dry tips", new Color(.44f, .40f, .27f), 0, .03f)
            };
            foreach (var material in materials)
            {
                // Actual tapered geometry needs neither alpha clipping nor a texture that aliases at a distance.
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.SetFloat("_ZWrite", 1);
                material.SetFloat("_SpecularHighlights", 0);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
        }

        void BuildCell(Transform root, Collider ground, Collider[] nearby, float x, float z, int cell)
        {
            var random = new System.Random(7919 + cell * 104729);
            float width = Mathf.Min(CellSize, 76f - x), depth = Mathf.Min(CellSize, 67f - z);
            int columns = Mathf.CeilToInt(width / SampleSpacing), rows = Mathf.CeilToInt(depth / SampleSpacing);
            var samples = new int[columns * rows];
            for (int i = 0; i < samples.Length; i++) samples[i] = i;
            // A capped cell samples its whole area rather than stopping along one visible straight edge.
            for (int i = samples.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int value = samples[i]; samples[i] = samples[other]; samples[other] = value;
            }

            var vertices = new List<Vector3>(MaxTuftsPerCell * 28);
            var triangles = new[] { new List<int>(2400), new List<int>(2400), new List<int>(600) };
            var origin = new Vector3(x + width * .5f, 0, z + depth * .5f);
            int tufts = 0;
            foreach (int sample in samples)
            {
                float px = x + (sample % columns + Range(random, .18f, .82f)) * width / columns;
                float pz = z + (sample / columns + Range(random, .18f, .82f)) * depth / rows;
                float meadow = Mathf.PerlinNoise(px * .087f + 39.7f, pz * .087f + 71.3f);
                if (random.NextDouble() > Mathf.Lerp(.38f, .90f, meadow)) continue;
                var samplePosition = root.TransformPoint(new Vector3(px, 0, pz));
                var rayStart = new Vector3(samplePosition.x, ground.bounds.max.y + 10f, samplePosition.z);
                if (!Physics.Raycast(rayStart, Vector3.down, out var hit, 11f, ~0, QueryTriggerInteraction.Ignore)
                    || hit.collider != ground || hit.normal.y < .95f || !Clear(hit.point, ground, nearby)) continue;

                // Roots extend a few millimetres into the lawn; there is no horizontal coplanar grass card.
                var center = root.InverseTransformPoint(hit.point) - origin - Vector3.up * .008f;
                int blades = random.Next(3, 5);
                float phase = Range(random, 0, Mathf.PI * 2);
                float height = Range(random, .19f, .33f);
                for (int blade = 0; blade < blades; blade++)
                {
                    float angle = phase + blade * 2.399963f + Range(random, -.3f, .3f);
                    var forward = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                    var side = new Vector3(forward.z, 0, -forward.x);
                    var bladeRoot = center + forward * Range(random, .015f, .08f);
                    int material = random.NextDouble() < .10 ? 2 : random.Next(2);
                    AddBlade(vertices, triangles[material], bladeRoot, forward, side,
                        Mathf.Clamp(height * Range(random, .80f, 1.18f), .15f, .38f),
                        Range(random, .038f, .069f), Range(random, .065f, .16f));
                }
                if (++tufts == MaxTuftsPerCell) break;
            }
            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = "Meadow grass cell " + cell, subMeshCount = materials.Length };
            mesh.SetVertices(vertices);
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh);
            var node = WorldArt.Group(root, mesh.name, origin);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Frustum culling uses each small mesh's bounds. No distance threshold or LOD causes popping.
        }

        static bool Clear(Vector3 point, Collider ground, Collider[] nearby)
        {
            int count = Physics.OverlapSphereNonAlloc(point + Vector3.up * .25f, .48f,
                nearby, ~0, QueryTriggerInteraction.Ignore);
            if (count == nearby.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = nearby[i];
                if (collider == ground) continue;
                var bounds = collider.bounds;
                // Ignore the lower foundation; keep blades away from roads, seats, walls and props.
                if (bounds.max.y <= point.y + .012f) continue;
                float dx = Mathf.Max(Mathf.Max(bounds.min.x - point.x, 0), point.x - bounds.max.x);
                float dz = Mathf.Max(Mathf.Max(bounds.min.z - point.z, 0), point.z - bounds.max.z);
                if (dx * dx + dz * dz < Clearance * Clearance) return false;
            }
            return true;
        }

        static void AddBlade(List<Vector3> vertices, List<int> triangles, Vector3 root,
            Vector3 forward, Vector3 side, float height, float width, float bend)
        {
            int start = vertices.Count;
            var middle = root + Vector3.up * (height * .57f) + forward * (bend * .28f);
            vertices.Add(root - side * (width * .5f));
            vertices.Add(root + forward * (width * .19f));
            vertices.Add(root + side * (width * .5f));
            vertices.Add(middle - side * (width * .30f));
            vertices.Add(middle + forward * (width * .24f));
            vertices.Add(middle + side * (width * .30f));
            vertices.Add(root + Vector3.up * height + forward * bend);
            // A folded cross-section and a bent, tapering tip give each blade a real three-dimensional shape.
            Triangle(triangles, start, 0, 3, 1); Triangle(triangles, start, 1, 3, 4);
            Triangle(triangles, start, 3, 6, 4); Triangle(triangles, start, 1, 4, 2);
            Triangle(triangles, start, 2, 4, 5); Triangle(triangles, start, 4, 6, 5);
        }

        static void Triangle(List<int> triangles, int start, int a, int b, int c)
        {
            triangles.Add(start + a); triangles.Add(start + b); triangles.Add(start + c);
        }

        static float Range(System.Random random, float min, float max)
            => min + (float)random.NextDouble() * (max - min);

        void OnDestroy()
        {
            foreach (var mesh in meshes)
                if (mesh)
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
        }
    }
}
