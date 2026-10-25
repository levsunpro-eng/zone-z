using System;
using UnityEngine;

namespace ZoneZ
{
    [DefaultExecutionOrder(-500)]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance;
        public GameCatalog Catalog;
        public ProfileStore Store;
        public MatchDirector Match;
        public GameUI UI;
        public ZooWorld World;
        public NetworkSession Network;
        public LobbyClient Lobby;
        public bool Paused;
        public bool SimulationPaused => Paused && !(Network && Network.IsActive);
        public GameProfile Profile => Store?.Current;
        public Camera MenuCamera;
        public string FatalError;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.targetFrameRate = 120;
            Time.fixedDeltaTime = 1f / 60f;
            Time.maximumDeltaTime = .1f;
            try {
                Catalog = GameCatalog.Load();
                string[] args=Environment.GetCommandLineArgs();
                int profileArgument=Array.IndexOf(args,"-zonez-profile");
                bool smoke=Array.IndexOf(args,"-zonez-net-test")>=0||Array.IndexOf(args,"-zonez-lobby-test")>=0||Array.IndexOf(args,"-zonez-smoke")>=0||Array.IndexOf(args,"-zonez-soak")>=0||Array.IndexOf(args,"-zonez-benchmark")>=0||Array.IndexOf(args,"-zonez-art-review")>=0||Array.IndexOf(args,"-zonez-arsenal-review")>=0;
                Store=profileArgument>=0 && profileArgument+1<args.Length?ProfileStore.Load(args[profileArgument+1]):smoke?ProfileStore.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ZoneZ_Smoke_"+Guid.NewGuid().ToString("N"))):ProfileStore.Load();
                Store.PurchasesAllowed=()=>Match==null||Match.Phase==MatchPhase.Menu;
                AudioListener.volume = Profile.volume;
                World = ZooWorld.Build(transform);
                var cameraObject = new GameObject("MenuCamera"); cameraObject.transform.SetParent(transform);
                MenuCamera = cameraObject.AddComponent<Camera>(); MenuCamera.nearClipPlane=.1f; MenuCamera.farClipPlane=500; MenuCamera.fieldOfView=60;
                WorldLook.ConfigureCamera(MenuCamera);
                cameraObject.tag = "MainCamera"; cameraObject.AddComponent<AudioListener>();
                if (World.MenuCameraAnchor) cameraObject.transform.SetPositionAndRotation(World.MenuCameraAnchor.position,World.MenuCameraAnchor.rotation);
                Match = gameObject.AddComponent<MatchDirector>();
                Network = gameObject.AddComponent<NetworkSession>();
                Lobby = gameObject.AddComponent<LobbyClient>();
                gameObject.AddComponent<DamageBatchEnd>().Match=Match;
                UI = gameObject.AddComponent<GameUI>(); UI.Initialize(this);
                SetPaused(false);
                if (Array.IndexOf(Environment.GetCommandLineArgs(),"-zonez-smoke") >= 0) gameObject.AddComponent<RuntimeSmoke>();
                if (Array.IndexOf(args,"-zonez-soak")>=0||Array.IndexOf(args,"-zonez-benchmark")>=0)gameObject.AddComponent<RuntimeSoak>();
                if (Array.IndexOf(args,"-zonez-net-test")>=0)gameObject.AddComponent<RuntimeNetworkSmoke>();
                if (Array.IndexOf(args,"-zonez-lobby-test")>=0)gameObject.AddComponent<RuntimeLobbySmoke>();
                if (Array.IndexOf(args,"-zonez-net-ui")>=0)gameObject.AddComponent<RuntimeNetworkUI>();
            } catch(Exception error) {
                FatalError=error.ToString(); Debug.LogException(error); Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
                string[] args=Environment.GetCommandLineArgs();
                string mode=Array.IndexOf(args,"-zonez-smoke")>=0?"runtime-smoke":Array.IndexOf(args,"-zonez-soak")>=0?"soak":Array.IndexOf(args,"-zonez-benchmark")>=0?"benchmark":null;
                if(mode!=null) {
                    int reportArgument=Array.IndexOf(args,"-zonez-report");
                    if(reportArgument>=0 && reportArgument+1<args.Length) {
                        try {
                            System.IO.Directory.CreateDirectory(args[reportArgument+1]);
                            System.IO.File.WriteAllText(System.IO.Path.Combine(args[reportArgument+1],mode+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(new { passed=false, stage="startup", error=FatalError },Newtonsoft.Json.Formatting.Indented));
                        } catch(Exception reportError) { Debug.LogException(reportError); }
                    }
                    Application.Quit(1);
                }
            }
        }

        public void SetPaused(bool paused)
        {
            Paused=paused; Time.timeScale=SimulationPaused ? 0 : 1;
            bool play=Match && Match.Phase==MatchPhase.Playing && !paused && Match.Local && Match.Local.Alive;
            Cursor.lockState=play?CursorLockMode.Locked:CursorLockMode.None;
            Cursor.visible=!play;
        }
        public void Notify(string message) { if (UI) UI.Notify(message); else Debug.Log(message); }

        void LateUpdate()
        {
            if (!Match || !MenuCamera) return;
            bool spectating=Match.Local && !Match.Local.Alive && Match.IsTeamMode && Match.Phase==MatchPhase.Playing && Match.Actors.Exists(a=>a.Alive && a.Team==Match.Local.Team);
            bool playerView=Match.Local && (Match.Local.Alive || spectating) && Match.Phase!=MatchPhase.Menu && Match.Phase!=MatchPhase.Results;
            if(Match.Local && Match.Local.ViewCamera) Match.Local.ViewCamera.gameObject.SetActive(playerView);
            MenuCamera.gameObject.SetActive(!playerView);
            if (!playerView && Match.Local && Match.Phase==MatchPhase.Playing && Match.IsTeamMode) {
                Actor follow=Match.Actors.Find(a=>a.Alive && a.Team==Match.Local.Team);
                if(follow) MenuCamera.transform.SetPositionAndRotation(follow.EyePosition,follow.Aim.rotation);
            } else if(!playerView && World.MenuCameraAnchor) {
                MenuCamera.transform.SetPositionAndRotation(World.MenuCameraAnchor.position,World.MenuCameraAnchor.rotation);
            }
        }
        void OnApplicationQuit() { Store?.Save(); Time.timeScale=1; }
        void OnGUI()
        {
            if(string.IsNullOrEmpty(FatalError))return;
            GUI.Box(new Rect(30,30,Screen.width-60,Screen.height-60),"Ошибка запуска Зоны Z");
            GUI.TextArea(new Rect(50,80,Screen.width-100,Screen.height-170),FatalError);
            if(GUI.Button(new Rect(50,Screen.height-70,180,32),"Закрыть")) Application.Quit(1);
        }
    }
}
