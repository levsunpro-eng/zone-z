using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    // Autoplay goes through real rules; it does not force scores, kills, timers or rewards.
    public sealed class RuntimeSoak : MonoBehaviour
    {
        readonly List<string> errors=new List<string>();
        readonly List<object> results=new List<object>();
        readonly List<float> frames=new List<float>(),fullRosterFrames=new List<float>();
        readonly Dictionary<int,Vector3> lastPositions=new Dictionary<int,Vector3>();
        readonly Dictionary<int,float> distances=new Dictionary<int,float>();
        GameRoot root;
        string directory;
        bool benchmark,measuring;
        float started;
        IEnumerator Start()
        {
            root=GameRoot.Instance;var args=Environment.GetCommandLineArgs();benchmark=Array.IndexOf(args,"-zonez-benchmark")>=0;
            int at=Array.IndexOf(args,"-zonez-report");directory=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../Diagnostics"));
            Directory.CreateDirectory(directory);Application.logMessageReceived+=OnLog;
            if(benchmark){QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;}
            while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
            yield return null;
            // Exercise a progressed loadout on an isolated QA profile so bot decision
            // paths include purchasable ultimates as well as the starter grenade.
            root.Profile.ownedAbilities=root.Catalog.Abilities.Select(a=>a.Id).ToList();
            root.Profile.ultimateId="underbarrel";
            foreach(string mode in benchmark?new[]{"team"}:new[]{"team","deathmatch"}) {
                root.Match.StartMatch(mode);EnableAutoplay();
                Time.timeScale=benchmark?1:4;started=Time.realtimeSinceStartup;lastPositions.Clear();distances.Clear();
                while(root.Match.Phase!=MatchPhase.Results && Time.realtimeSinceStartup-started<(benchmark?90:420)) {
                    if(!benchmark)Time.timeScale=4;
                    measuring=root.Match.Phase==MatchPhase.Playing && Time.realtimeSinceStartup-started>20;
                    if(benchmark && frames.Count>0 && Time.realtimeSinceStartup-started>=75)break;
                    yield return null;
                }
                measuring=false;
                if(!benchmark && root.Match.Phase!=MatchPhase.Results)errors.Add(mode+": match did not finish within real-time safety timeout");
                if(!benchmark && root.Match.Actors.Sum(a=>a.Kills)==0)errors.Add(mode+": bots recorded no kills");
                results.Add(new {
                    mode,phase=root.Match.Phase.ToString(),rounds=root.Match.RoundNumber,roundWins=root.Match.RoundWins,
                    elapsedRealSeconds=Time.realtimeSinceStartup-started,kills=root.Match.Actors.Sum(a=>a.Kills),
                    participants=root.Match.Actors.Select(a=>new{a.DisplayName,a.Kills,a.Deaths,a.Assists,a.Score,ultimate=a.Ability.EquippedId,distance=distances.TryGetValue(a.Id,out float d)?d:0}).ToArray(),
                    credits=root.Match.EarnedCredits
                });
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"soak-"+mode+".png"));yield return null;
                root.Match.ReturnToMenu();yield return null;
            }
            Time.timeScale=1;Application.logMessageReceived-=OnLog;
            if(benchmark&&frames.Count<120)errors.Add("Insufficient rendered benchmark frames");
            var report=new {passed=errors.Count==0,benchmark,errors,results,frameTimeMs=Stats(frames),fullRosterFrameTimeMs=Stats(fullRosterFrames),gpu=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,memoryMB=SystemInfo.systemMemorySize,width=Screen.width,height=Screen.height,unity=Application.unityVersion,utc=DateTime.UtcNow};
            File.WriteAllText(Path.Combine(directory,benchmark?"benchmark.json":"soak.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            Debug.Log(errors.Count==0?"ZONEZ_SOAK_OK":"ZONEZ_SOAK_FAILED");Application.Quit(errors.Count==0?0:2);
        }
        static object Stats(List<float> values)
        {
            if(values.Count==0)return new{samples=0};
            var sorted=values.OrderBy(x=>x).ToArray();return new{samples=values.Count,mean=values.Average(),median=sorted[sorted.Length/2],p99=sorted[Mathf.Min(sorted.Length-1,(int)(sorted.Length*.99f))],meanFps=1000/values.Average()};
        }
        void EnableAutoplay()
        {
            root.Match.Local.GetComponent<PlayerMotor>().enabled=false;
            var bot=root.Match.Local.gameObject.AddComponent<BotBrain>();bot.Owner=root.Match.Local;
        }
        void Update()
        {
            if(!root||!root.Match||root.Match.Phase!=MatchPhase.Playing)return;
            if(measuring&&benchmark) {
                float ms=Time.unscaledDeltaTime*1000;frames.Add(ms);
                if(root.Match.Actors.Count(a=>a.Alive)==24)fullRosterFrames.Add(ms);
            }
            foreach(var actor in root.Match.Actors) {
                if(!actor.Alive)continue;
                if(lastPositions.TryGetValue(actor.Id,out var position)) {
                    float delta=Vector3.Distance(position,actor.transform.position);
                    if(delta<3){if(!distances.ContainsKey(actor.Id))distances[actor.Id]=0;distances[actor.Id]+=delta;}
                }
                lastPositions[actor.Id]=actor.transform.position;
            }
        }
        void OnLog(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text+"\n"+stack);}
    }
}
