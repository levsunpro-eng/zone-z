using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    // Opt-in integration runner: launched with its own profile, never the player's save.
    public sealed class RuntimeSmoke : MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        string directory;
        GameRoot root;
        IEnumerator Start()
        {
            root=GameRoot.Instance;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-zonez-report");
            directory=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../Diagnostics"));
            Directory.CreateDirectory(directory);Application.logMessageReceived+=OnLog;
            var pending=new Stack<IEnumerator>();pending.Push(Exercise());string failure=null;
            while(pending.Count>0 && failure==null) {
                bool more=false;object next=null;
                try {more=pending.Peek().MoveNext();if(more)next=pending.Peek().Current;}catch(Exception e){failure=e.ToString();}
                if(failure!=null)break;
                if(!more){pending.Pop();continue;}
                if(next is IEnumerator nested)pending.Push(nested);else yield return next;
            }
            Application.logMessageReceived-=OnLog;
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(directory,"runtime-smoke.json"),JsonConvert.SerializeObject(new {passed,checks,errors,failure,captureMode=OffscreenCapture?"Explicit URP scene frames (IMGUI menus/HUD excluded)":"Screen with menus/HUD",unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,utc=DateTime.UtcNow},Formatting.Indented));
            Debug.Log(passed?"ZONEZ_RUNTIME_SMOKE_OK":"ZONEZ_RUNTIME_SMOKE_FAILED "+failure);
            Application.Quit(passed?0:2);
        }
        void OnLog(string condition,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(condition+"\n"+stack);}
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);Debug.Log("SMOKE PASS "+label);}
        static bool OffscreenCapture => Array.IndexOf(Environment.GetCommandLineArgs(),"-zonez-offscreen-capture")>=0;
        Texture2D CaptureScene()
        {
            var camera=root.Match && root.Match.Local && root.Match.Local.ViewCamera?root.Match.Local.ViewCamera:root.MenuCamera;
            // Results intentionally disable the scene cameras; explicit requests can still render them.
            if(!camera)throw new InvalidOperationException("No scene camera for offscreen smoke capture.");
            var target=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);
            target.Create();var previous=RenderTexture.active;
            try {
                var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("Scene capture render request unsupported.");
                RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;
                var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();return pixels;
            } finally {RenderTexture.active=previous;target.Release();Destroy(target);}
        }
        IEnumerator Capture(string name)
        {
            // Allow camera changes, weapon draw and preview lighting to settle.
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();
            var capture=OffscreenCapture?CaptureScene():ScreenCapture.CaptureScreenshotAsTexture();
            var pixels=capture.GetPixels32();int darkest=765,brightest=0;
            for(int i=0;i<pixels.Length;i+=97) {
                int brightness=pixels[i].r+pixels[i].g+pixels[i].b;
                darkest=Math.Min(darkest,brightness);brightest=Math.Max(brightest,brightness);
            }
            if(brightest-darkest<24)errors.Add("Blank or uniform screenshot: "+name);
            File.WriteAllBytes(Path.Combine(directory,name+".png"),capture.EncodeToPNG());Destroy(capture);
            yield return null;
        }
        IEnumerator Exercise()
        {
            while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
            yield return null;yield return null;
            Check(root.World&&root.Catalog.Weapons.Count==36,"Boot loads world and full arsenal");
            yield return Capture("01-menu");
            var menuPage=typeof(GameUI).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic);
            var navigate=typeof(GameUI).GetMethod("ChangePage",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int i=1;i<6;i++) {
                navigate.Invoke(root.UI,new[]{Enum.ToObject(menuPage.FieldType,i)});
                yield return null;yield return null;yield return Capture("01-menu-"+i);
                if(i==5) {
                    var tab=typeof(GameUI).GetField("settingsTab",BindingFlags.Instance|BindingFlags.NonPublic);
                    for(int sub=1;sub<5;sub++){tab.SetValue(root.UI,sub);yield return null;yield return Capture("01-settings-"+sub);}
                }
            }
            root.Profile.uiScale=1.5f;navigate.Invoke(root.UI,new[]{Enum.ToObject(menuPage.FieldType,0)});
            yield return null;yield return Capture("01-menu-large-text");root.Profile.uiScale=1;
            root.Profile.healthLevel=root.Profile.speedLevel=root.Profile.strengthLevel=5;
            root.Match.StartMatch("deathmatch");StopControllers();
            Check(root.Match.Actors.Count==20,"Deathmatch has 20 participants");
            Check(root.Match.Actors.All(a=>root.Match.Actors.All(b=>a==b||Vector3.Distance(a.transform.position,b.transform.position)>=2)),"Deathmatch initial spawns cannot overlap other actors");
            Check(root.Match.Actors.All(a=>a.MaxHP==100&&a.SpeedMultiplier==1&&a.DamageMultiplier==1),"All permanent progression disabled in Deathmatch");
            root.Match.PreparationRemaining=.001f;root.Match.SendMessage("FixedUpdate");
            Check(root.Match.Phase==MatchPhase.Playing,"Deathmatch preparation enters play");
            Check(FindObjectsByType<LootPickup>(FindObjectsSortMode.None).Length==6,"Initial loot wave has six pickups");
            Actor player=root.Match.Local,enemy=root.Match.Actors[1];
            Check(player.IsSpawnProtected,"Respawn protection active");
            player.ApplyDamage(1000,enemy);Check(player.HP==100,"Protection blocks damage");
            player.CancelSpawnProtection();
            player.Armor=100;player.ApplyDamage(100,enemy,false,false,"chest",1);
            Check(Mathf.Approximately(player.HP,70)&&Mathf.Approximately(player.Armor,85),"Bot damage is reduced by 40 percent before armor");
            player.Heal(100);player.Ability.ApplyLoot("protective_field");player.ApplyDamage(30,enemy,false,true);
            Check(player.HP==100&&Mathf.Approximately(player.Shield,22),"Loot shield absorbs reduced bot damage");
            player.Ability.ApplyLoot("haste");Check(player.Shield==0&&Mathf.Approximately(player.Ability.SpeedFactor,1.25f),"New loot replaces previous effect");
            player.Ability.ResetForSpawn(false);
            for(int i=0;i<3;i++)player.Ability.RegisterKill(false);
            Check(player.Ability.Ready,"Grenade charged by three kills");
            player.Kill(enemy,false);Check(!player.Alive&&player.Ability.Ready,"DM death preserves earned unused ultimate");
            for(int i=0;i<180;i++)root.Match.SendMessage("FixedUpdate");
            Check(player.Alive&&player.Ability.Ready,"DM respawns after three simulation seconds");
            Check(root.Match.Actors.Where(a=>a!=player&&a.Alive).All(a=>Vector3.Distance(a.transform.position,player.transform.position)>=2),"Deathmatch respawn excludes occupied points");
            player.CancelSpawnProtection();enemy.CancelSpawnProtection();
            player.Ability.Initialize(player,"absolute_shot");player.Ability.ResetForSpawn(false);
            for(int i=0;i<5;i++)player.Ability.RegisterKill(false);
            Check(player.Ability.Activate()&&player.Ability.AbsoluteActive,"Absolute shot activates at five kills");
            enemy.Ability.ApplyLoot("protective_field");enemy.Kill(player,true);
            Check(!enemy.Alive,"Absolute damage bypasses armor and temporary shield");
            foreach(var actor in root.Match.Actors)Debug.Log($"SMOKE ACTOR {actor.Id} human={actor.Human} alive={actor.Alive} position={actor.transform.position} distance={Vector3.Distance(actor.transform.position,player.transform.position)}");
            foreach(var presentation in FindObjectsByType<WeaponPresentation>(FindObjectsSortMode.None)) {
                var owner=presentation.GetComponentInParent<Actor>();
                Debug.Log($"SMOKE WEAPON {presentation.WeaponId} owner={(owner?owner.Id:-1)} fp={presentation.FirstPerson} position={presentation.transform.position} local={presentation.transform.localPosition} renderers={presentation.GetComponentsInChildren<Renderer>().Count(r=>r.enabled)}");
            }
            // Capture a repeatable clear view rather than a randomly oriented respawn.
            player.Motor.enabled=false;
            player.transform.SetPositionAndRotation(root.World.LootPoints[11],Quaternion.LookRotation(Vector3.right));
            player.Aim.localRotation=Quaternion.identity;player.Motor.enabled=true;
            Physics.SyncTransforms();
            yield return Capture("02-deathmatch");
            var viewPlanes=GeometryUtility.CalculateFrustumPlanes(player.ViewCamera);
            Check(player.Weapon.Presentation && player.Weapon.Presentation.GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&GeometryUtility.TestPlanesAABB(viewPlanes,r.bounds)),"First-person weapon visible after respawn draw animation");
            yield return CaptureGroundViews(player);
            foreach(string label in RuntimeAbilityAcceptance.Run(root))Check(true,label);
            root.Match.Tick=35999;root.Match.SendMessage("FixedUpdate");root.Match.SendMessage("LateUpdate");
            Check(root.Match.Phase==MatchPhase.Results,"Deathmatch ends at 36000 ticks");
            int balance=root.Profile.credits;Check(root.Store.Current.rewardedMatches.Contains(root.Match.MatchId),"Completed match reward persisted");
            root.Store.GrantReward(root.Match.MatchId,root.Match.EarnedCredits);Check(root.Profile.credits==balance,"Duplicate reward does not add currency");
            yield return Capture("03-results");
            root.Match.ReturnToMenu();yield return null;
            root.Match.StartMatch("team");StopControllers();
            Check(root.Match.Actors.Count==24&&root.Match.Actors.Count(a=>a.Team==0)==12&&root.Match.Actors.Count(a=>a.Team==1)==12,"Team match has 12 vs 12");
            Check(root.Match.Actors.All(a=>a.MaxHP==120&&Mathf.Approximately(a.SpeedMultiplier,1.1f)&&Mathf.Approximately(a.DamageMultiplier,1.1f)),"Team progression applies equally to bots and human");
            for(int round=0;round<2;round++) {
                root.Match.PreparationRemaining=.001f;root.Match.SendMessage("FixedUpdate");
                foreach(var victim in root.Match.Actors.Where(a=>a.Team==1).ToArray())victim.Kill(root.Match.Local,false);
                root.Match.SendMessage("LateUpdate");Check(root.Match.Phase==MatchPhase.RoundEnd,"Elimination resolves team round "+(round+1));
                typeof(MatchDirector).GetField("phaseTimer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(root.Match,0f);
                root.Match.SendMessage("FixedUpdate");
            }
            Check(root.Match.Phase==MatchPhase.Results&&root.Match.RoundWins[0]==2,"Team match ends after two victories");
            yield return Capture("04-team-results");
            root.Match.ReturnToMenu();yield return null;
            // Every authored weapon must construct a first-person view without exceptions.
            foreach(var weapon in root.Catalog.Weapons) {
                var model=WeaponModelFactory.Create(root.World.PreviewAnchor,weapon,root.Store.SkinFor(weapon.Id),true);
                Check(model&&model.GetComponentsInChildren<Renderer>().Length>0,"Weapon model "+weapon.Id);
                Destroy(model.gameObject);
            }
            yield return null;
        }
        void StopControllers()
        {
            foreach(var actor in root.Match.Actors) {
                var bot=actor.GetComponent<BotBrain>();if(bot)bot.enabled=false;
                var player=actor.GetComponent<PlayerMotor>();if(player)player.enabled=false;
            }
        }

        IEnumerator CaptureGroundViews(Actor player)
        {
            var position=player.transform.position;var rotation=player.transform.rotation;var aim=player.Aim.localRotation;
            var views=new[] {
                new Vector3(-24,.16f,-5),new Vector3(-23.8f,.16f,-5),
                new Vector3(-58,.16f,3),new Vector3(-20,.16f,12)
            };
            var angles=new[] {new Vector3(25,160,0),new Vector3(25,160,0),new Vector3(28,45,0),new Vector3(32,35,0)};
            try {
                for(int i=0;i<views.Length;i++) {
                    player.Motor.enabled=false;
                    player.transform.SetPositionAndRotation(views[i],Quaternion.Euler(0,angles[i].y,0));
                    player.Aim.localRotation=Quaternion.Euler(angles[i].x,0,0);
                    Physics.SyncTransforms();
                    yield return Capture("02-ground-"+i);
                }
            } finally {
                player.transform.SetPositionAndRotation(position,rotation);player.Aim.localRotation=aim;
                player.Motor.enabled=true;Physics.SyncTransforms();
            }
        }
    }
}
