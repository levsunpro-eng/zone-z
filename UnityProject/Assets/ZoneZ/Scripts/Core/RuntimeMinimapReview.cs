using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    // Uses the same contacts and atlas texture that GameUI draws in a live match.
    public sealed class RuntimeMinimapReview : MonoBehaviour
    {
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        readonly List<object> captures = new List<object>();
        GameRoot root;
        string directory;
        Actor player, enemy, ally;
        GameObject cover;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-minimap-review") >= 0)
                new GameObject("Minimap runtime acceptance").AddComponent<RuntimeMinimapReview>();
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        IEnumerator Start()
        {
            directory = Argument("-zonez-report");
            if (string.IsNullOrEmpty(directory)) { Application.Quit(2); yield break; }
            Directory.CreateDirectory(directory); Application.logMessageReceived += OnLog;
            var pending = new Stack<IEnumerator>(); pending.Push(Exercise()); string failure = null;
            while (pending.Count > 0 && failure == null)
            {
                bool more = false; object next = null;
                try { more = pending.Peek().MoveNext(); if (more) next = pending.Peek().Current; }
                catch (Exception exception) { failure = exception.ToString(); }
                if (failure != null) break;
                if (!more) { pending.Pop(); continue; }
                if (next is IEnumerator nested) pending.Push(nested); else yield return next;
            }
            Application.logMessageReceived -= OnLog;
            bool passed = failure == null && errors.Count == 0;
            File.WriteAllText(Path.Combine(directory, "minimap-review.json"), JsonConvert.SerializeObject(new
            {
                passed, checks, errors, failure, captures,
                captureMode = "Explicit scene render with actual live minimap texture overlaid at the HUD coordinates; other IMGUI HUD excluded",
                unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName, utc = DateTime.UtcNow
            }, Formatting.Indented));
            Debug.Log(passed ? "ZONEZ_MINIMAP_RUNTIME_OK" : "ZONEZ_MINIMAP_RUNTIME_FAILED " + failure);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator Exercise()
        {
            Check(!string.IsNullOrEmpty(Argument("-zonez-profile")), "Explicit disposable QA profile");
            while (!SplashScreen.isFinished) yield return null;
            root = GameRoot.Instance; Check(root && root.World && root.UI, "Boot loads minimap world and HUD");
            root.Match.StartMatch("team");
            player = root.Match.Local; enemy = root.Match.Actors.First(a => a.Team != player.Team);
            ally = root.Match.Actors.First(a => a != player && a.Team == player.Team);
            foreach (var actor in root.Match.Actors)
            {
                var bot = actor.GetComponent<BotBrain>(); if (bot) bot.enabled = false;
                var motor = actor.GetComponent<PlayerMotor>(); if (motor) motor.enabled = false;
                if (actor != player && actor != enemy && actor != ally) actor.gameObject.SetActive(false);
            }
            root.Match.PreparationRemaining = .001f; root.Match.SendMessage("FixedUpdate");
            Place(player, new Vector3(-58, .12f, 0), 90);
            Place(enemy, new Vector3(-51, .12f, 0), -90);
            Place(ally, new Vector3(-68, .12f, -9), 0);
            root.SetPaused(false); Physics.SyncTransforms(); yield return null; yield return null;
            Check(root.Match.Phase == MatchPhase.Playing, "Contacts checked during an actual team match");
            Check(root.UI.MinimapTexture && root.UI.MinimapTexture.width == 256 && root.UI.MinimapTexture.height == 224, "Static tactical plan generated from actual geometry");
            Check(Contact(player, MinimapMarkerKind.Self), "Local player has a direction marker");
            Check(Contact(ally, MinimapMarkerKind.Ally), "Ally behind the camera is present");
            Check(Contact(enemy, MinimapMarkerKind.Enemy), "Visible enemy has a red contact");
            yield return Capture("01-visible-enemy");

            cover = WorldArt.Box(root.World.transform, "Disposable minimap QA wall", new Vector3(-54.5f, 2, 0), new Vector3(.4f, 4, 4),
                WorldArt.SurfaceMat("Minimap fixture wall", new Color(.45f, .48f, .44f)), true, "concrete");
            Physics.SyncTransforms(); yield return null;
            Check(!Contact(enemy, MinimapMarkerKind.Enemy), "Enemy disappears immediately behind a solid wall");
            Check(Contact(ally, MinimapMarkerKind.Ally), "Covered ally remains present");
            player.Ability.Revealed[enemy] = enemy.transform.position;
            Check(!Contact(enemy, MinimapMarkerKind.Enemy), "Recon data cannot bypass minimap visibility");
            yield return Capture("02-enemy-behind-wall");
            cover.SetActive(false); Physics.SyncTransforms(); yield return null;
            Check(Contact(enemy, MinimapMarkerKind.Enemy), "Enemy reappears when actual view is clear");
            Place(player, player.transform.position, -90); Physics.SyncTransforms(); yield return null;
            Check(!Contact(enemy, MinimapMarkerKind.Enemy), "Looking away removes the enemy contact");
            Check(Contact(ally, MinimapMarkerKind.Ally), "Looking away preserves allied positions");
            yield return Capture("03-looking-away");
            Place(player, player.transform.position, 90); Physics.SyncTransforms(); yield return null;
            enemy.Alive = false;
            Check(!Contact(enemy, MinimapMarkerKind.Enemy), "Dead enemy leaves the minimap"); enemy.Alive = true;
            ally.gameObject.SetActive(false);
            Check(!Contact(ally, MinimapMarkerKind.Ally), "Disconnected or inactive ally leaves the minimap"); ally.gameObject.SetActive(true);
            root.Match.ModeId = "deathmatch";
            Check(!Contact(ally, MinimapMarkerKind.Ally), "Deathmatch has no allied markers");
            Check(!Contact(ally, MinimapMarkerKind.Enemy), "Same-team value cannot reveal a hidden deathmatch opponent");
            Check(Contact(enemy, MinimapMarkerKind.Enemy), "Deathmatch still displays a visible opponent");
            yield return Capture("04-deathmatch");
            root.Match.ModeId = "team";
            foreach (var size in new[] { new Vector2(1600, 900), new Vector2(1920, 1080), new Vector2(1280, 1024), new Vector2(2560, 1080) })
            {
                float scale = Mathf.Min(size.x / 1600, size.y / 900), width = size.x / scale, offset = (size.y - 900 * scale) / (2 * scale);
                var panel = GameUI.MinimapPanelRect(width, offset);
                Check(Mathf.Abs((width - panel.xMax) * scale - 28 * scale) < .01f && Mathf.Abs(panel.y * scale + offset * scale - 20 * scale) < .01f,
                    "Minimap anchored at screen top right in " + size.x + "x" + size.y);
            }
            Check((GameUI.WorldToMinimap(Vector3.zero) - Vector2.one * .5f).sqrMagnitude < .00001f, "Map origin is centered");
            Check(GameUI.WorldToMinimap(Vector3.forward * 50).y < .5f && GameUI.WorldToMinimap(Vector3.right * 50).x > .5f,
                "Map north is up and east is right");
        }

        bool Contact(Actor actor, MinimapMarkerKind kind)
        {
            root.UI.RefreshMinimap(true);
            return root.UI.MinimapMarkers.Any(marker => marker.Actor == actor && marker.Kind == kind);
        }
        static void Place(Actor actor, Vector3 position, float yaw)
        {
            if (actor.Motor) actor.Motor.enabled = false;
            actor.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            actor.Aim.localRotation = Quaternion.identity;
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.15f); yield return null;
            root.UI.RefreshMinimap(true); var atlas = root.UI.MinimapTexture;
            File.WriteAllBytes(Path.Combine(directory, name + "-map.png"), atlas.EncodeToPNG());
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            target.Create(); var previous = RenderTexture.active; Texture2D picture = null;
            try
            {
                var camera = player.ViewCamera;
                var request = new RenderPipeline.StandardRequest { destination = target };
                Check(RenderPipeline.SupportsRenderRequest(camera, request), "Render request available for " + name);
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
                picture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); picture.Apply();
                var pixels = picture.GetPixels32(); var panel = GameUI.MinimapPanelRect(1600);
                for (int y = (int)panel.y; y < (int)panel.yMax; y++) for (int x = (int)panel.x; x < (int)panel.xMax; x++)
                    pixels[(899 - y) * 1600 + x] = new Color32(12, 20, 23, 255);
                var map = atlas.GetPixels32();
                for (int y = 0; y < 196; y++) for (int x = 0; x < 224; x++)
                {
                    int u = Mathf.Clamp(x * atlas.width / 224, 0, atlas.width - 1), v = Mathf.Clamp(y * atlas.height / 196, 0, atlas.height - 1);
                    pixels[(900 - (int)panel.y - 30 - 196 + y) * 1600 + (int)panel.x + 8 + x] = map[v * atlas.width + u];
                }
                picture.SetPixels32(pixels); picture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), picture.EncodeToPNG());
                captures.Add(new { file = name + ".png", contacts = root.UI.MinimapMarkers.Select(marker => new { actor = marker.Actor.DisplayName, kind = marker.Kind.ToString() }).ToArray() });
            }
            finally { RenderTexture.active = previous; target.Release(); Destroy(target); if (picture) Destroy(picture); }
        }
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        void OnLog(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) errors.Add(condition + "\n" + stack); }
    }
}
