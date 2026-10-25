using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    // Opt-in relay acceptance with three real player processes and a local lobby service.
    public sealed class RuntimeLobbySmoke : MonoBehaviour
    {
        [Serializable] sealed class Stage
        {
            public string name, code;
            public int friend, rival, count, tick;
        }
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        readonly HashSet<string> actions = new HashSet<string>();
        GameRoot root;
        string name, role, mode, endpoint, directory;
        const float FloorY = 179;
        static string Arg(string key, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, key);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }
        IEnumerator Start()
        {
            root = GameRoot.Instance; role = Arg("-zonez-lobby-test", "friend");
            name = role == "host" ? "Host" : role == "rival" ? "Rival" : "Friend";
            mode = Arg("-zonez-lobby-mode", "team"); endpoint = Arg("-zonez-lobby-server", "tcp://127.0.0.1:17780");
            directory = Arg("-zonez-report", Path.Combine(Path.GetTempPath(), "ZoneZ_LobbySmoke")); Directory.CreateDirectory(directory);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            Application.logMessageReceived += OnLog;
            string failure = null; var stack = new Stack<IEnumerator>(); stack.Push(Exercise());
            while (stack.Count > 0)
            {
                bool more = false; object current = null;
                try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                catch (Exception ex) { failure = ex.ToString(); }
                if (failure != null) break;
                if (!more) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
            Application.logMessageReceived -= OnLog;
            bool passed = failure == null && errors.Count == 0;
            Write(name + ".json", new { passed, name, role, mode, endpoint, checks, errors, failure,
                commands = root.Network.CommandsReceived, snapshots = root.Network.SnapshotsReceived, utc = DateTime.UtcNow });
            Debug.Log(passed ? "ZONEZ_LOBBY_SMOKE_OK " + name : "ZONEZ_LOBBY_SMOKE_FAILED " + name + " " + failure);
            Application.Quit(passed ? 0 : 2);
        }
        void OnLog(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) errors.Add(text + "\n" + stack);
        }
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label); Debug.Log("LOBBY PASS " + name + " " + label);
        }
        void Write(string file, object value) => File.WriteAllText(Path.Combine(directory, file), JsonConvert.SerializeObject(value, Formatting.Indented));
        void Publish(Stage stage) => Write("stage.json", stage);
        bool Exists(string who, string stage) => File.Exists(Path.Combine(directory, who + "-" + stage + ".json"));
        void Ack(string stage, string label)
        {
            if (!actions.Add("ack-" + stage)) return;
            Check(true, label); Write(name + "-" + stage + ".json", new { passed = true, stage });
        }
        Stage Read()
        {
            try { return JsonConvert.DeserializeObject<Stage>(File.ReadAllText(Path.Combine(directory, "stage.json"))); }
            catch (IOException) { return null; }
            catch (JsonException) { return null; }
        }
        IEnumerator Wait(Func<bool> test, float seconds, string message)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!test())
            {
                if (Time.realtimeSinceStartup > until) throw new TimeoutException(message + "; lobby: " + root.Lobby.Status + "; game: " + root.Network.Status);
                yield return null;
            }
        }
        IEnumerator Both(string stage) => Wait(() => Exists("Friend", stage) && Exists("Rival", stage), 20, "Both clients acknowledge " + stage);
        Actor Named(string actorName) => root.Match.Actors.FirstOrDefault(actor => actor.DisplayName == actorName);
        void Idle() => root.Network.SubmitInput(new NetworkCommand());
        bool HumanRoster(int count) => root.Network.UsesRelay && root.Network.HumanCount == count && root.Match.Actors.Count == count && root.Match.Actors.All(a => a.Human || a.RemoteControlled);
        bool TeamsCorrect() => mode != "team" || Named("Host") && Named("Friend") && Named("Rival") && Named("Host").Team == Named("Friend").Team && Named("Host").Team != Named("Rival").Team;

        IEnumerator Exercise()
        {
            Check(!string.IsNullOrEmpty(Arg("-zonez-profile")), "Disposable profile explicitly supplied");
            while (!Application.isBatchMode && !UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            root.Network.TestInputOverride = true;
            root.Lobby.Connect(endpoint, name);
            yield return Wait(() => root.Lobby.Connected, 25, "Connect to actual lobby service");
            Check(root.Lobby.Connected, "Lobby service handshake succeeds");
            if (role == "host") yield return Host(); else yield return Client();
        }

        IEnumerator Host()
        {
            root.Lobby.Create(mode);
            yield return Wait(() => root.Lobby.InLobby && root.Lobby.IsLeader && root.Lobby.Code.Length > 0, 12, "Host creates party");
            Publish(new Stage { name = "party", code = root.Lobby.Code });
            yield return Wait(() => root.Lobby.Players.Length == 2 && Exists("Friend", "party") && Exists("Rival", "party"), 35, "Friend joins party and rival creates separate party");
            Check(root.Lobby.Players.Any(p => p.name == "Friend"), "Invite code adds friend to host party");
            root.Lobby.Search();
            yield return Wait(() => root.Lobby.Searching, 10, "Host enters matchmaking first");
            yield return new WaitForSecondsRealtime(.6f);
            Check(root.Lobby.Searching && !root.Network.IsActive, "Searching alone waits for actual opponents without adding bots");
            Publish(new Stage { name = "queue_rival" });
            yield return Wait(() => root.Network.IsHost && HumanRoster(3) && Named("Friend") && Named("Rival"), 35, "Matchmaker starts three-person relayed game");
            Check(root.Network.IsHost, "First queued party leader becomes simulation host");
            Check(HumanRoster(3), "Matched game contains three real humans and no bots");
            Check(TeamsCorrect(), "Friend party remains together against rival party");
            root.Match.Local.GetComponent<PlayerMotor>().enabled = false;
            root.Match.PreparationRemaining = 0;
            yield return Wait(() => root.Match.Phase == MatchPhase.Playing, 5, "Relay match begins playing");
            Publish(new Stage { name = "match", count = 3 }); yield return Both("match");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "LobbySmokeFloor";
            floor.transform.position = new Vector3(0, FloorY - .5f, 0); floor.transform.localScale = new Vector3(40, 1, 40);
            Actor friend = Named("Friend"), rival = Named("Rival");
            Place(root.Match.Local, new Vector3(12, FloorY, 12)); Place(friend, new Vector3(0, FloorY, -4)); Place(rival, new Vector3(0, FloorY, 6));
            Physics.SyncTransforms();
            int commands = root.Network.CommandsReceived;
            Publish(new Stage { name = "move", friend = friend.Id, rival = rival.Id });
            yield return Wait(() => friend.transform.position.x > 1.2f, 15, "Relay forwards friend input to game host");
            Check(root.Network.CommandsReceived > commands, "Relayed input advances server movement");
            Publish(new Stage { name = "movement", friend = friend.Id, rival = rival.Id }); yield return Both("movement");
            int tick = root.Match.Tick; root.SetPaused(true);
            Publish(new Stage { name = "pause", tick = tick });
            yield return Wait(() => root.Match.Tick > tick + 30, 8, "Relayed simulation advances while host menu is open");
            Check(root.Paused && !root.SimulationPaused, "Relay match is not paused by host menu");
            yield return Both("pause"); root.SetPaused(false);
            Publish(new Stage { name = "close" }); root.Match.ReturnToMenu();
            yield return Both("close");
            Check(root.Lobby.Connected && !root.Lobby.InGame && root.Match.Phase == MatchPhase.Menu, "Closing match preserves lobby-service connection for next party");
            Destroy(floor);

            root.Lobby.Create(mode);
            yield return Wait(() => root.Lobby.InLobby && root.Lobby.IsLeader && root.Lobby.Code.Length > 0, 12, "Create second party after returning to menu");
            Publish(new Stage { name = "bots_party", code = root.Lobby.Code });
            yield return Wait(() => root.Lobby.Players.Length == 2 && Exists("Friend", "bots_party"), 20, "Friend rejoins second party");
            root.Lobby.StartWithBots();
            int expected = mode == "team" ? 24 : 20;
            yield return Wait(() => root.Network.IsHost && root.Network.HumanCount == 2 && root.Match.Actors.Count == expected, 25, "Explicit bot action starts populated game");
            foreach (var actor in root.Match.Actors) { var brain = actor.GetComponent<BotBrain>(); if (brain) { brain.StopNavigation(); brain.enabled = false; } }
            root.Match.Local.GetComponent<PlayerMotor>().enabled = false;
            Check(root.Network.UsesRelay && root.Match.Actors.Count(a => !a.Human && !a.RemoteControlled) == expected - 2, "Explicit bot mode contains two humans plus requested bots");
            if (mode == "team") Check(Named("Host").Team == Named("Friend").Team, "Friends also stay together in explicit bot match");
            Publish(new Stage { name = "bots_match", count = expected });
            yield return Wait(() => Exists("Friend", "bots_match"), 20, "Friend sees explicit bot roster");
            Publish(new Stage { name = "bots_close" }); root.Match.ReturnToMenu();
            yield return Wait(() => Exists("Friend", "bots_close"), 20, "Second relayed match closes cleanly");
            root.Lobby.Disconnect(); Check(!root.Lobby.Connected && !root.Network.IsActive, "Lobby and gameplay connections close cleanly");
        }

        void Place(Actor actor, Vector3 point)
        {
            actor.Ability.ResetForSpawn(false); actor.Respawn(point, Quaternion.identity); actor.SpawnProtectedUntil = 0;
        }

        IEnumerator Client()
        {
            float deadline = Time.realtimeSinceStartup + 180;
            while (Time.realtimeSinceStartup < deadline)
            {
                Stage stage = Read(); if (stage == null) { yield return null; continue; }
                if (stage.name != "move") Idle();
                switch (stage.name)
                {
                    case "party":
                        if (actions.Add("create-party")) { if (role == "friend") root.Lobby.Join(stage.code); else root.Lobby.Create(mode); }
                        if (root.Lobby.InLobby && (role == "rival" && root.Lobby.IsLeader || role == "friend" && root.Lobby.Code == stage.code && root.Lobby.Players.Length == 2))
                            Ack("party", role == "friend" ? "Friend joins invite-code party" : "Rival creates separate real-player party");
                        break;
                    case "queue_rival":
                        if (role == "rival" && actions.Add("search")) root.Lobby.Search();
                        break;
                    case "match":
                        if (root.Network.IsClient && root.Network.SnapshotsReceived > 1 && HumanRoster(3) && TeamsCorrect())
                            Ack("match", "Relayed three-human roster and party teams reach client");
                        break;
                    case "move":
                        root.Network.SubmitInput(new NetworkCommand { moveX = role == "friend" ? 1 : 0 });
                        break;
                    case "movement":
                        Actor moving = root.Match.Actors.Find(a => a.Id == stage.friend);
                        if (moving && moving.transform.position.x > .5f) Ack("movement", "Movement snapshot travels back through lobby relay");
                        break;
                    case "pause":
                        if (root.Match.Tick > stage.tick + 15) Ack("pause", "Relay snapshots continue while host pause menu is open");
                        break;
                    case "close":
                        if (!root.Network.IsActive && root.Match.Phase == MatchPhase.Menu && !root.Lobby.InGame)
                        {
                            Ack("close", "Host leaves and client returns to menu through relay ended event");
                            if (role == "rival") { root.Lobby.Disconnect(); yield break; }
                        }
                        break;
                    case "bots_party":
                        if (actions.Add("join-bots")) root.Lobby.Join(stage.code);
                        if (root.Lobby.InLobby && root.Lobby.Code == stage.code && root.Lobby.Players.Length == 2)
                            Ack("bots_party", "Same connection joins a fresh party after previous match");
                        break;
                    case "bots_match":
                        if (root.Network.IsClient && root.Network.UsesRelay && root.Network.HumanCount == 2 && root.Match.Actors.Count == stage.count &&
                            root.Match.Actors.Count(a => !a.Human && !a.RemoteControlled) == stage.count - 2)
                            Ack("bots_match", "Explicit bot match replicates full roster with two humans");
                        break;
                    case "bots_close":
                        if (!root.Network.IsActive && root.Match.Phase == MatchPhase.Menu && !root.Lobby.InGame)
                        {
                            Ack("bots_close", "Second relayed match closes without stale actors"); root.Lobby.Disconnect(); yield break;
                        }
                        break;
                }
                yield return null;
            }
            throw new TimeoutException("Lobby test client workflow exceeded time limit");
        }
    }
}
