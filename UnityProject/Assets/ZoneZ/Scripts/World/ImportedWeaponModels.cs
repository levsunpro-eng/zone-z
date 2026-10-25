using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZoneZ
{
    /// <summary>Adapted CC0 source art. See docs/WEAPON_ASSET_LICENSE.md for source and modifications.</summary>
    internal static class ImportedWeaponModels
    {
        const string Resource = "Models/FreeAK/";
        sealed class Part { public string Name; public Mesh Mesh; public Vector3 Pivot; }
        static Part[] ak;
        static readonly Dictionary<string, Color32[]> SourcePixels = new Dictionary<string, Color32[]>();

        internal static bool TryBuild(Transform root, WeaponPresentation view, string id, SkinPreset skin,
            Material paint, Material grip, Material accent)
        {
            if (id != "weapon_ak47") return false;
            if (ak == null) ak = LoadAK();
            if (ak == null) return false;
            ApplyPbr(view, paint, "Body", skin, false);
            ApplyPbr(view, grip, "Magazine", skin, true);
            foreach (var part in ak)
            {
                var node = WorldArt.Group(root, "Imported AK " + part.Name, part.Pivot);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = part.Name == "Magazine" ? grip : paint;
                renderer.shadowCastingMode = view.FirstPerson
                    ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = !view.FirstPerson;
                if (part.Name == "Magazine") view.Magazine = node;
                if (part.Name == "Bolt") view.Slide = node;
            }
            view.MagazineRemoval = new Vector3(0, -.20f, .035f);
            view.Muzzle = WorldArt.Group(root, "Muzzle", new Vector3(0, .091f, .560f));
            view.Length = .90f;
            return true;
        }

        static Part[] LoadAK()
        {
            var resource = Resources.Load<TextAsset>(Resource + "AK");
            if (!resource) return null; // Existing authored model remains a safe asset-import fallback.
            var loaded = new List<Part>();
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(resource.bytes), Encoding.UTF8))
                {
                    if (reader.ReadInt32() != 0x5A414B31 || reader.ReadInt32() != 1)
                        throw new InvalidDataException("Unknown AK mesh resource version.");
                    int count = reader.ReadInt32();
                    if (count != 5) throw new InvalidDataException("AK resource must contain five mechanical parts.");
                    for (int p = 0; p < count; p++)
                    {
                        int nameLength = reader.ReadInt32();
                        if (nameLength < 1 || nameLength > 32) throw new InvalidDataException("Invalid AK part name.");
                        string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                        int vertexCount = reader.ReadInt32(), indexCount = reader.ReadInt32();
                        if (vertexCount < 3 || vertexCount > 20000 || indexCount < 3 || indexCount > 60000 || indexCount % 3 != 0)
                            throw new InvalidDataException("Invalid AK part geometry size.");
                        var vertices = new Vector3[vertexCount];
                        var normals = new Vector3[vertexCount];
                        var uv = new Vector2[vertexCount];
                        var indices = new int[indexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        }
                        for (int i = 0; i < indexCount; i++)
                        {
                            indices[i] = reader.ReadInt32();
                            if (indices[i] < 0 || indices[i] >= vertexCount) throw new InvalidDataException("AK index outside vertex buffer.");
                        }
                        var pivot = name == "Magazine" ? new Vector3(0, .052f, .089f)
                            : name == "Bolt" ? new Vector3(.009f, .091f, .082f) : Vector3.zero;
                        for (int i = 0; i < vertices.Length; i++) vertices[i] -= pivot;
                        // Shared meshes deliberately do not use the per-instance " mesh" ownership suffix.
                        var mesh = new Mesh { name = "CC0 AK " + name, vertices = vertices, normals = normals, uv = uv, triangles = indices };
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        loaded.Add(new Part { Name = name, Mesh = mesh, Pivot = pivot });
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected data after AK geometry.");
                }
                return loaded.ToArray();
            }
            catch (Exception ex)
            {
                foreach (var part in loaded)
                    if (Application.isPlaying) UnityEngine.Object.Destroy(part.Mesh); else UnityEngine.Object.DestroyImmediate(part.Mesh);
                Debug.LogWarning("Free AK model unavailable; using authored fallback: " + ex.Message);
                return null;
            }
        }

        static void ApplyPbr(WeaponPresentation owner, Material material, string atlas, SkinPreset skin, bool magazine)
        {
            var albedo = Resources.Load<Texture2D>(Resource + atlas + "_Albedo");
            material.color = Color.white;
            material.mainTexture = albedo;
            material.mainTextureScale = Vector2.one;
            SetMap(material, "_BumpMap", atlas + "_Normal", "_NORMALMAP");
            SetMap(material, "_OcclusionMap", atlas + "_AO", "_OCCLUSIONMAP");
            SetMap(material, "_MetallicGlossMap", atlas + "_MetallicSmoothness", "_METALLICSPECGLOSSMAP");
            material.SetFloat("_BumpScale", .75f);
            material.SetFloat("_OcclusionStrength", .8f);
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_GlossMapScale", 1);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            bool customized = skin.pattern != 0 || skin.wear > 0 ||
                !SameColor(skin.colorA, "4B5446") || !SameColor(skin.colorB, "20272C") || !SameColor(skin.colorC, "BFA470");
            if (albedo && customized)
            {
                var texture = CustomizedAtlas(albedo, atlas, skin, magazine);
                owner.OwnedResources.Add(texture);
                material.mainTexture = texture;
            }
        }

        static void SetMap(Material material, string property, string name, string keyword)
        {
            var texture = Resources.Load<Texture2D>(Resource + name);
            if (!texture || !material.HasProperty(property)) return;
            material.SetTexture(property, texture); material.EnableKeyword(keyword);
        }

        static bool SameColor(string value, string reference) => string.IsNullOrEmpty(value) ||
            string.Equals(value.TrimStart('#'), reference, StringComparison.OrdinalIgnoreCase);

        static Color Parse(string hex, string fallback)
        {
            ColorUtility.TryParseHtmlString("#" + fallback, out var color);
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var parsed)) color = parsed;
            return color;
        }

        static Texture2D CustomizedAtlas(Texture2D source, string atlas, SkinPreset skin, bool magazine)
        {
            if (!SourcePixels.TryGetValue(atlas, out var original))
            { original = source.GetPixels32(); SourcePixels[atlas] = original; }
            const int size = 512;
            var output = new Color32[size * size];
            Color a = Parse(skin.colorA, "4B5446"), b = Parse(skin.colorB, "20272C"), c = Parse(skin.colorC, "BFA470");
            bool customA = !SameColor(skin.colorA, "4B5446"), customB = !SameColor(skin.colorB, "20272C"), customC = !SameColor(skin.colorC, "BFA470");
            float angle = skin.patternRotation * Mathf.Deg2Rad, cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            float scale = Mathf.Clamp(skin.patternScale, .5f, 2);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Color src = original[(y * source.height / size) * source.width + x * source.width / size];
                bool wood = !magazine && src.r > src.g * 1.28f && src.r > src.b * 1.6f;
                bool custom = magazine ? customB : wood ? customC : customA;
                Color tint = magazine ? b : wood ? c : a;
                Color final = custom ? tint * Mathf.Clamp(.65f + src.grayscale * 1.25f, .55f, 1.25f) : src;
                float px = ((x-size*.5f)*cs-(y-size*.5f)*sn)*scale/4;
                float py = ((x-size*.5f)*sn+(y-size*.5f)*cs)*scale/4;
                float pattern = 1;
                if (skin.pattern == 1) pattern = Mathf.Repeat(px+py*.5f, 40) < 12 ? .42f : 1;
                else if (skin.pattern == 2) pattern = Mathf.PerlinNoise(px*.055f+10, py*.06f+10) > .55f ? .42f : Mathf.PerlinNoise(px*.11f+15,py*.11f+10) > .5f ? .72f : 1;
                else if (skin.pattern == 3) pattern = (Mathf.FloorToInt(px/14)+Mathf.FloorToInt(py/14))%3 == 0 ? .42f : Mathf.Repeat(px+py,28) < 3 ? .7f : 1;
                // Retain the original edge highlights and bake grain when adding the player's coating.
                final *= Mathf.Lerp(pattern, 1, Mathf.Clamp01(src.grayscale-.3f));
                float wear = Mathf.Clamp01(skin.wear);
                if (wear > 0 && Mathf.PerlinNoise(x*.35f+33,y*.4f+11) > 1-wear*.42f)
                    final = Color.Lerp(final, wood ? new Color(.19f,.115f,.06f) : new Color(.25f,.265f,.28f), .7f);
                final.a = 1; output[y*size+x] = final;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            { name = "AK personalized " + atlas, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            texture.SetPixels32(output); texture.Apply(true, true);
            return texture;
        }
    }
}
