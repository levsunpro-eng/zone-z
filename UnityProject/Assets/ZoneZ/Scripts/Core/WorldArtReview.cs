using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace ZoneZ
{
    // Opt-in visual and navigation acceptance. Launch with an explicit disposable profile.
    public sealed class WorldArtReview : MonoBehaviour
    {
        const float ObservationSeconds = 20;
        const int CaptureWidth = 1600, CaptureHeight = 900;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        readonly HashSet<string> uniqueErrors = new HashSet<string>();
        readonly List<object> captures = new List<object>();
        readonly List<float> frameTimes = new List<float>(), gpuTimes = new List<float>();
        readonly List<AnimalTrace> animals = new List<AnimalTrace>();
        readonly NavMeshPath connection = new NavMeshPath();
        readonly FrameTiming[] latestTiming = new FrameTiming[1];
        GameRoot root;
        Camera reviewCamera;
        AmbientZoo follow;
        Vector3 followOffset;
        string directory, profilePath;
        bool measuring;
        float started, observedSeconds;
        int navigationFrames;

        sealed class AnimalTrace
        {
            public AmbientZoo animal;
            public Vector3 initial, previous;
            public float distance, maxDisplacement, nextSample;
            public int movingFrames, navigationSamples;
            public readonly List<object> samples = new List<object>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-world-review") < 0) return;
            if (FindFirstObjectByType<WorldArtReview>()) return;
            new GameObject("World art review runner").AddComponent<WorldArtReview>();
        }

        static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-") ? args[index + 1] : null;
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            directory = Argument(args, "-zonez-report");
            profilePath = Argument(args, "-zonez-profile");
            if (string.IsNullOrEmpty(directory))
            {
                Debug.LogError("World art review requires -zonez-report and a disposable -zonez-profile.");
                Application.Quit(2); yield break;
            }
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += OnLog;
            var pending = new Stack<IEnumerator>(); pending.Push(Exercise());
            string failure = null;
            while (pending.Count > 0 && failure == null)
            {
                bool more = false; object next = null;
                try { more = pending.Peek().MoveNext(); if (more) next = pending.Peek().Current; }
                catch (Exception exception) { failure = exception.ToString(); }
                if (failure != null) break;
                if (!more) { pending.Pop(); continue; }
                if (next is IEnumerator nested) pending.Push(nested); else yield return next;
            }
            measuring = false; follow = null;
            Application.logMessageReceived -= OnLog;
            bool passed = failure == null && errors.Count == 0;
            var report = new
            {
                passed, checks, errors, failure, profile = profilePath,
                observationSeconds = observedSeconds, navigationFrames,
                captureMode = "Explicit URP gameplay-world requests, authored light and post-processing, no IMGUI HUD",
                width = CaptureWidth, height = CaptureHeight, captures,
                animals = animals.Select(trace => new
                {
                    name = trace.animal ? trace.animal.name : "destroyed",
                    species = trace.animal ? trace.animal.Kind.ToString() : "unknown",
                    distanceMeters = trace.distance, maximumDisplacementMeters = trace.maxDisplacement,
                    trace.movingFrames, trace.navigationSamples,
                    start = Coordinates(trace.initial), end = Coordinates(trace.previous), trace.samples
                }).ToArray(),
                scene = SceneInfo(), frameTimeMs = Stats(frameTimes), gpuFrameTimeMs = Stats(gpuTimes),
                gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                cpu = SystemInfo.processorType, unity = Application.unityVersion, utc = DateTime.UtcNow
            };
            File.WriteAllText(Path.Combine(directory, "world-review.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            Debug.Log(passed ? "ZONEZ_WORLD_REVIEW_OK" : "ZONEZ_WORLD_REVIEW_FAILED " + failure);
            if (reviewCamera) Destroy(reviewCamera.gameObject);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator Exercise()
        {
            Check(!string.IsNullOrEmpty(profilePath), "Explicit disposable profile supplied");
            while (!SplashScreen.isFinished) yield return null;
            root = GameRoot.Instance;
            Check(root && root.World && root.World.Navigation, "Authored world and gameplay NavMesh loaded");
            Check(string.IsNullOrEmpty(root.FatalError), "World startup has no fatal error");
            Check(root.Match && root.Match.Phase == MatchPhase.Menu, "Review preserves menu state and awards no match rewards");
            root.SetPaused(false);
            Application.runInBackground = true;
            var cameraObject = new GameObject("World review camera");
            cameraObject.transform.SetParent(transform, false);
            reviewCamera = cameraObject.AddComponent<Camera>(); reviewCamera.enabled = false;
            reviewCamera.nearClipPlane = .035f; reviewCamera.farClipPlane = 350;
            reviewCamera.clearFlags = CameraClearFlags.Skybox;
            reviewCamera.aspect = (float)CaptureWidth / CaptureHeight;
            reviewCamera.cullingMask = root.MenuCamera.cullingMask;
            WorldLook.ConfigureCamera(reviewCamera);
            Check(reviewCamera.allowHDR, "Review uses gameplay HDR and WorldLook camera configuration");

            foreach (var animal in FindObjectsByType<AmbientZoo>(FindObjectsSortMode.None).Where(a => a.IsGroundAnimal))
            {
                Check(animal.NavigationReady, animal.name + " starts on navigation ground");
                Check(animal.GetComponentsInChildren<Actor>(true).Length == 0, animal.name + " has no combat Actor");
                Check(animal.GetComponentsInChildren<Collider>(true).Length == 0, animal.name + " has no blocking or damage colliders");
                animals.Add(new AnimalTrace { animal = animal, initial = animal.transform.position, previous = animal.transform.position });
            }
            foreach (var species in new[] { AmbientZoo.Species.Squirrel, AmbientZoo.Species.Rat, AmbientZoo.Species.Turtle })
                Check(animals.Any(trace => trace.animal.Kind == species), species + " exists on public ground");
            started = Time.realtimeSinceStartup; measuring = true;
            SetView(new Vector3(-44, 30, -43), new Vector3(7, 1, 6), 62);
            yield return Capture("01-wide-overview");
            SetView(new Vector3(-22, 1.78f, -8), new Vector3(4, 1.45f, 4), 68);
            yield return Capture("02-central-plaza");
            SetView(new Vector3(-55, 1.72f, 6.3f), new Vector3(-3, 1.3f, 3.2f), 70);
            yield return Capture("03-public-route");
            SetView(new Vector3(-38, 1.55f, -.8f), new Vector3(-36, 1, 4), 53);
            yield return Capture("04-weathered-wall-material");
            foreach (var species in new[] { AmbientZoo.Species.Squirrel, AmbientZoo.Species.Rat, AmbientZoo.Species.Turtle })
            {
                FollowAnimal(animals.First(trace => trace.animal.Kind == species).animal);
                yield return Capture("05-" + species.ToString().ToLowerInvariant() + "-start");
            }
            follow = null;
            while (Time.realtimeSinceStartup - started < ObservationSeconds) yield return null;
            observedSeconds = Time.realtimeSinceStartup - started; measuring = false;
            Check(navigationFrames >= 120, "Ground roaming observed for 20 real seconds and at least 120 frames");
            Check(errors.Count == 0, "Every observed animal frame stays on the connected main NavMesh");
            foreach (var trace in animals)
            {
                float minimumTravel = trace.animal.Kind == AmbientZoo.Species.Turtle ? .35f : 1;
                Check(trace.distance > minimumTravel && trace.maxDisplacement > minimumTravel * .5f && trace.movingFrames > 20,
                    trace.animal.name + " actually walks through the public park (" + trace.distance.ToString("F2") + " m)");
            }
            foreach (var species in new[] { AmbientZoo.Species.Squirrel, AmbientZoo.Species.Rat, AmbientZoo.Species.Turtle })
            {
                FollowAnimal(animals.First(trace => trace.animal.Kind == species).animal);
                yield return Capture("06-" + species.ToString().ToLowerInvariant() + "-after-roaming");
            }
            follow = null;
        }

        void SetView(Vector3 position, Vector3 target, float fieldOfView)
        {
            follow = null; reviewCamera.fieldOfView = fieldOfView;
            reviewCamera.transform.position = position;
            reviewCamera.transform.LookAt(target);
        }

        void FollowAnimal(AmbientZoo animal)
        {
            follow = animal; reviewCamera.fieldOfView = 43;
            var target = animal.transform.position + Vector3.up * .19f;
            // Use the actual position and clear existing geometry instead of moving the animal for a staged image.
            followOffset = new Vector3(1.1f, .65f, 1.1f);
            for (int i = 0; i < 16; i++)
            {
                var candidate = Quaternion.Euler(0, 22.5f * i, 0) * new Vector3(1.1f, .65f, 1.1f);
                var cameraPosition = animal.transform.position + candidate;
                if (Physics.Linecast(cameraPosition, target, out var hit, ~0, QueryTriggerInteraction.Ignore)) continue;
                followOffset = candidate; break;
            }
            UpdateFollowCamera();
        }

        void UpdateFollowCamera()
        {
            if (!follow || !reviewCamera) return;
            reviewCamera.transform.position = follow.transform.position + followOffset;
            reviewCamera.transform.LookAt(follow.transform.position + Vector3.up * .19f);
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.18f);
            // Explicit render requests work even when a hidden/batch window has no end-of-frame event.
            yield return null;
            var target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            target.Create(); var previousTarget = RenderTexture.active; Texture2D pixels = null;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                Check(RenderPipeline.SupportsRenderRequest(reviewCamera, request), "URP render request available for " + name);
                RenderPipeline.SubmitRenderRequest(reviewCamera, request);
                RenderTexture.active = target;
                pixels = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0); pixels.Apply();
                var values = pixels.GetPixels32(); var brightness = new List<float>();
                for (int i = 0; i < values.Length; i += 97)
                    brightness.Add((values[i].r * .2126f + values[i].g * .7152f + values[i].b * .0722f) / 255);
                brightness.Sort();
                Check(brightness[brightness.Count - 1] - brightness[0] > .07f, "Scene image is populated: " + name);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
                captures.Add(new
                {
                    file = name + ".png", camera = Coordinates(reviewCamera.transform.position),
                    euler = Coordinates(reviewCamera.transform.eulerAngles), reviewCamera.fieldOfView,
                    followedAnimal = follow ? follow.name : null,
                    timeRealSeconds = Time.realtimeSinceStartup - started,
                    luminanceMean = brightness.Average(), luminanceP10 = brightness[brightness.Count / 10],
                    luminanceMedian = brightness[brightness.Count / 2], luminanceP90 = brightness[brightness.Count * 9 / 10]
                });
            }
            finally
            {
                RenderTexture.active = previousTarget; target.Release(); Destroy(target);
                if (pixels) Destroy(pixels);
            }
            yield return null;
        }

        void LateUpdate()
        {
            UpdateFollowCamera();
            if (!measuring || !root || !root.World) return;
            float elapsed = Time.realtimeSinceStartup - started;
            navigationFrames++; frameTimes.Add(Time.unscaledDeltaTime * 1000);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, latestTiming) > 0 && latestTiming[0].gpuFrameTime > 0)
                gpuTimes.Add((float)latestTiming[0].gpuFrameTime);
            foreach (var trace in animals)
            {
                if (!trace.animal) { Error("Observed fauna was destroyed during roaming."); continue; }
                var point = trace.animal.transform.position;
                float step = Vector3.Distance(point, trace.previous);
                if (step > .00001f) { trace.distance += step; trace.movingFrames++; }
                trace.maxDisplacement = Mathf.Max(trace.maxDisplacement, Vector3.Distance(trace.initial, point));
                if (step > .45f) Error(trace.animal.name + " teleported between observed frames.");
                trace.navigationSamples++;
                if (!NavMesh.SamplePosition(point, out var hit, .3f, NavMesh.AllAreas) || Vector3.Distance(point, hit.position) > .12f)
                    Error(trace.animal.name + " left walkable ground.");
                else
                {
                    if (hit.position.y < -.2f || hit.position.y > .8f) Error(trace.animal.name + " walked on an elevated NavMesh island.");
                    if (!NavMesh.CalculatePath(root.World.TeamASpawns[0], hit.position, NavMesh.AllAreas, connection) || connection.status != NavMeshPathStatus.PathComplete)
                        Error(trace.animal.name + " lost connection to the main public park NavMesh.");
                    if (NavMesh.SamplePosition(trace.previous, out var before, .3f, NavMesh.AllAreas) &&
                        NavMesh.Raycast(before.position, hit.position, out var boundary, NavMesh.AllAreas))
                        Error(trace.animal.name + " crossed a navigation boundary between observed frames.");
                }
                if (elapsed >= trace.nextSample)
                {
                    trace.samples.Add(new { seconds = elapsed, position = Coordinates(point), distanceMeters = trace.distance });
                    trace.nextSample = elapsed + .5f;
                }
                trace.previous = point;
            }
        }

        object SceneInfo()
        {
            if (!root || !root.World) return null;
            return new
            {
                root.World.name, renderers = root.World.GetComponentsInChildren<Renderer>().Length,
                sunIntensity = RenderSettings.sun ? RenderSettings.sun.intensity : 0,
                sunColor = RenderSettings.sun ? new[] { RenderSettings.sun.color.r, RenderSettings.sun.color.g, RenderSettings.sun.color.b } : null,
                RenderSettings.ambientIntensity, RenderSettings.fog, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
                gameplayPhase = root.Match ? root.Match.Phase.ToString() : "unavailable", simulationTimeScale = Time.timeScale
            };
        }

        static object Coordinates(Vector3 point) => new { x = point.x, y = point.y, z = point.z };
        static object Stats(List<float> values)
        {
            if (values.Count == 0) return new { samples = 0 };
            var sorted = values.OrderBy(value => value).ToArray();
            return new { samples = values.Count, mean = values.Average(), median = sorted[sorted.Length / 2], p99 = sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * .99f))] };
        }
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label); Debug.Log("WORLD REVIEW PASS " + label);
        }
        void Error(string error) { if (uniqueErrors.Add(error)) errors.Add(error); }
        void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) Error(condition + "\n" + stack);
        }
    }
}
