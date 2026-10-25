using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZoneZ
{
    internal sealed class ImportedArsenalTag : MonoBehaviour
    {
        public float BodyWidth;
    }

    internal static class ImportedArsenalModels
    {
        const string Path = "Models/FreeArsenal/";
        [Serializable] internal sealed class Spec
        {
            public string id, source;
            public int triangles;
            public float muzzleZ, muzzleY, minimumZ, bodyWidth;
            public float[] triggerGrip, supportGrip;
        }
        [Serializable] sealed class Manifest { public Spec[] models; }
        sealed class Part { public string Role, Surface; public Mesh Mesh; public Vector3 Pivot; }
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>();
        static readonly Dictionary<string, Part[]> Meshes = new Dictionary<string, Part[]>();
        static bool initialized;

        internal static Spec GetSpec(string id)
        {
            if (!initialized)
            {
                initialized = true;
                var file = Resources.Load<TextAsset>(Path + "manifest");
                if (file)
                {
                    var manifest = JsonUtility.FromJson<Manifest>(file.text);
                    if (manifest?.models != null) foreach (var item in manifest.models) Specs[item.id] = item;
                }
            }
            return Specs.TryGetValue(id, out var spec) ? spec : null;
        }

        internal static bool Build(Transform root, WeaponPresentation view, string id, Material paint, Material polymer, Material accent, Material steel)
        {
            var spec = GetSpec(id);
            if (spec == null) return false;
            if (!Meshes.TryGetValue(id, out var parts))
            {
                parts = Load(id.Substring("weapon_".Length));
                if (parts == null) return false;
                Meshes[id] = parts;
            }
            var tag = root.gameObject.AddComponent<ImportedArsenalTag>(); tag.BodyWidth = spec.bodyWidth;
            var glass = WorldArt.Mat("Arsenal coated optic glass", new Color(.028f,.11f,.13f), .5f, .92f);
            var groups = new Dictionary<string, Transform>();
            foreach (var part in parts)
            {
                if (!groups.TryGetValue(part.Role, out var group))
                {
                    group = WorldArt.Group(root, "Adapted " + spec.source + " " + part.Role, part.Pivot);
                    groups[part.Role] = group;
                    if (part.Role == "Magazine") view.Magazine = group;
                    if (part.Role == "Slide") view.Slide = group;
                    if (part.Role == "Pump") view.Pump = group;
                }
                var node = WorldArt.Group(group, "Quaternius " + part.Surface);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = part.Surface == "Paint" ? paint : part.Surface == "Polymer" ? polymer
                    : part.Surface == "Accent" ? accent : part.Surface == "Glass" ? glass : steel;
                renderer.shadowCastingMode = view.FirstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = !view.FirstPerson;
            }
            view.Muzzle = WorldArt.Group(root, "Muzzle", new Vector3(0,spec.muzzleY,spec.muzzleZ+.002f));
            view.Length = spec.muzzleZ-spec.minimumZ;
            return true;
        }

        static Part[] Load(string id)
        {
            var asset = Resources.Load<TextAsset>(Path+id);
            if (!asset) return null;
            var parts = new List<Part>();
            using (var reader = new BinaryReader(new MemoryStream(asset.bytes), Encoding.UTF8))
            {
                if (reader.ReadInt32()!=0x5A415231 || reader.ReadInt32()!=1) return null;
                int count=reader.ReadInt32();
                if (count<1 || count>32) return null;
                for (int j=0;j<count;j++)
                {
                    int nameSize=reader.ReadInt32();
                    if (nameSize<1 || nameSize>80) return null;
                    var name=Encoding.UTF8.GetString(reader.ReadBytes(nameSize)).Split('|');
                    int vc=reader.ReadInt32(),ic=reader.ReadInt32();
                    if (vc<3 || vc>60000 || ic<3 || ic>120000 || ic%3!=0) return null;
                    var vertices=new Vector3[vc];var normals=new Vector3[vc];var uv=new Vector2[vc];var indices=new int[ic];
                    for (int v=0;v<vc;v++)
                    {
                        vertices[v]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                        normals[v]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                        uv[v]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
                    }
                    for(int i=0;i<ic;i++) { indices[i]=reader.ReadInt32(); if(indices[i]<0||indices[i]>=vc) return null; }
                    var mesh=new Mesh{name="CC0 adapted "+id+" "+name[0]+" "+name[1],vertices=vertices,normals=normals,uv=uv,triangles=indices};
                    mesh.RecalculateBounds();mesh.RecalculateTangents();
                    parts.Add(new Part{Role=name[0],Surface=name[1],Mesh=mesh});
                }
            }
            foreach (var role in new[]{"Magazine","Slide","Pump"})
            {
                Bounds bounds=default;bool found=false;
                foreach(var part in parts) if(part.Role==role) { if(found) bounds.Encapsulate(part.Mesh.bounds); else { bounds=part.Mesh.bounds;found=true; } }
                if(!found) continue;
                foreach(var part in parts) if(part.Role==role)
                {
                    part.Pivot=bounds.center;var vertices=part.Mesh.vertices;
                    for(int v=0;v<vertices.Length;v++)vertices[v]-=part.Pivot;
                    part.Mesh.vertices=vertices;part.Mesh.RecalculateBounds();
                }
            }
            return parts.ToArray();
        }

        internal static void Surface(Material material,string type)
        {
            foreach(var item in new[]{("_BumpMap","Normal","_NORMALMAP"),("_MetallicGlossMap","MetallicSmoothness","_METALLICSPECGLOSSMAP"),("_OcclusionMap","AO","_OCCLUSIONMAP")})
            {
                var texture=Resources.Load<Texture2D>(Path+type+"_"+item.Item2);
                if(texture && material.HasProperty(item.Item1)) { material.SetTexture(item.Item1,texture);material.EnableKeyword(item.Item3); }
            }
            material.SetFloat("_BumpScale",type=="Polymer"?.45f:.25f);
            material.SetFloat("_Smoothness",1);
            material.SetFloat("_GlossMapScale",1);
            material.SetFloat("_SmoothnessTextureChannel",0);
            material.SetFloat("_OcclusionStrength",.3f);
            if(type!="Steel")
            {
                var grain=Resources.Load<Texture2D>(Path+type+"_Albedo");
                if(grain) material.mainTexture=grain;
            }
        }
    }
}
