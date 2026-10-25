using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>Static leaf clusters share six compact meshes; trunks remain the collision geometry.</summary>
    internal static class FoliageArt
    {
        static Material leaves;
        static readonly Mesh[] Crowns = new Mesh[6];

        public static bool Crown(Transform parent, float height, int variant)
        {
            if (!EnsureLeaves()) return false;
            variant = Mathf.Abs(variant) % Crowns.Length;
            if (!Crowns[variant]) Crowns[variant] = BuildCrown(variant);
            var root = WorldArt.Group(parent, "Oak foliage crown", Vector3.up * height * .88f);
            float width = 5.8f + height * .12f;
            root.localScale = new Vector3(width, 3.5f + (height - 5.4f) * .2f, width);
            root.localRotation = Quaternion.Euler(0, variant * 61, 0);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = Crowns[variant];
            var renderer = root.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = leaves;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return true;
        }

        public static bool Shrub(Transform parent, Vector3 position, Vector3 size, int variant)
        {
            if (!EnsureLeaves()) return false;
            variant = Mathf.Abs(variant) % Crowns.Length;
            if (!Crowns[variant]) Crowns[variant] = BuildCrown(variant);
            var root = WorldArt.Group(parent, "Overgrown shrub foliage", position);
            root.localScale = size;
            root.localRotation = Quaternion.Euler(0, variant * 61, 0);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = Crowns[variant];
            var renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = leaves;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return true;
        }

        static bool EnsureLeaves()
        {
            if (leaves) return true;
            var texture = Resources.Load<Texture2D>("Textures/oak-foliage");
            if (!texture) return false;
            texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = 4;
            leaves = WorldArt.NewMaterial("Oak leaf clusters", new Color(.65f, .73f, .59f), 0, .10f);
            leaves.mainTexture = texture;
            leaves.SetFloat("_AlphaClip", 1); leaves.SetFloat("_Cutoff", .38f);
            leaves.SetFloat("_Cull", (float)CullMode.Off); leaves.SetFloat("_ZWrite", 1);
            leaves.SetOverrideTag("RenderType", "TransparentCutout");
            leaves.EnableKeyword("_ALPHATEST_ON"); leaves.renderQueue = (int)RenderQueue.AlphaTest;
            return true;
        }

        static Mesh BuildCrown(int variant)
        {
            var random = new System.Random(3901 + variant * 43);
            var vertices = new List<Vector3>(336); var normals = new List<Vector3>(336);
            var uv = new List<Vector2>(336); var triangles = new List<int>(504);
            for (int cluster = 0; cluster < 28; cluster++)
            {
                float y = -.32f + .64f * (cluster + .5f) / 28;
                float angle = cluster * 2.399963f + (float)random.NextDouble() * .5f;
                float radius = Mathf.Sqrt(1 - y * y / .16f) * (.23f + (float)random.NextDouble() * .12f);
                var center = new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
                float width = .24f + (float)random.NextDouble() * .13f;
                float tall = .29f + (float)random.NextDouble() * .14f;
                float yaw = (float)random.NextDouble() * 360;
                for (int card = 0; card < 3; card++)
                {
                    var rotation = Quaternion.Euler(card == 2 ? 66 : (float)random.NextDouble() * 24 - 12, yaw + card * 90, (float)random.NextDouble() * 30 - 15);
                    int start = vertices.Count;
                    vertices.Add(center + rotation * new Vector3(-width / 2, -tall / 2, 0));
                    vertices.Add(center + rotation * new Vector3(width / 2, -tall / 2, 0));
                    vertices.Add(center + rotation * new Vector3(width / 2, tall / 2, 0));
                    vertices.Add(center + rotation * new Vector3(-width / 2, tall / 2, 0));
                    for (int i = 0; i < 4; i++) normals.Add(rotation * Vector3.back);
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                }
            }
            var mesh = new Mesh { name = "Oak clustered crown " + variant };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
