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
    /// <summary>Opt-in, rendered acceptance views of the same public model factories used by gameplay.</summary>
    public sealed class ModelArtReview : MonoBehaviour
    {
        const int ReviewLayer = 30;
        const int Width = 1600, Height = 1000;
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        readonly List<object> views = new List<object>();
        readonly List<UnityEngine.Object> ownedResources = new List<UnityEngine.Object>();
        readonly Vector3 origin = new Vector3(2500, 10, 2500);
        string directory, profilePath, failure;
        GameRoot root;
        Transform stage, mount;
        Camera reviewCamera;
        RenderTexture target;
        GameObject floor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-art-review") < 0) return;
            var node = new GameObject("Model art review (opt-in)");
            DontDestroyOnLoad(node);
            node.AddComponent<ModelArtReview>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int reportArgument = Array.IndexOf(args, "-zonez-report");
            directory = reportArgument >= 0 && reportArgument + 1 < args.Length
                ? Path.GetFullPath(args[reportArgument + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../Diagnostics/ModelArt"));
            Application.logMessageReceived += OnLog;
            var pending = new Stack<IEnumerator>();
            pending.Push(Exercise());
            // Drive nested enumerators here so a capture failure is included in the JSON report.
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
                File.WriteAllText(Path.Combine(directory, "model-art-review.json"), JsonConvert.SerializeObject(new {
                    passed, failure, checks, errors, profilePath,
                    unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName,
                    width = Width, height = Height, utc = DateTime.UtcNow,
                    notes = "PNG captures are an isolated studio camera rendering the gameplay factories. Renderer visibility is a frustum/bounds test, not an occlusion test. Triangle counts include enabled SkinnedMeshRenderer and MeshRenderer instances. Visual quality still requires human review.",
                    views
                }, Formatting.Indented));
            }
            catch (Exception exception) { passed = false; Debug.LogException(exception); }
            Application.logMessageReceived -= OnLog;
            Debug.Log(passed ? "ZONEZ_MODEL_ART_REVIEW_OK" : "ZONEZ_MODEL_ART_REVIEW_FAILED " + failure);
            if (reviewCamera) reviewCamera.targetTexture = null;
            if (target) target.Release();
            foreach (var resource in ownedResources) if (resource) Destroy(resource);
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
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return null;
            root = GameRoot.Instance;
            Require(root && string.IsNullOrEmpty(root.FatalError) && root.Store != null, "Game boot completed before art review");
            profilePath = Path.GetFullPath(root.Store.FilePath);
            string playerDirectory = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Require(!profilePath.StartsWith(playerDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Art review uses an isolated profile outside the player's persistent data directory");
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-smoke") < 0,
                "Art review is not combined with the separate runtime smoke runner");
            // All changes below are scene-only. No purchases, progression, skins or profile settings are changed.
            root.SetPaused(false);
            root.enabled = false;
            if (root.UI) root.UI.enabled = false;
            if (root.Match) root.Match.enabled = false;
            if (root.World) root.World.gameObject.SetActive(false);
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            SetupStudio();

            var akSpec = root.Catalog.Weapons.First(x => x.Id.Contains("ak47"));
            var skin = new SkinPreset { weaponId = akSpec.Id, stickers = new int[4] };
            var ak = WeaponModelFactory.Create(mount, akSpec, skin, true);
            SetLayer(ak.transform);
            Require(ak.Muzzle && ak.Magazine && ak.TriggerHand && ak.SupportHand, "AK has muzzle, magazine and both hand anchors");
            Require(ak.GetComponentsInChildren<Collider>(true).Length == 0, "AK view model has no colliders");
            SetFirstPersonCamera();
            yield return new WaitForSecondsRealtime(.6f);
            yield return Capture("01-ak-first-person-idle", ak.transform, 0);
            Vector3 magazineIdle = ak.Magazine.localPosition, supportIdle = ak.SupportHand.localPosition;
            foreach (float progress in new[] { .24f, .48f, .78f, .93f })
            {
                ak.SetReload(progress, true);
                yield return new WaitForSecondsRealtime(.08f);
                yield return Capture("02-ak-reload-" + Mathf.RoundToInt(progress * 100).ToString("00"), ak.transform, progress);
                if (Mathf.Approximately(progress, .48f))
                {
                    Require(Vector3.Distance(ak.Magazine.localPosition, magazineIdle) > .02f, "Reload extracts the actual magazine through SetReload");
                    Require(Vector3.Distance(ak.SupportHand.localPosition, supportIdle) > .02f, "Reload moves the actual support hand through SetReload");
                }
            }
            ak.SetReload(1, true);
            yield return new WaitForSecondsRealtime(.08f);
            Require(Vector3.Distance(ak.Magazine.localPosition, magazineIdle) < .005f, "Reload returns the magazine to its seated position");
            ak.SetReload(0);
            ak.Inspect();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Capture("03-ak-inspect", ak.transform, 0);
            Destroy(ak.gameObject);
            yield return null;

            // A side view makes the complete weapon silhouette and material work reviewable as well.
            var display = WeaponModelFactory.Create(stage, akSpec, skin, false);
            SetLayer(display.transform);
            yield return null;
            FrameSubject(display.transform, new Vector3(-1.7f, .65f, .8f), .12f);
            yield return Capture("04-ak-full-model", display.transform, 0);
            Destroy(display.gameObject);
            yield return null;

            var pistolSpec = root.Catalog.Weapons.First(x => x.Id.Contains("glock"));
            var pistol = WeaponModelFactory.Create(mount, pistolSpec, new SkinPreset { weaponId = pistolSpec.Id, stickers = new int[4] }, true);
            SetLayer(pistol.transform);
            SetFirstPersonCamera();
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("05-pistol-new-hands", pistol.transform, 0);
            Destroy(pistol.gameObject);
            yield return null;

            floor.SetActive(true);
            for (int team = 0; team < 2; team++)
            {
                var actor = ActorVisual.Create(stage, team);
                SetLayer(actor.transform);
                actor.SetDead(false); actor.SetAim(0); actor.SetMotion(0, false);
                Require(actor.Head && actor.Chest, "Team " + team + " actor retains head and chest gameplay anchors");
                yield return new WaitForSecondsRealtime(.35f);
                FrameSubject(actor.transform, new Vector3(-.65f, .12f, 1), .14f);
                yield return Capture("06-team-" + team + "-front", actor.transform, 0);
                FrameSubject(actor.transform, new Vector3(.65f, .12f, -1), .14f);
                yield return Capture("07-team-" + team + "-back", actor.transform, 0);
                actor.SetMotion(0, true);
                yield return new WaitForSecondsRealtime(.45f);
                FrameSubject(actor.transform, new Vector3(-.8f, .16f, 1), .16f);
                yield return Capture("08-team-" + team + "-crouch", actor.transform, 0);
                actor.SetMotion(3.2f, false);
                yield return new WaitForSecondsRealtime(.32f);
                FrameSubject(actor.transform, new Vector3(.7f, .12f, 1), .18f);
                yield return Capture("09-team-" + team + "-walk", actor.transform, 0);
                Destroy(actor.gameObject);
                yield return null;
            }
        }

        void SetupStudio()
        {
            Application.runInBackground = true;
            Time.timeScale = 1;
            stage = new GameObject("Art review studio").transform;
            stage.position = origin;
            var cameraNode = new GameObject("Art review camera");
            reviewCamera = cameraNode.AddComponent<Camera>();
            reviewCamera.clearFlags = CameraClearFlags.SolidColor;
            reviewCamera.backgroundColor = new Color(.12f, .15f, .18f);
            reviewCamera.cullingMask = 1 << ReviewLayer;
            reviewCamera.farClipPlane = 30;
            reviewCamera.allowHDR = false;
            reviewCamera.allowMSAA = true;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Model art review target", antiAliasing = 1 };
            target.Create();
            ownedResources.Add(target);
            // Keep a backbuffer camera alive: players with only offscreen cameras can skip
            // normal frame rendering, especially when the diagnostic window is hidden.
            reviewCamera.targetTexture = null;
            reviewCamera.enabled = true;
            reviewCamera.aspect = Width / (float)Height;
            mount = new GameObject("Gameplay first-person mount").transform;
            mount.SetParent(reviewCamera.transform, false);
            mount.localPosition = new Vector3(.24f, -.27f, .32f);
            mount.localRotation = Quaternion.identity;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .70f, .76f);
            RenderSettings.ambientEquatorColor = new Color(.39f, .43f, .47f);
            RenderSettings.ambientGroundColor = new Color(.24f, .26f, .28f);
            RenderSettings.ambientIntensity = 1;
            StudioLight("Large neutral key", new Vector3(37, -32, 0), new Color(1, .94f, .86f), 1.25f);
            StudioLight("Soft blue fill", new Vector3(18, 120, 0), new Color(.73f, .84f, 1), .65f);
            StudioLight("Back rim", new Vector3(26, 195, 0), new Color(1, .97f, .91f), .8f);
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Review floor";
            floor.transform.position = origin + Vector3.down * .025f;
            floor.transform.localScale = Vector3.one * .5f;
            var material = WorldArt.NewMaterial("Art review matte floor", new Color(.16f, .18f, .20f), 0, .1f);
            ownedResources.Add(material);
            floor.GetComponent<Renderer>().sharedMaterial = material;
            SetLayer(floor.transform);
            Destroy(floor.GetComponent<Collider>());
            floor.SetActive(false);
        }

        void StudioLight(string name, Vector3 rotation, Color color, float intensity)
        {
            var node = new GameObject(name);
            var light = node.AddComponent<Light>();
            node.transform.rotation = Quaternion.Euler(rotation);
            light.type = LightType.Directional; light.color = color; light.intensity = intensity;
            light.cullingMask = 1 << ReviewLayer;
            light.shadows = name.Contains("key") ? LightShadows.Soft : LightShadows.None;
        }

        void SetFirstPersonCamera()
        {
            reviewCamera.transform.SetPositionAndRotation(origin + Vector3.up * 1.62f, Quaternion.identity);
            reviewCamera.fieldOfView = 60;
            reviewCamera.nearClipPlane = .02f;
        }

        void FrameSubject(Transform subject, Vector3 direction, float margin)
        {
            // Runtime skinned renderer bounds can lag behind a pose or have a different
            // scale from its imported skeleton. Frame the actual deformed surface.
            var points = SubjectWorldVertices(subject);
            Require(points.Count > 0, subject.name + " has active mesh vertices to frame");
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            reviewCamera.fieldOfView = 35;
            reviewCamera.nearClipPlane = .03f;
            reviewCamera.aspect = Width / (float)Height;
            var rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
            var worldToView = Quaternion.Inverse(rotation);
            float tanY = Mathf.Tan(reviewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float tanX = tanY * reviewCamera.aspect;
            float distance = .1f;
            // Fit both axes with perspective depth instead of a loose bounding sphere.
            // This lets long weapons fill the landscape frame while retaining boots/head.
            foreach (var point in points)
            {
                Vector3 p = worldToView * (point - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(p.x) * (1 + margin) / tanX - p.z);
                distance = Mathf.Max(distance, Mathf.Abs(p.y) * (1 + margin) / tanY - p.z);
                distance = Mathf.Max(distance, reviewCamera.nearClipPlane + .05f - p.z);
            }
            reviewCamera.transform.position = bounds.center + direction.normalized * distance;
            reviewCamera.transform.rotation = rotation;
        }

        static List<Vector3> SubjectWorldVertices(Transform subject)
        {
            var points = new List<Vector3>();
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>().Where(IsEnabled))
            {
                Mesh baked = null;
                var mesh = RendererMesh(renderer);
                try
                {
                    if (renderer is SkinnedMeshRenderer skinned && mesh)
                    {
                        baked = new Mesh();
                        skinned.BakeMesh(baked, false);
                        mesh = baked;
                    }
                    if (mesh && mesh.isReadable && mesh.vertexCount > 0)
                    {
                        foreach (var vertex in mesh.vertices) points.Add(renderer.transform.TransformPoint(vertex));
                    }
                    else
                    {
                        var bounds = renderer.bounds;
                        for (int x = -1; x <= 1; x += 2)
                            for (int y = -1; y <= 1; y += 2)
                                for (int z = -1; z <= 1; z += 2)
                                    points.Add(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                    }
                }
                finally { if (baked) Destroy(baked); }
            }
            return points;
        }

        IEnumerator Capture(string name, Transform subject, float reloadProgress)
        {
            // Settle public animation updates, then explicitly submit a fresh SRP frame.
            // Reading an attached RenderTexture alone can return an untouched black buffer
            // when a hidden player has skipped automatic offscreen camera rendering.
            yield return null;
            yield return new WaitForEndOfFrame();
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(reviewCamera, request), "Active render pipeline supports explicit capture: " + name);
            RenderPipeline.SubmitRenderRequest(reviewCamera, request);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            try { pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); }
            finally { RenderTexture.active = previous; }
            var colors = pixels.GetPixels32();
            int pink = 0, darkest = 765, brightest = 0;
            foreach (var color in colors)
            {
                if (color.r > 200 && color.b > 200 && color.g < 65) pink++;
                int brightness = color.r + color.g + color.b;
                darkest = Math.Min(darkest, brightness); brightest = Math.Max(brightest, brightness);
            }
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            Destroy(pixels);
            var planes = GeometryUtility.CalculateFrustumPlanes(reviewCamera);
            var renderers = subject.GetComponentsInChildren<Renderer>(true);
            var enabled = renderers.Where(IsEnabled).ToArray();
            var visible = enabled.Where(x => GeometryUtility.TestPlanesAABB(planes, x.bounds)).ToArray();
            if (brightest - darkest < 25) errors.Add("Blank or uniform capture: " + name);
            if (pink > colors.Length / 1000) errors.Add("Potential missing-shader magenta pixels in " + name + ": " + pink);
            if (visible.Length == 0) errors.Add("No visible subject renderer in " + name);
            foreach (var renderer in enabled)
                foreach (var material in renderer.sharedMaterials)
                    if (!material || !material.shader || !material.shader.isSupported || material.shader.name.Contains("InternalErrorShader"))
                        errors.Add("Missing or unsupported shader on " + name + "/" + renderer.name);
            var presentation = subject.GetComponent<WeaponPresentation>();
            views.Add(new {
                name, file = name + ".png", reloadProgress, pinkPixels = pink,
                capture = "RenderPipeline.StandardRequest", brightness = new { darkest, brightest },
                rendererCount = renderers.Length, enabledRendererCount = enabled.Length, visibleRendererCount = visible.Length,
                skinnedRendererCount = enabled.Count(x => x is SkinnedMeshRenderer),
                triangles = enabled.Sum(TriangleCount), visibleTriangles = visible.Sum(TriangleCount),
                camera = new { position = V(reviewCamera.transform.position - origin), rotation = V(reviewCamera.transform.eulerAngles), fov = reviewCamera.fieldOfView },
                magazinePosition = presentation && presentation.Magazine ? V(presentation.Magazine.localPosition) : null,
                supportHandPosition = presentation && presentation.SupportHand ? V(presentation.SupportHand.localPosition) : null,
                assets = enabled.Select(renderer => new {
                    node = NodePath(subject, renderer.transform), type = renderer.GetType().Name,
                    mesh = RendererMesh(renderer) ? RendererMesh(renderer).name : null,
                    bones = renderer is SkinnedMeshRenderer skinned ? skinned.bones.Length : 0,
                    triangles = TriangleCount(renderer), visible = visible.Contains(renderer),
                    boundsCenter = V(renderer.bounds.center - origin), boundsSize = V(renderer.bounds.size),
                    materials = renderer.sharedMaterials.Select(MaterialInfo).ToArray()
                }).ToArray()
            });
        }

        static object MaterialInfo(Material material)
        {
            if (!material) return new { missing = true };
            return new {
                name = material.name, shader = material.shader ? material.shader.name : null,
                supported = material.shader && material.shader.isSupported,
                textures = new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }
                    .Where(material.HasProperty).Select(property => new { property, texture = material.GetTexture(property) })
                    .Where(x => x.texture).Select(x => new { x.property, name = x.texture.name, width = x.texture.width, height = x.texture.height }).ToArray()
            };
        }

        static bool IsEnabled(Renderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy;
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static Mesh RendererMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        static long TriangleCount(Renderer renderer)
        {
            var mesh = RendererMesh(renderer);
            if (!mesh) return 0;
            long triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(i) / 3;
            return triangles;
        }

        static string NodePath(Transform root, Transform node)
        {
            string path = node.name;
            while (node != root && node.parent) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }

        static void SetLayer(Transform node)
        {
            foreach (var child in node.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ReviewLayer;
        }
    }
}
