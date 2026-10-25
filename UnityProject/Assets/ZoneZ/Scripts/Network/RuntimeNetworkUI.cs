using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    // Explicit graphical acceptance runner. The launcher supplies an isolated
    // temporary profile; no production settings or existing lobby are reused.
    public sealed class RuntimeNetworkUI : MonoBehaviour
    {
        [Serializable]
        sealed class Screenshot
        {
            public string name, file;
            public int width, height, darkest, brightest, sampledColors;
        }

        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        readonly List<Screenshot> screenshots=new List<Screenshot>();
        GameRoot root;
        string directory, endpoint;
        int port;
        const BindingFlags PrivateField=BindingFlags.Instance|BindingFlags.NonPublic;

        static string Argument(string key,string fallback=null)
        {
            var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,key);
            return index>=0&&index+1<args.Length?args[index+1]:fallback;
        }

        IEnumerator Start()
        {
            root=GameRoot.Instance;
            directory=Argument("-zonez-report",Path.Combine(Path.GetTempPath(),"ZoneZ_NetworkUI_"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            endpoint=Argument("-zonez-ui-lobby-server","");
            Application.logMessageReceived+=OnLog;
            string failure=null;
            var pending=new Stack<IEnumerator>(); pending.Push(Exercise());
            while(pending.Count>0) {
                bool more=false; object current=null;
                try { more=pending.Peek().MoveNext(); if(more) current=pending.Peek().Current; }
                catch(Exception e) { failure=e.ToString(); }
                if(failure!=null) break;
                if(!more) { pending.Pop(); continue; }
                if(current is IEnumerator nested) { pending.Push(nested); continue; }
                yield return current;
            }
            try { root?.Match?.ReturnToMenu(); root?.Lobby?.Disconnect(); }
            catch(Exception e) { errors.Add("Cleanup: "+e); }
            Application.logMessageReceived-=OnLog;
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(directory,"network-ui.json"),JsonConvert.SerializeObject(new {
                passed, checks, errors, failure, screenshots, endpoint,
                unity=Application.unityVersion, utc=DateTime.UtcNow,
                note="Pixel checks confirm rendered, correctly sized frames. Layout still requires visual inspection."
            },Formatting.Indented));
            Debug.Log(passed?"ZONEZ_NETWORK_UI_OK":"ZONEZ_NETWORK_UI_FAILED "+failure);
            Application.Quit(passed?0:2);
        }

        void OnLog(string message,string stack,LogType type)
        {
            if(type==LogType.Error||type==LogType.Exception) errors.Add(message+"\n"+stack);
        }

        void Check(bool condition,string label)
        {
            if(!condition) throw new InvalidOperationException(label);
            checks.Add(label); Debug.Log("NETWORK UI PASS "+label);
        }

        void SetUI(string field,object value)
        {
            var member=typeof(GameUI).GetField(field,PrivateField);
            if(member==null) throw new MissingFieldException(typeof(GameUI).FullName,field);
            member.SetValue(root.UI,value);
        }

        IEnumerator Wait(Func<bool> condition,float seconds,string label)
        {
            float until=Time.realtimeSinceStartup+seconds;
            while(!condition()) {
                if(Time.realtimeSinceStartup>until) throw new TimeoutException(label+"; lobby: "+root.Lobby.Status+"; network: "+root.Network.Status);
                yield return null;
            }
        }

        IEnumerator Capture(string name)
        {
            // OnGUI and the camera must both finish a real graphical frame.
            // WaitForEndOfFrame deliberately runs without -batchmode.
            yield return new WaitForSecondsRealtime(.6f);
            yield return new WaitForEndOfFrame();
            Texture2D capture=ScreenCapture.CaptureScreenshotAsTexture();
            Check(capture,"Screenshot texture exists: "+name);
            try {
                Check(capture.width==1600&&capture.height==900,"Screenshot is 1600 x 900: "+name);
                Color32[] pixels=capture.GetPixels32();
                int darkest=765,brightest=0;
                var colors=new HashSet<int>();
                for(int i=0;i<pixels.Length;i+=97) {
                    Color32 pixel=pixels[i]; int brightness=pixel.r+pixel.g+pixel.b;
                    darkest=Math.Min(darkest,brightness); brightest=Math.Max(brightest,brightness);
                    colors.Add(((pixel.r>>4)<<8)|((pixel.g>>4)<<4)|(pixel.b>>4));
                }
                Check(brightest-darkest>=24&&colors.Count>=8,"Screenshot contains nonblank rendered pixels: "+name);
                string path=Path.Combine(directory,name+".png");
                File.WriteAllBytes(path,capture.EncodeToPNG());
                screenshots.Add(new Screenshot { name=name,file=path,width=capture.width,height=capture.height,
                    darkest=darkest,brightest=brightest,sampledColors=colors.Count });
            }
            finally { Destroy(capture); }
            yield return null;
        }

        IEnumerator Exercise()
        {
            Check(!Application.isBatchMode,"Graphical player launched without batch mode");
            string profile=Argument("-zonez-profile");
            Check(!string.IsNullOrWhiteSpace(profile),"Explicit disposable profile supplied");
            string temporaryRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            Check(Path.GetFullPath(profile).StartsWith(temporaryRoot,StringComparison.OrdinalIgnoreCase),"Disposable profile is inside the temporary directory");
            Check(int.TryParse(Argument("-zonez-ui-port","18779"),out port)&&port>=1024&&port<=65535,"UI fixture TCP port is valid");
            Check(root&&root.UI&&root.Network&&root.Lobby,"Game and network UI initialized");
            Application.runInBackground=true; Application.targetFrameRate=60; QualitySettings.vSyncCount=0;
            Screen.SetResolution(1600,900,false);
            root.Profile.uiScale=1;
            yield return Wait(()=>UnityEngine.Rendering.SplashScreen.isFinished,30,"Splash screen completes");
            yield return Wait(()=>Screen.width==1600&&Screen.height==900,12,"Requested graphical resolution applies");

            root.Match.ReturnToMenu(); root.Lobby.Disconnect();
            var page=typeof(GameUI).GetField("page",PrivateField);
            Check(page!=null,"Play page is available to UI acceptance fixture");
            page.SetValue(root.UI,Enum.ToObject(page.FieldType,0));
            SetUI("networkOnline",false); SetUI("networkDirect",false);
            SetUI("toast",null); SetUI("exitConfirm",false); SetUI("pauseSettings",false);
            yield return Capture("01-local");

            SetUI("networkOnline",true); SetUI("networkDirect",false);
            SetUI("lobbyFieldsLoaded",true); SetUI("lobbyEndpoint","");
            yield return Capture("02-lobby-connect");

            SetUI("networkDirect",true); SetUI("networkHost",true);
            SetUI("networkNickname","Хозяин"); SetUI("networkPort",port.ToString()); SetUI("networkHostCode","DRUZYA");
            yield return Capture("03-direct-host");

            SetUI("networkHost",false); SetUI("networkNickname","Друг");
            SetUI("networkAddress","192.168.1.100"); SetUI("networkJoinCode","DRUZYA");
            yield return Capture("04-direct-join");

            Check(root.Network.Host("Хозяин","team",port,"DRUZYA"),"Direct host opens for pause-menu screenshot");
            // Keep combat from interrupting visual acceptance with a death overlay.
            foreach(var actor in root.Match.Actors) {
                var brain=actor.GetComponent<BotBrain>(); if(brain) { brain.StopNavigation(); brain.enabled=false; }
            }
            root.Match.PreparationRemaining=0;
            yield return Wait(()=>root.Match.Phase==MatchPhase.Playing,6,"Host reaches live match");
            SetUI("toast",null); root.SetPaused(true);
            int before=root.Match.Tick;
            yield return Capture("05-host-pause");
            Check(root.Paused&&!root.SimulationPaused&&Mathf.Approximately(Time.timeScale,1)&&root.Match.Tick>before,"Online menu remains open while simulation continues");
            root.Match.ReturnToMenu();

            if(!string.IsNullOrWhiteSpace(endpoint)) {
                SetUI("networkOnline",true); SetUI("networkDirect",false); SetUI("lobbyEndpoint",endpoint);
                root.Lobby.Connect(endpoint,"Проверка интерфейса");
                yield return Wait(()=>root.Lobby.Connected,15,"Optional lobby service connects");
                root.Lobby.Create("team");
                yield return Wait(()=>root.Lobby.InLobby&&root.Lobby.IsLeader,10,"Optional real lobby is created");
                SetUI("toast",null);
                yield return Capture("06-lobby-room");
                root.Lobby.Search();
                yield return Wait(()=>root.Lobby.Searching,6,"Optional lobby enters waiting search");
                yield return Capture("07-lobby-search");
                root.Lobby.CancelSearch();
                yield return Wait(()=>!root.Lobby.Searching,6,"Optional search cancellation returns lobby to ready state");
                root.Lobby.Leave(); root.Lobby.Disconnect();
            }
            Check(screenshots.Count==(string.IsNullOrWhiteSpace(endpoint)?5:7),"All requested UI screenshots were written");
        }
    }
}
