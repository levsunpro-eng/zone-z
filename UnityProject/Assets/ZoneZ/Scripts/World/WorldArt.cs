using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>Shared, original runtime geometry and materials. World collision is deliberately separate from foliage and effects.</summary>
    internal static class WorldArt
    {
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly Dictionary<Material, float> SurfaceTileMeters = new Dictionary<Material, float>();
        static readonly Dictionary<(PrimitiveType, Vector3), Mesh> SurfaceMeshes = new Dictionary<(PrimitiveType, Vector3), Mesh>();
        static Texture2D grain;
        static Font signFont;
        static Material signMaterial;
        static bool subscribedToFontRebuild;
        static readonly Texture2D[] Patterns = new Texture2D[4];

        public static Material Mat(string name, Color color, float metallic = 0, float smoothness = .25f)
        {
            if (Materials.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = NewMaterial(name, color, metallic, smoothness);
            if (grain == null) grain = Noise();
            material.mainTexture = grain;
            Materials[name] = material;
            return material;
        }

        public static Material NewMaterial(string name, Color color, float metallic = 0, float smoothness = .25f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color, enableInstancing = true };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        // Architectural textures use physical UV density. Weapon skins keep their authored UVs.
        public static Material SurfaceMat(string name, Color color, float metallic = 0, float smoothness = .25f, float tileMeters = 2, string albedoResource = null)
        {
            var material = Mat(name, color, metallic, smoothness);
            SurfaceTileMeters[material] = Mathf.Max(.1f, tileMeters);
            if (!string.IsNullOrEmpty(albedoResource))
            {
                var albedo = Resources.Load<Texture2D>(albedoResource);
                if (albedo)
                {
                    albedo.wrapMode = TextureWrapMode.Repeat;
                    albedo.filterMode = FilterMode.Trilinear;
                    albedo.anisoLevel = 8;
                    material.mainTexture = albedo;
                    var normal = Resources.Load<Texture2D>(albedoResource + "-normal");
                    if (normal)
                    {
                        material.SetTexture("_BumpMap", normal);
                        material.SetFloat("_BumpScale", .65f);
                        material.EnableKeyword("_NORMALMAP");
                    }
                    var occlusion = Resources.Load<Texture2D>(albedoResource + "-ao");
                    if (occlusion)
                    {
                        material.SetTexture("_OcclusionMap", occlusion);
                        material.SetFloat("_OcclusionStrength", .45f);
                        material.EnableKeyword("_OCCLUSIONMAP");
                    }
                }
            }
            return material;
        }

        static Mesh MeterMappedShape(Mesh source, PrimitiveType type, Vector3 size, float tileMeters)
        {
            var extent = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) / tileMeters;
            var key = (type, extent);
            if (SurfaceMeshes.TryGetValue(key, out var cached) && cached) return cached;
            var mesh = Object.Instantiate(source);
            mesh.name = "Meter mapped architecture " + type;
            var positions = mesh.vertices; var normals = mesh.normals; var uvs = new Vector2[positions.Length];
            var originalUv = mesh.uv;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = Vector3.Scale(positions[i] + Vector3.one * .5f, extent);
                Vector3 n = normals[i];
                if (type == PrimitiveType.Sphere)
                    uvs[i] = Vector2.Scale(originalUv[i], new Vector2(Mathf.PI * Mathf.Max(extent.x, extent.z), Mathf.PI * extent.y * .5f));
                else if (type == PrimitiveType.Cylinder && Mathf.Abs(n.y) < .5f)
                    uvs[i] = new Vector2(originalUv[i].x * Mathf.PI * Mathf.Max(extent.x, extent.z), (positions[i].y + 1) * extent.y);
                else if (Mathf.Abs(n.y) > .5f) uvs[i] = new Vector2(p.x, p.z);
                else if (Mathf.Abs(n.x) > .5f) uvs[i] = new Vector2(p.z, p.y);
                else uvs[i] = new Vector2(p.x, p.y);
            }
            mesh.uv = uvs;
            SurfaceMeshes[key] = mesh;
            return mesh;
        }

        public static Texture2D Pattern(int index)
        {
            index = Mathf.Clamp(index, 0, 3);
            if (Patterns[index] != null) return Patterns[index];
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true) { name = "Original skin pattern " + index, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float v = .91f;
                if (index == 1) v = ((x + y / 2) % 40 < 12) ? .38f : .92f;
                if (index == 2) v = Mathf.PerlinNoise(x * .055f, y * .06f) > .55f ? .35f : Mathf.PerlinNoise(x * .11f + 5, y * .11f) > .5f ? .66f : .98f;
                if (index == 3) v = ((x / 14 + y / 14) % 3 == 0) ? .4f : ((x + y) % 28 < 3 ? .65f : .98f);
                pixels[y * size + x] = new Color(v, v, v);
            }
            texture.SetPixels(pixels); texture.Apply(true, true); Patterns[index] = texture;
            return texture;
        }

        static Texture2D Noise()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true) { name = "ZoneZ surface grain", wrapMode = TextureWrapMode.Repeat };
            var colors = new Color[size * size];
            var rng = new System.Random(239);
            for (int i = 0; i < colors.Length; i++) { float v = .84f + (float)rng.NextDouble() * .16f; colors[i] = new Color(v, v, v); }
            texture.SetPixels(colors); texture.Apply(true, true); return texture;
        }

        public static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool solid = false, string surface = "concrete", Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if ((type == PrimitiveType.Cube || type == PrimitiveType.Cylinder || type == PrimitiveType.Sphere) && material && SurfaceTileMeters.TryGetValue(material, out var tileMeters))
            {
                var filter = go.GetComponent<MeshFilter>();
                filter.sharedMesh = MeterMappedShape(filter.sharedMesh, type, go.transform.lossyScale, tileMeters);
            }
            renderer.shadowCastingMode = scale.sqrMagnitude < .012f ? ShadowCastingMode.Off : ShadowCastingMode.On;
            var collider = go.GetComponent<Collider>();
            if (!solid) Object.DestroyImmediate(collider);
            else
            {
                // Unity's Cylinder primitive uses a CapsuleCollider. Nonuniform scaling turns a flat
                // fountain/tabletop into a large invisible rounded obstacle; use its actual static mesh.
                // A SphereCollider likewise cannot follow an ellipsoid's nonuniform scale.
                if (type == PrimitiveType.Cylinder || type == PrimitiveType.Sphere)
                {
                    Object.DestroyImmediate(collider);
                    go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
                var tag = go.AddComponent<SurfaceTag>(); tag.MaterialId = surface;
            }
            return go;
        }

        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool solid = false, string surface = "concrete", Vector3 euler = default)
            => Shape(parent, name, PrimitiveType.Cube, position, scale, material, solid, surface, euler);

        public static GameObject Cylinder(Transform parent, string name, Vector3 position, float radius, float height, Material material, bool solid = false, string surface = "metal", Vector3 euler = default)
            => Shape(parent, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, height * .5f, radius * 2), material, solid, surface, euler);

        public static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool solid = false, string surface = "stone")
            => Shape(parent, name, PrimitiveType.Sphere, position, scale, material, solid, surface);

        public static Transform Group(Transform parent, string name, Vector3 position = default, Vector3 euler = default)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localEulerAngles = euler; return go.transform;
        }

        public static GameObject Beam(Transform parent, string name, Vector3 start, Vector3 end, float width, Material material, bool solid = false, string surface = "metal")
        {
            var go = Box(parent, name, (start + end) * .5f, new Vector3(width, (end - start).magnitude, width), material, solid, surface);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start); return go;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 position, float scale, Color color, Vector3 euler = default)
        {
            var go = new GameObject("Sign " + text); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            // Callers specify the board's outward +Z normal. TextMesh's readable face is -Z.
            go.transform.localRotation = Quaternion.Euler(euler) * Quaternion.Euler(0, 180, 0);
            var mesh = go.AddComponent<TextMesh>(); mesh.text = text; mesh.fontSize = 64; mesh.characterSize = scale; mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = color;
            if (!signFont) signFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = signFont;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (!signMaterial)
            {
                var template = Resources.Load<Material>("WorldSignMaterial");
                if (template) signMaterial = new Material(template) { name = "Depth tested world signs" };
                else
                {
                    var shader = Resources.Load<Shader>("ZoneZWorldSign");
                    if (shader) signMaterial = new Material(shader) { name = "Depth tested world signs" };
                }
            }
            if (!subscribedToFontRebuild) { Font.textureRebuilt += RefreshFontAtlas; subscribedToFontRebuild = true; }
            RefreshFontAtlas(signFont);
            renderer.sharedMaterial = signMaterial;
            return mesh;
        }

        static void RefreshFontAtlas(Font rebuilt)
        {
            if (rebuilt == signFont && signFont && signMaterial)
                signMaterial.mainTexture = signFont.material.mainTexture;
        }

        // A tapered rectangular prism, authored with real faces rather than scaling one generic gun cube.
        public static GameObject Taper(Transform parent, string name, Vector3 center, Vector3 nearSize, Vector3 farSize, float length, Material material, Vector3 euler = default)
        {
            var vertices = new Vector3[8];
            for (int side = 0; side < 2; side++) for (int i = 0; i < 4; i++)
            {
                var size = side == 0 ? nearSize : farSize;
                vertices[side * 4 + i] = new Vector3((i == 0 || i == 3 ? -1 : 1) * size.x * .5f, (i < 2 ? -1 : 1) * size.y * .5f, (side == 0 ? -.5f : .5f) * length);
            }
            int[] triangles = { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5 };
            // Face splits preserve the manufactured edges and provide UVs for skin patterns.
            var positions = new Vector3[triangles.Length]; var uvs = new Vector2[triangles.Length]; var indices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                positions[i] = vertices[triangles[i]]; indices[i] = i;
                // Each face receives two non-degenerate UV axes, including the top and end caps.
                int face = i / 6;
                uvs[i] = face < 2 ? new Vector2(positions[i].x * 5, positions[i].y * 5)
                    : face < 4 ? new Vector2(positions[i].x * 5, positions[i].z * 4)
                    : new Vector2(positions[i].z * 4, positions[i].y * 5);
            }
            var mesh = new Mesh { name = name + " mesh" }; mesh.vertices = positions; mesh.uv = uvs; mesh.triangles = indices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = center; go.transform.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static void CombineRigidParts(Transform root, List<Object> owned)
        {
            var groups = new Dictionary<Material,List<MeshFilter>>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !renderer.sharedMaterial || !filter.sharedMesh) continue;
                if (!groups.TryGetValue(renderer.sharedMaterial, out var list)) { list = new List<MeshFilter>(); groups.Add(renderer.sharedMaterial,list); }
                list.Add(filter);
            }
            foreach (var group in groups)
            {
                if (group.Value.Count < 2) continue;
                var parts = new CombineInstance[group.Value.Count];
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i] = new CombineInstance { mesh = group.Value[i].sharedMesh, transform = root.worldToLocalMatrix * group.Value[i].transform.localToWorldMatrix };
                    group.Value[i].GetComponent<MeshRenderer>().enabled = false;
                }
                var mesh = new Mesh { name = "Rigid rig batch", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts, true, true); mesh.RecalculateBounds(); owned.Add(mesh);
                var node = Group(root,"Batched rig " + group.Key.name);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
            }
        }
    }
}
