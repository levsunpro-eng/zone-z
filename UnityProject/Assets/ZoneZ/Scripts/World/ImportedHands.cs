using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    /// <summary>CC0 para / MakeHuman anatomy, tailored glove/sleeve shell and release pose.</summary>
    internal static class ImportedHands
    {
        [Serializable] sealed class Model { public Part[] parts; }
        [Serializable] sealed class Part { public string name; public Vector3[] vertices,normals,release,releaseNormals; public int[] triangles; }
        static readonly Dictionary<string,Mesh[]> Meshes = new Dictionary<string,Mesh[]>();
        static Material glove, fabric, rubber;

        public static bool Attach(Transform parent, WeaponPresentation view, bool compact)
        {
            var profile=WeaponHandProfile.Create(parent,view,compact);
            var right=Load("RightGrip");
            var left=profile.Family==HandFamily.DualPistols?MirroredTrigger():Load(profile.FittedAk?"LeftGrip":"LeftGripCompact");
            if(right==null || left==null)return false;
            PrepareMaterials();
            view.HandProfile=profile;
            view.TriggerHand=WorldArt.Group(parent,"Anatomical trigger wrist",profile.TriggerPosition);
            view.TriggerHand.localRotation=profile.TriggerRotation;
            Build(view.TriggerHand,right,view,new Vector3(.30f,-.37f,-.34f),profile.FittedAk);
            view.SupportHand=WorldArt.Group(parent,"Anatomical support wrist",profile.SupportPosition);
            view.SupportHand.localRotation=profile.SupportRotation;
            Build(view.SupportHand,left,view,new Vector3(-.28f,-.35f,-.32f),profile.FittedAk);
            parent.gameObject.AddComponent<HandPoseDriver>().Initialize(view);
            return true;
        }

        static Mesh[] MirroredTrigger()
        {
            if(Meshes.TryGetValue("LeftTriggerGrip",out var cached) && cached[0])return cached;
            var original=Load("RightGrip");if(original==null)return null;
            var result=new Mesh[original.Length];
            for(int i=0;i<original.Length;i++)
            {
                var mesh=UnityEngine.Object.Instantiate(original[i]);mesh.name=original[i].name.Replace("RightGrip","LeftTriggerGrip");
                var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;
                for(int j=0;j<vertices.Length;j++){vertices[j].x=-vertices[j].x;normals[j].x=-normals[j].x;}
                for(int j=0;j<triangles.Length;j+=3){int swap=triangles[j];triangles[j]=triangles[j+2];triangles[j+2]=swap;}
                mesh.vertices=vertices;mesh.normals=normals;mesh.triangles=triangles;
                if(mesh.blendShapeCount>0)
                {
                    var delta=new Vector3[vertices.Length];var dn=new Vector3[vertices.Length];var dt=new Vector3[vertices.Length];
                    mesh.GetBlendShapeFrameVertices(0,0,delta,dn,dt);mesh.ClearBlendShapes();
                    for(int j=0;j<delta.Length;j++){delta[j].x=-delta[j].x;dn[j].x=-dn[j].x;dt[j].x=-dt[j].x;}
                    mesh.AddBlendShapeFrame("Release",100,delta,dn,dt);
                }
                mesh.RecalculateBounds();mesh.RecalculateTangents();result[i]=mesh;
            }
            Meshes["LeftTriggerGrip"]=result;return result;
        }

        static Mesh[] Load(string name)
        {
            if(Meshes.TryGetValue(name,out var cached) && cached.Length>0 && cached[0])return cached;
            var source=Resources.Load<TextAsset>("ImportedHands/"+name);if(!source)return null;
            var data=JsonUtility.FromJson<Model>(source.text);var meshes=new Mesh[data.parts.Length];
            for(int i=0;i<meshes.Length;i++) {
                var p=data.parts[i];var mesh=new Mesh{name=p.name+" CC0"};
                mesh.vertices=p.vertices;mesh.triangles=p.triangles;mesh.normals=p.normals;
                var uv=new Vector2[p.vertices.Length];
                for(int v=0;v<uv.Length;v++)uv[v]=new Vector2(p.vertices[v].x*15,p.vertices[v].y*15);
                mesh.uv=uv;mesh.RecalculateTangents();mesh.RecalculateBounds();
                if(p.release!=null && p.release.Length==p.vertices.Length)mesh.AddBlendShapeFrame("Release",100,p.release,p.releaseNormals,new Vector3[p.vertices.Length]);
                meshes[i]=mesh;
            }
            Meshes[name]=meshes;return meshes;
        }

        static void Build(Transform wrist,Mesh[] parts,WeaponPresentation owner,Vector3 sleeveEnd,bool preserveSleeve)
        {
            foreach(var source in parts) {
                var mesh=source;
                if(source.name.Contains("Sleeve") && !preserveSleeve) mesh=FitSleeve(wrist,source,owner,sleeveEnd);
                var node=WorldArt.Group(wrist,mesh.name);Renderer renderer;
                if(mesh.blendShapeCount>0) {
                    var skin=node.gameObject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;
                    skin.updateWhenOffscreen=true;renderer=skin;
                } else {
                    node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=node.gameObject.AddComponent<MeshRenderer>();
                }
                renderer.sharedMaterial=mesh.name.Contains("Sleeve")?fabric:mesh.name.Contains("cuff")?rubber:glove;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows=true;
            }
        }

        static Mesh FitSleeve(Transform wrist,Mesh source,WeaponPresentation owner,Vector3 endpoint)
        {
            var mesh=UnityEngine.Object.Instantiate(source);mesh.name=source.name+" fitted";mesh.ClearBlendShapes();
            var vertices=mesh.vertices;var originalEnd=Vector3.zero;int count=0;
            foreach(var v in vertices)if(v.y<-.24f){originalEnd+=v;count++;}
            originalEnd/=Mathf.Max(1,count);
            Vector3 target=wrist.InverseTransformPoint(owner.transform.TransformPoint(endpoint));
            Vector3 delta=target-originalEnd;
            for(int i=0;i<vertices.Length;i++)
            {
                float t=Mathf.Clamp01((-vertices[i].y-.025f)/.235f);
                vertices[i]+=delta*(t*t*(3-2*t));
            }
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            owner.OwnedResources.Add(mesh);return mesh;
        }

        static void PrepareMaterials()
        {
            if(glove)return;
            glove=WorldArt.NewMaterial("Tailored graphite gloves",new Color(.105f,.12f,.095f),.02f,.26f);
            fabric=WorldArt.NewMaterial("Woven olive sleeves",new Color(.23f,.27f,.18f),0,.12f);
            rubber=WorldArt.NewMaterial("Glove cuff rubber",new Color(.035f,.042f,.03f),0,.22f);
            OperatorMaterials.ApplySurface(glove,OperatorSurface.Rubber,1.6f);
            OperatorMaterials.ApplySurface(fabric,OperatorSurface.Fabric,1.2f);
            OperatorMaterials.ApplySurface(rubber,OperatorSurface.Rubber,1.0f);
        }
    }

    [DefaultExecutionOrder(20)]
    internal sealed class HandPoseDriver : MonoBehaviour
    {
        WeaponPresentation view;
        SkinnedMeshRenderer[] support,trigger;
        public void Initialize(WeaponPresentation presentation)
        {
            view=presentation;support=view.SupportHand.GetComponentsInChildren<SkinnedMeshRenderer>();trigger=view.TriggerHand.GetComponentsInChildren<SkinnedMeshRenderer>();
        }
        void LateUpdate()
        {
            if(!view)return;
            foreach(var s in support)if(s && s.sharedMesh.blendShapeCount>0)s.SetBlendShapeWeight(0,view.SupportReleaseAmount);
            foreach(var s in trigger)if(s && s.sharedMesh.blendShapeCount>0)s.SetBlendShapeWeight(0,view.Melee?0:3);
        }
    }
}
