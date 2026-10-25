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
    /// <summary>Opt-in contact sheets exercising the same animation update as live actors.</summary>
    public sealed class RuntimeAnimationReview : MonoBehaviour
    {
        const int ReviewLayer = 30, FrameSize = 480, FrameCount = 8, Columns = 4;
        const float Step = 1f / 60f;
        readonly Vector3 origin = new Vector3(2500, 10, 2500);
        readonly List<string> checks = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly List<object> sequences = new List<object>();
        readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        string directory, profilePath, failure;
        Transform studio, stage;
        Camera reviewCamera;
        RenderTexture target;
        Mesh baked;

        sealed class Scenario
        {
            public string Name;
            public Vector3 Velocity;
            public float Warmup = .8f, Interval = .12f;
            public bool Crouch;
        }

        struct Motion
        {
            public Vector3 Velocity;
            public float Height, Yaw;
            public bool Grounded, Crouch;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-animation-review") < 0) return;
            var node = new GameObject("Animation review (opt-in)");
            DontDestroyOnLoad(node);
            node.AddComponent<RuntimeAnimationReview>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int report = Array.IndexOf(args, "-zonez-report");
            directory = report >= 0 && report + 1 < args.Length ? Path.GetFullPath(args[report + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../Diagnostics/Animation"));
            Application.logMessageReceived += OnLog;
            var pending = new Stack<IEnumerator>();
            pending.Push(Exercise());
            while (pending.Count > 0 && failure == null)
            {
                object next = null;
                bool more = false;
                try { more = pending.Peek().MoveNext(); if (more) next = pending.Peek().Current; }
                catch (Exception exception) { failure = exception.ToString(); }
                if (failure != null) break;
                if (!more) { pending.Pop(); continue; }
                if (next is IEnumerator nested) pending.Push(nested);
                else yield return next;
            }
            bool passed = failure == null && errors.Count == 0;
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "animation-review.json"), JsonConvert.SerializeObject(new {
                    passed, failure, checks, errors, profilePath, sequences,
                    unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName, utc = DateTime.UtcNow,
                    frameSize = FrameSize, frameCount = FrameCount, columns = Columns,
                    notes = "Contact sheets read left to right, then top to bottom. Every sequence uses a fresh imported gameplay actor, manually advanced at 60 Hz through ActorVisual.AdvanceAnimation. The stage moves before each animation update. Cameras follow horizontal travel at a fixed world angle and ground-relative height. PNGs use explicit RenderPipeline.StandardRequest; they are scene renders, not mockups. Geometry and render checks do not replace visual review of motion. Foot bone coordinates are joint origins, not sole contact points."
                }, Formatting.Indented));
            }
            catch (Exception exception) { passed = false; Debug.LogException(exception); }
            Application.logMessageReceived -= OnLog;
            Debug.Log(passed ? "ZONEZ_ANIMATION_REVIEW_OK" : "ZONEZ_ANIMATION_REVIEW_FAILED " + failure);
            if (reviewCamera) reviewCamera.targetTexture = null;
            if (target) target.Release();
            foreach (var resource in resources) if (resource) Destroy(resource);
            Application.Quit(passed ? 0 : 2);
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + trace);
        }

        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label);
        }

        IEnumerator Exercise()
        {
            Directory.CreateDirectory(directory);
            while (!SplashScreen.isFinished) yield return null;
            yield return null;
            var root = GameRoot.Instance;
            Require(root && string.IsNullOrEmpty(root.FatalError) && root.Store != null,
                "Game and catalog boot before animation review");
            var args = Environment.GetCommandLineArgs();
            int profile = Array.IndexOf(args, "-zonez-profile");
            Require(profile >= 0 && profile + 1 < args.Length,
                "Animation review was launched with an explicit isolated profile");
            profilePath = Path.GetFullPath(root.Store.FilePath);
            var playerDirectory = Path.GetFullPath(Application.persistentDataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Require(!profilePath.StartsWith(playerDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Review profile is outside the player's persistent data directory");
            foreach (var flag in new[] { "-zonez-smoke", "-zonez-soak", "-zonez-benchmark", "-zonez-art-review", "-zonez-net-test", "-zonez-lobby-test" })
                Require(Array.IndexOf(args, flag) < 0, "Animation review does not also run " + flag);
            root.SetPaused(false);
            root.enabled = false;
            if (root.UI) root.UI.enabled = false;
            if (root.Match) root.Match.enabled = false;
            if (root.World) root.World.gameObject.SetActive(false);
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            SetupStudio();
            var scenarios = new[] {
                new Scenario { Name = "01-walk-forward", Velocity = Vector3.forward * 2.5f },
                new Scenario { Name = "02-run-forward", Velocity = Vector3.forward * 6 },
                new Scenario { Name = "03-walk-backward", Velocity = Vector3.back * 3 },
                new Scenario { Name = "04-strafe-left", Velocity = Vector3.left * 3 },
                new Scenario { Name = "05-strafe-right", Velocity = Vector3.right * 3 },
                new Scenario { Name = "06-crouch-walk", Velocity = Vector3.forward * 1.5f, Crouch = true, Interval = .16f },
                new Scenario { Name = "07-start-stop", Warmup = 0, Interval = .24f },
                new Scenario { Name = "08-jump-land", Warmup = 0, Interval = .14f },
                new Scenario { Name = "09-turn-in-place", Warmup = 0, Interval = .16f },
                new Scenario { Name = "10-crouch-transition", Warmup = 0, Interval = .20f }
            };
            foreach (var scenario in scenarios)
                foreach (string view in new[] { "front", "side" })
                    yield return Sequence(scenario, view);
            Require(sequences.Count == scenarios.Length * 2, "All animation sequences have front and side contact sheets");
        }

        void SetupStudio()
        {
            Application.runInBackground = true;
            Time.timeScale = 1;
            studio = new GameObject("Animation studio").transform;
            resources.Add(studio.gameObject);
            studio.position = origin;
            stage = new GameObject("Moving actor stage").transform;
            stage.SetParent(studio, false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Animation review ground with IK collider";
            floor.transform.SetParent(studio, false);
            floor.transform.localPosition = new Vector3(0, -.10f, 0);
            floor.transform.localScale = new Vector3(80, .2f, 80);
            floor.layer = ReviewLayer;
            var material = WorldArt.NewMaterial("Animation review matte floor", new Color(.19f, .21f, .23f), 0, .1f);
            resources.Add(material);
            floor.GetComponent<Renderer>().sharedMaterial = material;
            Require(floor.GetComponent<BoxCollider>(), "Studio floor provides a physical ground plane for foot placement");
            // Ground grid gives a stable reference for planted feet while the camera follows travel.
            var gridMaterial = WorldArt.NewMaterial("Animation floor grid", new Color(.25f, .28f, .31f), 0, .1f);
            resources.Add(gridMaterial);
            for (int i = -20; i <= 20; i++)
            {
                CreateGridLine(new Vector3(i, .002f, 0), new Vector3(.008f, .002f, 40), gridMaterial);
                CreateGridLine(new Vector3(0, .002f, i), new Vector3(40, .002f, .008f), gridMaterial);
            }
            reviewCamera = new GameObject("Animation review camera").AddComponent<Camera>();
            resources.Add(reviewCamera.gameObject);
            reviewCamera.clearFlags = CameraClearFlags.SolidColor;
            reviewCamera.backgroundColor = new Color(.105f, .13f, .16f);
            reviewCamera.cullingMask = 1 << ReviewLayer;
            reviewCamera.nearClipPlane = .03f;
            reviewCamera.farClipPlane = 50;
            reviewCamera.allowHDR = false;
            reviewCamera.orthographic = true;
            reviewCamera.aspect = 1;
            // Retain a backbuffer camera so hidden players continue rendering frames.
            reviewCamera.enabled = true;
            target = new RenderTexture(FrameSize, FrameSize, 24, RenderTextureFormat.ARGB32) {
                name = "Animation review target", antiAliasing = 1
            };
            target.Create();
            resources.Add(target);
            baked = new Mesh { name = "Animation review temporary baked geometry" };
            resources.Add(baked);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .70f, .76f);
            RenderSettings.ambientEquatorColor = new Color(.39f, .43f, .47f);
            RenderSettings.ambientGroundColor = new Color(.24f, .26f, .28f);
            RenderSettings.ambientIntensity = 1;
            StudioLight("Animation key", new Vector3(37, -32, 0), new Color(1, .94f, .86f), 1.25f, true);
            StudioLight("Animation fill", new Vector3(18, 120, 0), new Color(.73f, .84f, 1), .65f, false);
            StudioLight("Animation rim", new Vector3(26, 195, 0), new Color(1, .97f, .91f), .8f, false);
            Physics.SyncTransforms();
        }

        void CreateGridLine(Vector3 position, Vector3 size, Material material)
        {
            var line = WorldArt.Box(studio, "Ground reference grid", position, size, material);
            line.layer = ReviewLayer;
            // WorldArt boxes may include primitives; the grid must not change the IK surface.
            foreach (var collider in line.GetComponents<Collider>()) { collider.enabled = false; Destroy(collider); }
        }

        void StudioLight(string name, Vector3 rotation, Color color, float intensity, bool shadows)
        {
            var node = new GameObject(name);
            node.transform.SetParent(studio, false);
            node.transform.rotation = Quaternion.Euler(rotation);
            var light = node.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << ReviewLayer;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        IEnumerator Sequence(Scenario scenario, string view)
        {
            stage.SetPositionAndRotation(origin, Quaternion.identity);
            var actor = ActorVisual.Create(stage, 0);
            actor.enabled = false;
            actor.SetAim(0);
            actor.SetDead(false);
            foreach (var child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ReviewLayer;
            Require(actor.UsesImportedModel, scenario.Name + "/" + view + " uses the imported gameplay operator");
            Require(actor.GetComponentsInChildren<Collider>(true).Length == 0,
                scenario.Name + "/" + view + " starts without visual colliders");
            var bones = actor.GetComponentsInChildren<Transform>(true).Where(x =>
                x.name == "Tarso.L" || x.name == "Tarso.R" || x.name == "Espinilla.L" || x.name == "Espinilla.R" ||
                x.name == "Muslo.L" || x.name == "Muslo.R").ToArray();
            Require(bones.Length == 6, scenario.Name + "/" + view + " exposes six leg joints");
            // Warm up at a constant travel speed so each sheet includes a full steady cycle.
            for (float elapsed = -scenario.Warmup; elapsed < -.0001f; elapsed += Step)
                Advance(actor, scenario, elapsed, Mathf.Min(Step, -elapsed));
            string name = scenario.Name + "-" + view;
            string frameDirectory = Path.Combine(directory, name);
            Directory.CreateDirectory(frameDirectory);
            var sheet = new Texture2D(FrameSize * Columns, FrameSize * (FrameCount / Columns), TextureFormat.RGB24, false);
            resources.Add(sheet);
            var frames = new List<object>();
            float time = 0;
            Motion motion = Sample(scenario, 0);
            for (int frame = 0; frame < FrameCount; frame++)
            {
                float end = frame * scenario.Interval;
                if (frame == 0) motion = Advance(actor, scenario, 0, 0);
                while (time + .0001f < end)
                {
                    float dt = Mathf.Min(Step, end - time);
                    time += dt;
                    motion = Advance(actor, scenario, time, dt);
                }
                SetCamera(view, scenario.Name == "08-jump-land");
                // ActorVisual is disabled: yielding for the render cannot change the sampled pose.
                yield return null;
                yield return new WaitForEndOfFrame();
                var frameInfo = GeometryInfo(actor, bones, name + "/" + frame, time, motion);
                var pixels = ReadFrame(name + "/" + frame);
                int column = frame % Columns, row = frame / Columns;
                sheet.SetPixels(column * FrameSize, (FrameCount / Columns - 1 - row) * FrameSize,
                    FrameSize, FrameSize, pixels.GetPixels());
                string file = frame.ToString("00") + ".png";
                File.WriteAllBytes(Path.Combine(frameDirectory, file), pixels.EncodeToPNG());
                Destroy(pixels);
                frames.Add(new { frame, file = name + "/" + file, data = frameInfo });
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), sheet.EncodeToPNG());
            resources.Remove(sheet);
            Destroy(sheet);
            Require(actor.GetComponentsInChildren<Collider>(true).Length == 0, name + " animation adds no gameplay colliders");
            sequences.Add(new { name, file = name + ".png", interval = scenario.Interval, warmup = scenario.Warmup,
                cameraView = view, frames });
            Destroy(actor.gameObject);
            yield return null;
        }

        Motion Advance(ActorVisual actor, Scenario scenario, float time, float dt)
        {
            Motion motion = Sample(scenario, time);
            Vector3 position = stage.position + new Vector3(motion.Velocity.x, 0, motion.Velocity.z) * dt;
            position.y = origin.y + motion.Height;
            stage.SetPositionAndRotation(position, Quaternion.Euler(0, motion.Yaw, 0));
            Physics.SyncTransforms();
            actor.AdvanceAnimation(motion.Velocity, motion.Grounded, motion.Crouch, dt);
            return motion;
        }

        static Motion Sample(Scenario scenario, float time)
        {
            var sample = new Motion { Velocity = scenario.Velocity, Grounded = true, Crouch = scenario.Crouch };
            if (scenario.Name == "07-start-stop")
            {
                float speed = time < .25f ? 0 : time < .55f ? (time - .25f) / .30f * 4
                    : time < .90f ? 4 : time < 1.15f ? (1.15f - time) / .25f * 4 : 0;
                sample.Velocity = Vector3.forward * Mathf.Max(0, speed);
            }
            else if (scenario.Name == "08-jump-land")
            {
                const float gravity = 23;
                float vertical = Mathf.Sqrt(2 * gravity * 1.05f);
                float flightTime = time - .20f;
                sample.Velocity = Vector3.forward * 2.5f;
                if (flightTime > 0 && flightTime < 2 * vertical / gravity)
                {
                    sample.Grounded = false;
                    sample.Height = vertical * flightTime - .5f * gravity * flightTime * flightTime;
                    sample.Velocity.y = vertical - gravity * flightTime;
                }
            }
            else if (scenario.Name == "09-turn-in-place")
                sample.Yaw = Mathf.SmoothStep(0, 110, Mathf.Clamp01((time - .1f) / .8f));
            else if (scenario.Name == "10-crouch-transition")
                sample.Crouch = time >= .2f && time < .9f;
            return sample;
        }

        void SetCamera(string view, bool jumping)
        {
            var focus = new Vector3(stage.position.x, origin.y + (jumping ? 1.40f : .96f), stage.position.z);
            Vector3 direction = view == "front" ? new Vector3(.08f, .045f, 1) : new Vector3(1, .045f, .04f);
            reviewCamera.orthographicSize = jumping ? 1.60f : 1.13f;
            reviewCamera.transform.position = focus + direction.normalized * 6;
            reviewCamera.transform.LookAt(focus, Vector3.up);
        }

        object GeometryInfo(ActorVisual actor, Transform[] bones, string label, float time, Motion motion)
        {
            bool hasBounds = false, finite = true;
            int vertices = 0, skinCount = 0, framedVertices = 0;
            var bounds = new Bounds();
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                skinCount++;
                baked.Clear();
                renderer.BakeMesh(baked, false);
                foreach (var vertex in baked.vertices)
                {
                    Vector3 worldPoint = renderer.transform.TransformPoint(vertex);
                    Vector3 point = worldPoint - stage.position;
                    vertices++;
                    if (!Finite(point)) { finite = false; continue; }
                    Vector3 viewport = reviewCamera.WorldToViewportPoint(worldPoint);
                    if (viewport.z > reviewCamera.nearClipPlane && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1)
                        framedVertices++;
                    if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!finite || !hasBounds || vertices == 0) errors.Add(label + ": non-finite or empty skinned geometry");
            if (framedVertices == 0) errors.Add(label + ": all subject vertices lie outside the camera view");
            if (hasBounds && (bounds.size.y < .55f || bounds.size.y > 2.75f || bounds.size.x > 3 || bounds.size.z > 3))
                errors.Add(label + ": body bounds indicate collapsed or exploded skin: " + bounds);
            foreach (var bone in bones)
                if (!Finite(bone.position) || !Finite(bone.localPosition)) errors.Add(label + ": non-finite joint " + bone.name);
            return new {
                time, velocity = V(motion.Velocity), grounded = motion.Grounded, crouching = motion.Crouch,
                stagePosition = V(stage.position - origin), yaw = motion.Yaw,
                skinnedRendererCount = skinCount, vertexCount = vertices, framedVertices, finiteVertices = finite,
                boundsCenter = V(bounds.center), boundsSize = V(bounds.size), lowestSurfaceAboveFloor = bounds.min.y + stage.position.y - origin.y,
                segments = new[] { ".L", ".R" }.Select(side => new {
                    side,
                    thighLength = Vector3.Distance(bones.First(b => b.name == "Muslo" + side).position, bones.First(b => b.name == "Espinilla" + side).position),
                    shinLength = Vector3.Distance(bones.First(b => b.name == "Espinilla" + side).position, bones.First(b => b.name == "Tarso" + side).position)
                }).ToArray(),
                bones = bones.Select(bone => new { name = bone.name,
                    localToStage = V(stage.InverseTransformPoint(bone.position)), worldFromStudio = V(bone.position - origin),
                    localRotation = new[] { bone.localRotation.x, bone.localRotation.y, bone.localRotation.z, bone.localRotation.w }
                }).ToArray()
            };
        }

        Texture2D ReadFrame(string label)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(reviewCamera, request))
                throw new InvalidOperationException("Active render pipeline cannot explicitly capture " + label);
            RenderPipeline.SubmitRenderRequest(reviewCamera, request);
            var pixels = new Texture2D(FrameSize, FrameSize, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0);
                pixels.Apply();
            }
            finally { RenderTexture.active = previous; }
            int darkest = 765, brightest = 0, pink = 0;
            foreach (var color in pixels.GetPixels32())
            {
                int brightness = color.r + color.g + color.b;
                darkest = Math.Min(darkest, brightness);
                brightest = Math.Max(brightest, brightness);
                if (color.r > 200 && color.b > 200 && color.g < 65) pink++;
            }
            if (brightest - darkest < 25) errors.Add(label + ": blank or uniform render");
            if (pink > FrameSize * FrameSize / 1000) errors.Add(label + ": potential missing-shader magenta pixels: " + pink);
            return pixels;
        }

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
    }
}
