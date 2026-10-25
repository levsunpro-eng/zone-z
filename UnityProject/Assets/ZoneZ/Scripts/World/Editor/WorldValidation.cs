#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZoneZ.Editor
{
    public static class WorldValidation
    {
        [MenuItem("Zone Z/Validate world and weapon art")]
        public static void Run()
        {
            if(Application.isPlaying) throw new InvalidOperationException("Run world validation outside Play Mode.");
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var openScene=SceneManager.GetSceneAt(i);
                if(openScene.isLoaded && string.IsNullOrEmpty(openScene.path))
                    throw new InvalidOperationException("World validation was not started because an untitled scene is open. Unity cannot create the required additive Edit Mode scene in this state. Save or close that scene yourself, or run ZoneZ.Editor.BuildTasks.Validate in batch mode. Your open scenes have not been saved, closed or changed.");
            }
            var previous=SceneManager.GetActiveScene();
            float previousShadowDistance=QualitySettings.shadowDistance;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var errors=new List<string>();
            GameObject holder=null;
            UnityEngine.AI.NavMeshData temporaryNavMesh=null;
            try
            {
                holder=new GameObject("World validation fixture");
                var world=ZooWorld.Build(holder.transform);
                temporaryNavMesh=world.Navigation.navMeshData;
                Physics.SyncTransforms();
                Require(world.Navigation && world.Navigation.navMeshData,"World NavMesh baking returned no data.",errors);
                world.ValidateNavigation(errors);
                Require(world.GetComponentsInChildren<TextMesh>().All(x=>x.font && x.GetComponent<MeshRenderer>().sharedMaterial),"World signs have an unassigned font or text material.",errors);
                foreach(var label in world.GetComponentsInChildren<TextMesh>())
                {
                    var outward=Vector3.forward*Mathf.Sign(label.transform.localPosition.z);
                    Require(Vector3.Dot(-(label.transform.localRotation*Vector3.forward),outward)>.99f,"Sign is mirrored on the outside of its board: "+label.text,errors);
                    var material=label.GetComponent<MeshRenderer>().sharedMaterial;
                    Require(material && material.shader && material.shader.name=="Zone Z/World Sign","World signs must use their depth-tested shader.",errors);
                }
                ValidateGroundUv(world,errors);
                GroundValidation.Run(world,errors);
                if(Resources.Load<Texture2D>("Textures/oak-foliage"))
                    Require(world.GetComponentsInChildren<MeshRenderer>().Count(x=>x.name=="Oak foliage crown")==29,"Expected 29 textured foliage crowns.",errors);
                var surfaceAsset=Resources.Load<TextAsset>("Balance/surfaces");
                if(!surfaceAsset) throw new InvalidOperationException("Missing Balance/surfaces for world material validation.");
                var surfaces=JObject.Parse(surfaceAsset.text);
                var surfaceIds=new HashSet<string>(surfaces["surfaces"].Select(x=>(string)x["id"]));
                foreach(var collider in world.GetComponentsInChildren<Collider>())
                {
                    var tag=collider.GetComponent<SurfaceTag>();
                    Require(tag && surfaceIds.Contains(tag.MaterialId),"Unknown collision material on "+collider.name,errors);
                }
                Require(world.GetComponentsInChildren<CapsuleCollider>().Length==0,"Static cylindrical art must not retain oversized primitive capsule colliders.",errors);
                var fountainRing=world.GetComponentsInChildren<Collider>().First(x=>x.name=="Fountain ground ring");
                Require(fountainRing.bounds.size.y<.2f,"Flat fountain ring has an invisible vertical collider.",errors);
                int worldTriangles=TriangleCount(world.gameObject);
                int worldRenderers=world.GetComponentsInChildren<MeshRenderer>().Count(x=>x.enabled);
                int worldMaterials=world.GetComponentsInChildren<Renderer>().Select(x=>x.sharedMaterial).Where(x=>x).Distinct().Count();
                Require(worldMaterials<=48,"World exceeds the 48 shared material review threshold.",errors);
                var animals=world.GetComponentsInChildren<AmbientZoo>();
                Require(animals.Length==9,"Expected exactly 9 live ambient animals.",errors);
                Require(animals.Count(x=>x.Kind==AmbientZoo.Species.Squirrel)==2,"Expected 2 squirrels.",errors);
                Require(animals.Count(x=>x.Kind==AmbientZoo.Species.Rat)==2,"Expected 2 rats.",errors);
                Require(animals.Count(x=>x.Kind==AmbientZoo.Species.Turtle)==1,"Expected 1 turtle.",errors);
                Require(animals.Count(x=>x.Kind==AmbientZoo.Species.Pigeon)==4,"Expected 4 pigeons.",errors);
                Require(world.GetComponentsInChildren<Transform>().Count(x=>x.name=="Old non-graphic remains")==2,"Expected 2 remains props.",errors);
                foreach(var animal in animals)
                    Require(animal.GetComponentsInChildren<Collider>().Length==0 && !animal.GetComponent<Actor>(),"Ambient fauna must not collide or score as combatants.",errors);
                AmbientZooValidation.Run(world,errors);
                var look=world.GetComponentsInChildren<UnityEngine.Rendering.Volume>().FirstOrDefault();
                Require(look && look.isGlobal && look.sharedProfile,"World has no authored dusk color grading.",errors);
                Require(world.GetComponentsInChildren<Transform>().All(x=>x.name!="ЛЕСНЫЕ ЖИТЕЛИ" && x.name!="ЧЕРЕПАХИ" && x.name!="ТЕХНИЧЕСКАЯ ЗОНА"),"Former squirrel/rat/turtle cages still exist.",errors);
                foreach(string surface in new[]{"Textures/weathered-concrete","Textures/worn-asphalt","Textures/dry-park-ground","Textures/weathered-timber"})
                {
                    Require(Resources.Load<Texture2D>(surface+"-normal"),surface+" is missing relief normals.",errors);
                    Require(Resources.Load<Texture2D>(surface+"-ao"),surface+" is missing surface occlusion.",errors);
                }

                var catalog=GameCatalog.Load();
                Require(catalog.Weapons.Count==36,"Expected 34 firearms, knife and Zeus.",errors);
                var teamMode=catalog.Modes["modes"].First(x=>(string)x["id"]=="team_elimination");
                var dmMode=catalog.Modes["modes"].First(x=>(string)x["id"]=="deathmatch");
                Require(world.TeamASpawns.Length==(int)teamMode["playersPerTeam"] && world.TeamBSpawns.Length==(int)teamMode["playersPerTeam"],"Team spawn capacity differs from modes.json.",errors);
                Require(world.DeathmatchSpawns.Length==(int)dmMode["spawnPointCount"],"DM spawn count differs from modes.json.",errors);
                Require(world.LootPoints.Length==(int)catalog.Loot["spawnRules"]["fixedPointCount"],"Loot point count differs from loot.json.",errors);
                var signatures=new HashSet<string>();
                int firstPersonMax=0,thirdPersonMax=0;
                foreach(var spec in catalog.Weapons)
                {
                    var skin=new SkinPreset{weaponId=spec.Id,pattern=2,patternRotation=35,patternScale=1.4f,stickers=new[]{1,2,3,4}};
                    var model=WeaponModelFactory.Create(holder.transform,spec,skin,true);
                    Require(model.Muzzle!=null,spec.Id+" has no muzzle anchor.",errors);
                    Require(model.GetComponentsInChildren<Collider>(true).Length==0,spec.Id+" contains a view-model collider.",errors);
                    Require(model.TriggerHand && model.SupportHand,spec.Id+" has no separate hand rig.",errors);
                    Require(model.TriggerHand && model.SupportHand &&
                        model.TriggerHand.GetComponentsInChildren<SkinnedMeshRenderer>().Any(x=>x.sharedMesh && x.sharedMesh.GetBlendShapeIndex("Release")>=0) &&
                        model.SupportHand.GetComponentsInChildren<SkinnedMeshRenderer>().Any(x=>x.sharedMesh && x.sharedMesh.GetBlendShapeIndex("Release")>=0),
                        spec.Id+" did not load both anatomical hands with finger release poses.",errors);
                    if(spec.Id=="weapon_ak47") Require(model.GetComponentsInChildren<MeshFilter>().Any(x=>x.sharedMesh && x.sharedMesh.name.StartsWith("CC0 AK")),"The free AK asset was replaced by its primitive fallback.",errors);
                    Require(model.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Sticker slot "))==4,spec.Id+" has fewer than 4 sticker regions.",errors);
                    Require(model.GetComponentsInChildren<Renderer>().All(x=>x.sharedMaterial!=null),spec.Id+" has a missing material.",errors);
                    foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
                        if(filter.sharedMesh && filter.sharedMesh.name=="Chamfered weapon block")
                            Require((filter.sharedMesh.bounds.size-Vector3.one).sqrMagnitude<.000001f && filter.sharedMesh.GetIndexCount(0)==132,spec.Id+" chamfer changed the original socket bounds or geometry budget.",errors);
                    var names=model.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.name+":"+x.transform.localPosition.ToString("F3")+":"+x.transform.localScale.ToString("F3")).OrderBy(x=>x);
                    Require(signatures.Add(string.Join("|",names)),spec.Id+" duplicates another weapon's silhouette parts.",errors);
                    int triangles=TriangleCount(model.gameObject); firstPersonMax=Mathf.Max(firstPersonMax,triangles);
                    Require(triangles<=35000,spec.Id+" exceeds the 35k first-person triangle budget.",errors);
                    model.SetAlternate(true); model.SetReload(.45f,true); model.Fire(); model.Inspect();
                    UnityEngine.Object.DestroyImmediate(model.gameObject);
                    model=WeaponModelFactory.Create(holder.transform,spec,null,false);
                    triangles=TriangleCount(model.gameObject); thirdPersonMax=Mathf.Max(thirdPersonMax,triangles);
                    Require(triangles<=8000,spec.Id+" exceeds the 8k world weapon triangle budget.",errors);
                    UnityEngine.Object.DestroyImmediate(model.gameObject);
                }
                foreach(var id in new[]{"underbarrel","laser_burst","rocket_launcher"})
                {
                    var model=WeaponModelFactory.CreateAbility(holder.transform,id,true);
                    Require(model.Muzzle && model.SupportHand,id+" lacks muzzle or first-person support hand.",errors);
                    Require(model.GetComponentsInChildren<Collider>(true).Length==0,id+" contains an art collider.",errors);
                    UnityEngine.Object.DestroyImmediate(model.gameObject);
                }
                var actor=ActorVisual.Create(holder.transform,0);
                Require(actor.UsesImportedModel,"The imported CC0 operator was replaced by its primitive fallback.",errors);
                int actorTriangles=TriangleCount(actor.gameObject),actorRenderers=actor.GetComponentsInChildren<MeshRenderer>().Count(x=>x.enabled);
                Require(actorTriangles<=25000,"Actor exceeds the 25k triangle budget.",errors);
                Require(actorRenderers<=40,"Actor requires too many independent renderer submissions.",errors);
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
                OperatorValidation.Run();
                Debug.Log($"ZONEZ_ART_BUDGETS: world triangles={worldTriangles}, renderers before runtime batching={worldRenderers}, shared materials={worldMaterials}; actor triangles={actorTriangles}, renderers={actorRenderers}; maximum weapon triangles first-person={firstPersonMax}, world={thirdPersonMax}. These are geometry counts, not FPS measurements.");
            }
            finally
            {
                if(holder) UnityEngine.Object.DestroyImmediate(holder);
                if(temporaryNavMesh) UnityEngine.Object.DestroyImmediate(temporaryNavMesh);
                QualitySettings.shadowDistance=previousShadowDistance;
                if(previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
            if(errors.Count>0) throw new InvalidOperationException("World validation failed:\n"+string.Join("\n",errors));
            Debug.Log("ZONEZ_WORLD_VALIDATION_OK: 48 spawns, 12 loot points, 9 animals, 2 remains, 36 weapons and 3 ability weapons.");
        }

        static void Require(bool valid,string error,List<string> errors) { if(!valid) errors.Add(error); }
        static void ValidateGroundUv(ZooWorld world,List<string> errors)
        {
            var ground=world.GetComponentsInChildren<MeshFilter>().First(x=>x.name=="Whole playable ground");
            var mesh=ground.sharedMesh; var vertices=mesh.vertices; var uv=mesh.uv; var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
                for(int edge=0;edge<3;edge++)
                {
                    int a=triangles[i+edge],b=triangles[i+(edge+1)%3];
                    float meters=ground.transform.TransformVector(vertices[b]-vertices[a]).magnitude;
                    float repeats=(uv[b]-uv[a]).magnitude;
                    Require(repeats>0 && Mathf.Abs(meters/repeats-4)<.001f,"Ground texture must keep a four-meter UV tile on every face.",errors);
                }
        }
        static int TriangleCount(GameObject root)
        {
            int result=0;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!renderer || !renderer.enabled || !filter.sharedMesh) continue;
                for(int i=0;i<filter.sharedMesh.subMeshCount;i++) result+=(int)(filter.sharedMesh.GetIndexCount(i)/3);
            }
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!renderer.enabled || !renderer.sharedMesh)continue;
                for(int i=0;i<renderer.sharedMesh.subMeshCount;i++)result+=(int)(renderer.sharedMesh.GetIndexCount(i)/3);
            }
            return result;
        }
    }
}
#endif
