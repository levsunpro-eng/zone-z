using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZoneZ
{
    // Explicit opt-in, separate-process acceptance runner. Every process must use
    // a disposable -zonez-profile and its own log; the report directory is shared.
    public sealed class RuntimeNetworkSmoke : MonoBehaviour
    {
        [Serializable]
        sealed class Stage
        {
            public string name;
            public int first, second, count, tick, round, magazine;
            public float startX, hp, fullHP;
        }

        readonly List<string> checks = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly HashSet<string> acknowledged = new HashSet<string>();
        GameRoot root;
        string directory, role, name, mode;
        int port;
        Stage stage;
        const float FixtureY = 179;

        static string Argument(string key, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        IEnumerator Start()
        {
            root = GameRoot.Instance; role = Argument("-zonez-net-test", "client");
            name = role == "host" ? "Host" : Argument("-zonez-net-name", "Client1");
            mode = Argument("-zonez-net-mode", "deathmatch");
            directory = Argument("-zonez-report", Path.Combine(Path.GetTempPath(), "ZoneZ_NetworkSmoke"));
            Directory.CreateDirectory(directory);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            Application.logMessageReceived += OnLog;
            string failure = null;
            // Flatten nested coroutines so exceptions from waits and probes also
            // produce an explicit failed report instead of silently stopping.
            var pending = new Stack<IEnumerator>(); pending.Push(Exercise());
            while (pending.Count > 0)
            {
                bool more = false; object current = null;
                try { more = pending.Peek().MoveNext(); if (more) current = pending.Peek().Current; }
                catch (Exception e) { failure = e.ToString(); }
                if (failure != null) break;
                if (!more) { pending.Pop(); continue; }
                if (current is IEnumerator nested) { pending.Push(nested); continue; }
                yield return current;
            }
            Application.logMessageReceived -= OnLog;
            bool passed = failure == null && errors.Count == 0;
            Write(name + ".json", new { passed, name, role, mode, checks, errors, failure,
                commands = root.Network.CommandsReceived, snapshots = root.Network.SnapshotsReceived,
                unity = Application.unityVersion, utc = DateTime.UtcNow });
            Debug.Log(passed ? "ZONEZ_NETWORK_SMOKE_OK " + name : "ZONEZ_NETWORK_SMOKE_FAILED " + name + " " + failure);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator Exercise()
        {
            Check(!string.IsNullOrEmpty(Argument("-zonez-profile")), "Explicit disposable profile supplied");
            Check(int.TryParse(Argument("-zonez-port", "17777"), out port), "Test port is valid");
            while (!Application.isBatchMode && !UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return null;
            root.Profile.primaryId = "weapon_ak47"; root.Profile.pistolId = "weapon_glock";
            root.Network.TestInputOverride = true;
            if (role == "host") yield return Host(); else yield return Client();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) errors.Add(message + "\n" + stack);
        }
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label); Debug.Log("NETWORK PASS " + name + " " + label);
        }
        void Write(string file, object value) => File.WriteAllText(Path.Combine(directory, file), JsonConvert.SerializeObject(value, Formatting.Indented));
        void Publish(Stage next) { stage = next; Write("stage.json", next); }
        bool AckExists(string client, string step) => File.Exists(Path.Combine(directory, client + "-" + step + ".json"));
        void Ack(string step, string label)
        {
            if (!acknowledged.Add(step)) return;
            Check(true, label); Write(name + "-" + step + ".json", new { passed = true, step, tick = root.Match.Tick });
        }
        IEnumerator Wait(Func<bool> condition, float seconds, string failure)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(failure + "; network: " + root.Network.Status);
                yield return null;
            }
        }
        IEnumerator Both(string step) => Wait(() => AckExists("Client1", step) && AckExists("Client2", step), 15, "Both clients acknowledge " + step);
        Actor Named(string actorName) => root.Match.Actors.FirstOrDefault(actor => actor.DisplayName == actorName);

        void StopBots()
        {
            foreach (var actor in root.Match.Actors)
            {
                var bot = actor.GetComponent<BotBrain>();
                if (bot) { bot.StopNavigation(); bot.enabled = false; }
            }
        }
        void Place(Actor actor, Vector3 position, float yaw)
        {
            actor.Ability.ResetForSpawn(false); actor.Respawn(position, Quaternion.Euler(0, yaw, 0));
            actor.SpawnProtectedUntil = 0; actor.Armor = 0;
        }

        IEnumerator Host()
        {
            Check(root.Network.Host("Host", mode, port, "NETTEST"), "Host listens on requested TCP port");
            StopBots(); root.Match.Local.GetComponent<PlayerMotor>().enabled = false;
            root.Match.PreparationRemaining = 0;
            Publish(new Stage { name = "connect", count = mode == "team" ? 24 : 20 });
            yield return RejectionProbe("WRONG", NetworkSession.ProtocolVersion, "Неверный код", "Wrong room code is rejected");
            yield return RejectionProbe("NETTEST", NetworkSession.ProtocolVersion + 1, "версии", "Protocol mismatch is rejected");
            yield return Wait(() => root.Network.HumanCount == 3 && Named("Client1") && Named("Client2"), 45, "Two clients join host");
            Check(root.Match.Actors.Count == (mode == "team" ? 24 : 20), "Joining replaces bots without changing roster size");
            Check(root.Match.Actors.All(a => a.MaxHP == 100 && a.SpeedMultiplier == 1 && a.DamageMultiplier == 1), "Online actors have equal base combat stats");
            Publish(new Stage { name = "joined", count = root.Match.Actors.Count });
            yield return Both("joined");
            Actor first = Named("Client1"), second = Named("Client2");
            Check(first.RemoteControlled && second.RemoteControlled && !first.Human && !second.Human, "Server owns two remote input actors");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "NetworkSmokeFloor";
            floor.transform.position = new Vector3(0, FixtureY - .5f, 0); floor.transform.localScale = new Vector3(40, 1, 40);
            StopBots(); Place(root.Match.Local, new Vector3(12, FixtureY, 12), 0);
            Place(first, new Vector3(0, FixtureY, -4), 0); Place(second, new Vector3(0, FixtureY, 5), 180);
            Physics.SyncTransforms();
            int commandsBefore = root.Network.CommandsReceived;
            Publish(new Stage { name = "move", first = first.Id, second = second.Id, startX = first.transform.position.x });
            yield return Wait(() => first.transform.position.x > 1.2f, 12, "Client movement reaches authoritative server actor");
            Check(root.Network.CommandsReceived > commandsBefore, "Server accepts movement command sequence");
            Publish(new Stage { name = "move_result", first = first.Id, second = second.Id, startX = 0 });
            yield return Both("move_result");

            Place(first, new Vector3(0, FixtureY, -4), 0); Place(second, new Vector3(0, FixtureY, 5), 180);
            second.MaxHP = second.HP = 5000; Physics.SyncTransforms();
            int fullAmmo = first.Weapon.Ammo;
            Publish(new Stage { name = "fire", first = first.Id, second = second.Id, magazine = fullAmmo, fullHP = second.HP });
            yield return Wait(() => first.Weapon.Ammo < fullAmmo && second.HP < 5000, 15, "Remote fire consumes server ammunition and damages target");
            Check(first.Weapon.Ammo < fullAmmo && second.HP < 5000, "Server resolves remote weapon ray and damage");
            Publish(new Stage { name = "combat_result", first = first.Id, second = second.Id, magazine = fullAmmo, fullHP = 5000 });
            yield return Both("combat_result");

            first.ApplyDamage(17, null, false, true);
            Publish(new Stage { name = "health", first = first.Id, second = second.Id, hp = first.HP });
            yield return Both("health");
            int beforePause = root.Match.Tick;
            root.SetPaused(true);
            Publish(new Stage { name = "pause", first = first.Id, second = second.Id, tick = beforePause });
            yield return Wait(() => root.Match.Tick > beforePause + 30, 8, "Simulation continues with host pause menu open");
            Check(root.Paused && !root.SimulationPaused && Time.timeScale == 1, "Host menu does not pause network simulation");
            yield return Both("pause"); root.SetPaused(false);

            int roundBeforeDeath = root.Match.RoundNumber;
            first.Kill(null, false);
            Publish(new Stage { name = "death", first = first.Id, second = second.Id });
            yield return Both("death");
            if (mode == "team") { root.Match.SendMessage("BeginRound"); StopBots(); root.Match.PreparationRemaining = 0; }
            yield return Wait(() => first.Alive, 12, "Server respawns dead actor");
            Check(first.Deaths == 1 && first.Alive, mode == "team" ? "New team round respawns remote player" : "Deathmatch timer respawns remote player");
            Publish(new Stage { name = "respawn", first = first.Id, second = second.Id, round = roundBeforeDeath });
            yield return Both("respawn");

            Publish(new Stage { name = "disconnect", first = first.Id, second = second.Id });
            yield return Wait(() => root.Network.HumanCount == 2 && !second.RemoteControlled, 15, "Disconnected client slot becomes a bot");
            Check(second.GetComponent<BotBrain>().enabled, "Disconnected slot resumes bot controller");
            yield return Wait(() => AckExists("Client1", "disconnect") && AckExists("Client2", "disconnect"), 12, "Clients observe disconnect and bot handover");
            Publish(new Stage { name = "shutdown" });
            root.Match.ReturnToMenu();
            yield return Wait(() => AckExists("Client1", "shutdown"), 15, "Remaining client returns to menu after host closes room");
            Check(!root.Network.IsActive && root.Match.Phase == MatchPhase.Menu, "Host stops room and returns to menu");
            Destroy(floor);
        }

        IEnumerator RejectionProbe(string code, int protocol, string expected, string label)
        {
            using (var probe = new NetworkTransport())
            {
                probe.Connect("127.0.0.1", port); float deadline = Time.realtimeSinceStartup + 10; bool rejected = false;
                while (!rejected && Time.realtimeSinceStartup < deadline)
                {
                    while (probe.TryPoll(out var evt))
                    {
                        if (evt.Kind == TransportEventKind.Connected)
                            probe.Send(0, JsonConvert.SerializeObject(new NetworkMessage { type = "hello", protocol = protocol,
                                content = root.Network.ContentHash, code = code, nickname = "RejectedProbe" }));
                        if (evt.Kind == TransportEventKind.Data)
                        {
                            var message = JsonConvert.DeserializeObject<NetworkMessage>(evt.Payload);
                            rejected = message != null && message.type == "reject" && (message.reason ?? "").Contains(expected);
                        }
                    }
                    yield return null;
                }
                Check(rejected, label);
            }
        }

        Stage ReadStage()
        {
            try { return JsonConvert.DeserializeObject<Stage>(File.ReadAllText(Path.Combine(directory, "stage.json"))); }
            catch (IOException) { return null; }
            catch (JsonException) { return null; }
        }
        void Idle() => root.Network.SubmitInput(new NetworkCommand { yaw = root.Match.Local ? root.Match.Local.transform.eulerAngles.y : 0 });

        IEnumerator Client()
        {
            Check(name == "Client1" || name == "Client2", "Client has a distinct test identity");
            yield return Wait(() => ReadStage() != null, 45, "Host publishes room readiness");
            root.Network.Join(name, "127.0.0.1", port, "NETTEST");
            yield return Wait(() => root.Network.IsClient && root.Network.SnapshotsReceived >= 2 && root.Match.Local, 35, "Client receives initial authoritative roster");
            float deadline = Time.realtimeSinceStartup + 150;
            while (Time.realtimeSinceStartup < deadline)
            {
                stage = ReadStage(); if (stage == null) { yield return null; continue; }
                Actor first = root.Match.Actors.Find(a => a.Id == stage.first), second = root.Match.Actors.Find(a => a.Id == stage.second);
                if (stage.name != "move" && stage.name != "fire") Idle();
                switch (stage.name)
                {
                    case "joined":
                        if (root.Network.HumanCount == 3 && root.Match.Actors.Count == stage.count && root.Network.SnapshotsReceived >= 3)
                            Ack(stage.name, "Client sees three humans and complete authoritative roster");
                        break;
                    case "move":
                        root.Network.SubmitInput(new NetworkCommand { moveX = name == "Client1" ? 1 : 0, yaw = 0 });
                        break;
                    case "move_result":
                        if (first && first.transform.position.x > stage.startX + .5f)
                            Ack(stage.name, "Authoritative movement is visible on this client");
                        break;
                    case "fire":
                        if (name == "Client1" && first && second)
                        {
                            Vector3 aim = second.transform.position + Vector3.up * 1.15f - first.EyePosition;
                            root.Network.SubmitInput(new NetworkCommand { fire = true, yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg,
                                pitch = -Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg });
                        }
                        else Idle();
                        break;
                    case "combat_result":
                        if (first && second && first.Weapon.Ammo < stage.magazine && second.HP < stage.fullHP)
                            Ack(stage.name, "Server ammunition and bullet damage replicate together");
                        break;
                    case "health":
                        if (first && first.HP <= stage.hp + .01f && first.HP > 0)
                            Ack(stage.name, "Authoritative health update is replicated");
                        break;
                    case "pause":
                        if (root.Match.Tick > stage.tick + 15) Ack(stage.name, "Client receives live simulation during host pause menu");
                        break;
                    case "death":
                        if (first && !first.Alive && first.Deaths == 1) Ack(stage.name, "Authoritative death and scoreboard replicate");
                        break;
                    case "respawn":
                        if (first && first.Alive && first.HP > 0 && (mode != "team" || root.Match.RoundNumber > stage.round))
                            Ack(stage.name, "Respawn revives the replica and local camera");
                        break;
                    case "disconnect":
                        if (name == "Client2")
                        {
                            root.Match.ReturnToMenu();
                            Check(!root.Network.IsActive && root.Match.Phase == MatchPhase.Menu, "Disconnect button path returns client to menu");
                            Ack(stage.name, "Second client leaves the room cleanly"); yield break;
                        }
                        if (root.Network.HumanCount == 2 && second && !second.RemoteControlled && !second.Human)
                            Ack(stage.name, "Remaining client sees departed human replaced by bot");
                        break;
                    case "shutdown":
                        if (!root.Network.IsActive && root.Match.Phase == MatchPhase.Menu)
                        {
                            Ack(stage.name, "Host shutdown returns remaining client to menu"); yield break;
                        }
                        break;
                }
                yield return null;
            }
            throw new TimeoutException("Client workflow timed out at " + stage?.name);
        }
    }
}
