using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZoneZ
{
    /// <summary>Opt-in visual review of the complete gameplay arsenal, using real rendered models.</summary>
    public sealed class ArsenalArtReview : MonoBehaviour
    {
        const int Layer = 30, Width = 1600, Height = 1000;
        readonly Vector3 origin = new Vector3(2700, 15, 2700);
        readonly List<string> errors = new List<string>(), checks = new List<string>(), warnings = new List<string>();
        readonly List<ViewRecord> views = new List<ViewRecord>();
        readonly List<object> weapons = new List<object>();
        readonly List<object> equipment = new List<object>();
        readonly HashSet<string> capturedFamilies = new HashSet<string>();
        readonly List<string> familySamples = new List<string>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        GameRoot root;
        Camera reviewCamera;
        Transform studio, mount;
        RenderTexture target;
        string directory, profilePath, failure;

        sealed class ViewRecord
        {
            public string name, file, kind, weaponId, family;
            public float progress;
            public int darkest, brightest, magentaPixels;
            public object geometry;
        }

        sealed class Projection
        {
            public int vertices, inFront, inside;
            public float insideFraction;
            public float[] viewportMin, viewportMax;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-zonez-arsenal-review") < 0) return;
            var node = new GameObject("Complete arsenal art review (opt-in)");
            DontDestroyOnLoad(node);
            node.AddComponent<ArsenalArtReview>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-zonez-report");
            directory = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../Diagnostics/ArsenalArt"));
            Application.logMessageReceived += OnLog;
            var stack = new Stack<IEnumerator>();
            stack.Push(Exercise());
            while (stack.Count > 0 && failure == null)
            {
                object next = null;
                bool more = false;
                try { more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current; }
                catch (Exception exception) { failure = exception.ToString(); }
                if (failure != null) break;
                if (!more) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            bool passed = failure == null && errors.Count == 0;
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "arsenal-art-review.json"), JsonConvert.SerializeObject(new {
                    passed, failure, errors, warnings, checks, profilePath,
                    unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName, utc = DateTime.UtcNow,
                    width = Width, height = Height, weaponCount = weapons.Count, familySamples,
                    notes = "All PNGs are actual URP render requests. First-person camera and mount match gameplay; only full-model/gallery cameras fit the geometry. Gallery models are scaled to fit individual cells. Projection measurements use deformed vertices, but do not prove occlusion, a correct grip or animation quality. Pixel checks cannot replace visual review. Geometry budgets are not FPS measurements. No profile settings, purchases or progress are changed.",
                    weapons, equipment, views
                }, Formatting.Indented));
                WriteIndex(passed);
            }
            catch (Exception exception) { passed = false; Debug.LogException(exception); }
            Application.logMessageReceived -= OnLog;
            if (target) target.Release();
            foreach (var resource in owned) if (resource) Destroy(resource);
            Debug.Log(passed ? "ZONEZ_ARSENAL_ART_REVIEW_OK" : "ZONEZ_ARSENAL_ART_REVIEW_FAILED " + failure);
            Application.Quit(passed ? 0 : 2);
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace);
        }

        void Check(bool valid, string message)
        {
            if (valid) checks.Add(message);
            else errors.Add(message);
        }

        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }

        IEnumerator Exercise()
        {
            Directory.CreateDirectory(directory);
            foreach (string part in new[] { "fps", "third-person", "reload", "gallery", "actions" }) Directory.CreateDirectory(Path.Combine(directory, part));
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return null;
            root = GameRoot.Instance;
            Require(root && root.Store != null && string.IsNullOrEmpty(root.FatalError), "Game boot failed before arsenal review");
            profilePath = Path.GetFullPath(root.Store.FilePath);
            string playerDirectory = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Require(!profilePath.StartsWith(playerDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Arsenal review requires an isolated profile");
            var args = Environment.GetCommandLineArgs();
            Require(!new[] { "-zonez-art-review", "-zonez-smoke", "-zonez-soak", "-zonez-benchmark", "-zonez-net-test", "-zonez-lobby-test" }.Any(x => Array.IndexOf(args, x) >= 0), "Run arsenal review separately from other diagnostic runners");
            root.SetPaused(false); root.enabled = false;
            if (root.UI) root.UI.enabled = false;
            if (root.Match) root.Match.enabled = false;
            if (root.World) root.World.gameObject.SetActive(false);
            foreach (var existing in FindObjectsByType<Camera>(FindObjectsSortMode.None)) existing.enabled = false;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None)) volume.enabled = false;
            SetupStudio();
            var catalog = root.Catalog.Weapons.ToArray();
            Check(catalog.Length == 36, "Catalog contains all 36 weapons");
            var abilities = new[] {
                new WeaponSpec { Id = "underbarrel", Name = "Underbarrel launcher", Category = "ability", Slot = "primary" },
                new WeaponSpec { Id = "laser_burst", Name = "Laser burst", Category = "ability", Slot = "primary" },
                new WeaponSpec { Id = "rocket_launcher", Name = "Rocket launcher", Category = "ability", Slot = "primary" }
            };
            // An explicit list ensures unusual reload mechanics are represented even when categories overlap.
            var representatives = new HashSet<string> {
                "weapon_ak47", "weapon_glock", "weapon_elite", "weapon_revolver", "weapon_mp7", "weapon_p90", "weapon_bizon",
                "weapon_famas", "weapon_awp", "weapon_g3sg1", "weapon_nova", "weapon_mag7", "weapon_negev"
            };
            foreach (var spec in catalog.Concat(abilities))
            {
                string family = Family(spec), stem = spec.Id.Replace("weapon_", "");
                bool familyExample = capturedFamilies.Add(family) || representatives.Contains(spec.Id);
                var skin = new SkinPreset { weaponId = spec.Id, stickers = new int[4] };
                SetFirstPersonCamera();
                var first = Create(mount, spec, skin, true);
                if (!first) continue;
                yield return new WaitForSecondsRealtime(.4f);
                Validate(first, spec, true);
                var idle = Metrics(first.transform, first);
                ValidateVisibility(first, spec.Id + " idle");
                yield return Capture("fps/" + stem, spec.Name, "first-person", spec.Id, family, 0, first.transform);
                var animation = new List<object>();
                bool firearm = !first.Melee && !first.Electric && spec.Category != "ability";
                if (firearm)
                {
                    if (familyExample) familySamples.Add(spec.Id + " (" + family + ")");
                    Vector3 initialMagazine = first.Magazine ? first.Magazine.localPosition : Vector3.zero;
                    Quaternion initialMagazineRotation = first.Magazine ? first.Magazine.localRotation : Quaternion.identity;
                    Vector3 initialSupport = first.SupportHand ? first.SupportHand.localPosition : Vector3.zero;
                    float magazineMotion = 0, handMotion = 0;
                    foreach (float progress in new[] { .18f, .48f, .86f })
                    {
                        first.SetReload(progress, true);
                        yield return new WaitForSecondsRealtime(.06f);
                        ValidateTransforms(first.transform, spec.Id + " reload " + progress);
                        if (first.Magazine) magazineMotion = Mathf.Max(magazineMotion, Vector3.Distance(initialMagazine, first.Magazine.localPosition));
                        if (first.SupportHand) handMotion = Mathf.Max(handMotion, Vector3.Distance(initialSupport, first.SupportHand.localPosition));
                        animation.Add(new { progress, geometry = Metrics(first.transform, first) });
                        if (familyExample) yield return Capture("reload/" + stem + "-" + Mathf.RoundToInt(progress * 100).ToString("00"), spec.Name + " reload " + progress, "reload", spec.Id, family, progress, first.transform);
                    }
                    if (first.Magazine && !first.SingleShellReload) Check(magazineMotion > .008f, spec.Id + " moves its magazine/cylinder during reload");
                    if (spec.Id == "weapon_elite") Check(handMotion < .008f, spec.Id + " keeps its support hand on the left pistol during reload");
                    else Check(handMotion > .01f, spec.Id + " moves its support hand during reload");
                    first.SetReload(1, true);
                    yield return new WaitForSecondsRealtime(.05f);
                    if (first.Magazine)
                    {
                        Check(Vector3.Distance(first.Magazine.localPosition, initialMagazine) < .005f, spec.Id + " reseats its magazine");
                        Check(Quaternion.Angle(first.Magazine.localRotation, initialMagazineRotation) < 1, spec.Id + " restores magazine rotation");
                    }
                    first.SetReload(0);
                    yield return new WaitForSecondsRealtime(.05f);
                    if (first.SupportHand) Check(Vector3.Distance(first.SupportHand.localPosition, initialSupport) < .008f, spec.Id + " restores its support grip");
                }
                else
                {
                    first.Fire();
                    yield return null;
                    yield return Capture("actions/" + stem + "-attack", spec.Name + " attack", "action", spec.Id, family, 0, first.transform);
                    first.Inspect();
                    yield return new WaitForSecondsRealtime(.8f);
                    yield return Capture("actions/" + stem + "-inspect", spec.Name + " inspect", "action", spec.Id, family, 0, first.transform);
                }
                if (first.Suppressor)
                {
                    first.SetAlternate(false);
                    Check(!first.Suppressor.gameObject.activeSelf, spec.Id + " alternate removes suppressor");
                    first.SetAlternate(true);
                    Check(first.Suppressor.gameObject.activeSelf, spec.Id + " alternate restores suppressor");
                }
                Destroy(first.gameObject);
                yield return null;
                var third = Create(studio, spec, skin, false);
                if (!third) continue;
                yield return null;
                Validate(third, spec, false);
                Frame(third.transform, new Vector3(-1.7f, .43f, .65f), .10f);
                var world = Metrics(third.transform, third);
                yield return Capture("third-person/" + stem, spec.Name, "third-person", spec.Id, family, 0, third.transform);
                weapons.Add(new { id = spec.Id, name = spec.Name, category = spec.Category, family, firstPerson = idle, thirdPerson = world, animation });
                Destroy(third.gameObject);
                yield return null;
            }
            Check(weapons.Count == 39, "All 36 catalog weapons and 3 ability weapons were reviewed in first and third person");
            for (int start = 0; start < catalog.Length; start += 12)
                yield return Gallery(catalog.Skip(start).Take(12).ToArray(), start / 12 + 1);
            yield return Gallery(abilities, 4);
            yield return EquipmentGallery();
        }

        WeaponPresentation Create(Transform parent, WeaponSpec spec, SkinPreset skin, bool firstPerson)
        {
            try
            {
                var model = spec.Category == "ability" ? WeaponModelFactory.CreateAbility(parent, spec.Id, firstPerson)
                    : WeaponModelFactory.Create(parent, spec, skin, firstPerson);
                SetLayer(model.transform);
                return model;
            }
            catch (Exception exception) { errors.Add(spec.Id + " construction failed (firstPerson=" + firstPerson + "): " + exception); return null; }
        }

        static string Family(WeaponSpec spec)
        {
            string id = spec.Id;
            if (id.Contains("revolver")) return "revolver";
            if (id.Contains("elite")) return "dual-pistols";
            if (id.Contains("p90")) return "top-magazine";
            if (id.Contains("bizon")) return "helical-magazine";
            if (id.Contains("famas") || id.Contains("aug")) return "bullpup";
            if (spec.SingleShellReload) return "single-shell-shotgun";
            if (spec.Category == "machinegun") return "belt-fed";
            if (id.Contains("awp") || id.Contains("ssg08")) return "bolt-action";
            return spec.Category ?? spec.Slot ?? "other";
        }

        void Validate(WeaponPresentation model, WeaponSpec spec, bool firstPerson)
        {
            string label = spec.Id + (firstPerson ? " FP" : " world");
            var renderers = model.GetComponentsInChildren<Renderer>().Where(Enabled).ToArray();
            Check(model.FirstPerson == firstPerson, label + " has correct presentation mode");
            Check(model.Muzzle && model.Muzzle.IsChildOf(model.transform), label + " has a valid muzzle anchor");
            Check(renderers.Length > 0, label + " has enabled geometry");
            Check(model.GetComponentsInChildren<Collider>(true).Length == 0, label + " has no art colliders");
            if (firstPerson)
            {
                Check(model.TriggerHand && model.SupportHand, label + " has both hand anchors");
                Check(model.GetComponentsInChildren<SkinnedMeshRenderer>().Count(x => x.sharedMesh && x.sharedMesh.blendShapeCount > 0) >= 2, label + " has deformable anatomical hands");
            }
            else Check(!model.TriggerHand && !model.SupportHand, label + " does not include first-person arms");
            if (!model.Melee && !model.Electric && !model.SingleShellReload && spec.Category != "ability") Check(model.Magazine, label + " has a magazine or cylinder anchor");
            long triangles = renderers.Sum(Triangles);
            Check(triangles > 0 && triangles <= (firstPerson ? 35000 : 8000), label + " fits geometry budget (" + triangles + " triangles)");
            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                    Check(material && material.shader && material.shader.isSupported && !material.shader.name.Contains("InternalErrorShader"), label + "/" + renderer.name + " has a supported material");
                var mesh = MeshOf(renderer);
                if (!mesh) { errors.Add(label + "/" + renderer.name + " has no mesh"); continue; }
                Check(mesh.subMeshCount <= renderer.sharedMaterials.Length, label + "/" + renderer.name + " assigns all submesh materials");
                if (mesh.isReadable)
                {
                    Check(mesh.vertices.All(Finite), label + "/" + mesh.name + " contains finite vertices");
                    if (mesh.normals.Length != 0) Check(mesh.normals.Length == mesh.vertexCount && mesh.normals.All(Finite), label + "/" + mesh.name + " contains finite normals");
                }
            }
            ValidateTransforms(model.transform, label);
        }

        void ValidateTransforms(Transform parent, string label)
        {
            Check(parent.GetComponentsInChildren<Transform>(true).All(x => Finite(x.localPosition) && Finite(x.localScale) && Finite(x.localEulerAngles)), label + " contains finite transforms");
        }

        void ValidateVisibility(WeaponPresentation model, string label)
        {
            var body = Project(WorldVertices(model.transform, r => !Within(r.transform, model.SupportHand) && !Within(r.transform, model.TriggerHand)));
            Check(body.inside >= 12, label + " has visible weapon surface vertices");
            foreach (var hand in new[] { model.TriggerHand, model.SupportHand })
            {
                if (!hand) continue;
                var projection = Project(WorldVertices(hand));
                Check(projection.inside >= 12, label + "/" + hand.name + " has visible hand surface vertices");
                if (projection.insideFraction < .05f) warnings.Add(label + "/" + hand.name + " has less than 5% of its geometry inside the camera frustum");
            }
        }

        object Metrics(Transform subject, WeaponPresentation model = null)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>().Where(Enabled).ToArray();
            return new {
                triangles = renderers.Sum(Triangles), renderers = renderers.Length,
                skinnedRenderers = renderers.Count(x => x is SkinnedMeshRenderer),
                projection = Project(WorldVertices(subject)),
                body = model ? Project(WorldVertices(subject, r => !Within(r.transform, model.SupportHand) && !Within(r.transform, model.TriggerHand))) : null,
                triggerHand = model && model.TriggerHand ? Project(WorldVertices(model.TriggerHand)) : null,
                supportHand = model && model.SupportHand ? Project(WorldVertices(model.SupportHand)) : null,
                magazine = model && model.Magazine ? Project(WorldVertices(model.Magazine)) : null,
                muzzle = model && model.Muzzle ? V(model.transform.InverseTransformPoint(model.Muzzle.position)) : null,
                supportPosition = model && model.SupportHand ? V(model.SupportHand.localPosition) : null,
                magazinePosition = model && model.Magazine ? V(model.Magazine.localPosition) : null,
                meshes = renderers.Select(r => new {
                    node = r.name, mesh = MeshOf(r) ? MeshOf(r).name : null, triangles = Triangles(r),
                    type = r.GetType().Name,
                    materials = r.sharedMaterials.Select(m => m ? new {
                        name = m.name, shader = m.shader ? m.shader.name : null,
                        textures = new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }
                            .Where(m.HasProperty).Where(p => m.GetTexture(p)).Select(p => p + ": " + m.GetTexture(p).name).ToArray()
                    } : null).ToArray()
                }).ToArray()
            };
        }

        IEnumerator Gallery(WeaponSpec[] specs, int page)
        {
            var gallery = WorldArt.Group(studio, "Arsenal contact sheet " + page);
            int columns = Math.Min(4, specs.Length), rows = (specs.Length + columns - 1) / columns;
            for (int i = 0; i < specs.Length; i++)
            {
                var holder = WorldArt.Group(gallery, specs[i].Id + " display");
                holder.localRotation = Quaternion.Euler(8, -78, 0);
                var model = Create(holder, specs[i], new SkinPreset { weaponId = specs[i].Id, stickers = new int[4] }, false);
                if (!model) continue;
                var points = WorldVertices(model.transform);
                if (points.Count == 0) continue;
                var bounds = BoundsOf(points);
                float scale = Mathf.Min(1.65f / Mathf.Max(.01f, bounds.size.x), .85f / Mathf.Max(.01f, bounds.size.y));
                holder.localScale = Vector3.one * scale;
                Vector3 center = new Vector3((i % columns - (columns - 1) * .5f) * 2, ((rows - 1) * .5f - i / columns) * 1.45f + .12f, 0);
                holder.position += origin + center - BoundsOf(WorldVertices(model.transform)).center;
                var label = WorldArt.Label(gallery, specs[i].Id == "underbarrel" ? "Underbarrel" : specs[i].Name,
                    new Vector3(center.x, center.y - .57f, -.6f), .10f, new Color(.84f, .89f, .92f), new Vector3(0, 180, 0));
                label.name = "Gallery caption " + specs[i].Id;
            }
            SetLayer(gallery);
            reviewCamera.orthographic = true; reviewCamera.orthographicSize = Mathf.Max(rows * 1.45f * .5f + .1f, columns / reviewCamera.aspect + .1f); reviewCamera.nearClipPlane = .03f;
            reviewCamera.transform.SetPositionAndRotation(origin + Vector3.back * 8, Quaternion.identity);
            yield return null;
            FitGalleryCaptions(gallery, 1.82f);
            yield return Capture("gallery/arsenal-" + page.ToString("00"), "Arsenal contact sheet " + page, "contact-sheet", null, null, 0, gallery);
            Destroy(gallery.gameObject);
            yield return null;
            reviewCamera.orthographic = false;
        }

        IEnumerator EquipmentGallery()
        {
            var gallery = WorldArt.Group(studio, "Equipment contact sheet");
            var ids = new[] { "medkit", "protective_field", "haste", "fast_reload", "stabilizer", "vampirism", "frag_grenade", "emp_grenade", "rocket_launcher" };
            for (int i = 0; i < ids.Length; i++)
            {
                var holder = WorldArt.Group(gallery, ids[i] + " display");
                holder.localRotation = Quaternion.Euler(-13, -24, i == 8 ? 65 : 0);
                Transform model = i < 6 ? EquipmentModels.BuildLoot(holder, ids[i]) : EquipmentModels.BuildProjectile(holder, ids[i]);
                var renderers = model.GetComponentsInChildren<Renderer>().Where(Enabled).ToArray();
                Check(model.GetComponentsInChildren<Collider>(true).Length == 0, ids[i] + " equipment art adds no collision");
                Check(renderers.Length > 0 && renderers.Sum(Triangles) > 0, ids[i] + " equipment has rendered geometry");
                Check(renderers.All(r => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported && !m.shader.name.Contains("InternalErrorShader"))), ids[i] + " equipment has supported materials");
                ValidateTransforms(model, ids[i] + " equipment");
                var points = WorldVertices(model); Require(points.Count > 0, ids[i] + " equipment has no vertices");
                var bounds = BoundsOf(points);
                equipment.Add(new { id = ids[i], kind = i < 6 ? "loot" : "projectile", triangles = renderers.Sum(Triangles), renderers = renderers.Length, worldSize = V(bounds.size), meshes = renderers.Select(r => MeshOf(r) ? MeshOf(r).name : null).Distinct().ToArray() });
                holder.localScale = Vector3.one * Mathf.Min(1.65f / Mathf.Max(.01f, bounds.size.x), 1.02f / Mathf.Max(.01f, bounds.size.y));
                Vector3 center = new Vector3((i % 3 - 1) * 2.4f, (1 - i / 3) * 1.6f + .12f, 0);
                holder.position += origin + center - BoundsOf(WorldVertices(model)).center;
                WorldArt.Label(gallery, ids[i].Replace('_', ' '), new Vector3(center.x, center.y - .67f, -.8f), .11f, new Color(.84f, .89f, .92f), new Vector3(0, 180, 0));
            }
            SetLayer(gallery);
            reviewCamera.orthographic = true; reviewCamera.orthographicSize = 2.6f; reviewCamera.nearClipPlane = .03f;
            reviewCamera.transform.SetPositionAndRotation(origin + Vector3.back * 8, Quaternion.identity);
            yield return null;
            FitGalleryCaptions(gallery, 2.2f);
            yield return Capture("gallery/equipment", "Six loot models and three projectiles", "equipment", null, null, 0, gallery);
            Check(equipment.Count == 9, "All six loot models and three projectile types were reviewed");
            Destroy(gallery.gameObject); yield return null; reviewCamera.orthographic = false;
        }

        void FitGalleryCaptions(Transform gallery, float cellWidth)
        {
            // TextMesh characterSize is not the actual glyph height. Measure the generated
            // geometry after one frame, then fit each short caption to 28 output pixels.
            float worldPerPixel = reviewCamera.orthographicSize * 2 / Height;
            foreach (var label in gallery.GetComponentsInChildren<TextMesh>())
            {
                var bounds = label.GetComponent<Renderer>().bounds;
                Require(bounds.size.x > 0 && bounds.size.y > 0, label.name + " has no caption geometry");
                float scale = Mathf.Min(28 * worldPerPixel / bounds.size.y, cellWidth / bounds.size.x);
                label.transform.localScale *= scale;
                Check(bounds.size.y * scale / worldPerPixel >= 24, label.name + " remains at least 24 pixels tall inside its gallery cell");
            }
        }

        void SetupStudio()
        {
            Application.runInBackground = true; Time.timeScale = 1;
            studio = new GameObject("Arsenal isolated studio").transform; studio.position = origin;
            reviewCamera = new GameObject("Arsenal capture camera").AddComponent<Camera>();
            reviewCamera.backgroundColor = new Color(.12f, .15f, .18f); reviewCamera.clearFlags = CameraClearFlags.SolidColor;
            reviewCamera.cullingMask = 1 << Layer; reviewCamera.farClipPlane = 40; reviewCamera.allowHDR = false; reviewCamera.aspect = Width / (float)Height;
            var cameraData = reviewCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.volumeLayerMask = 0;
            reviewCamera.targetTexture = null; reviewCamera.enabled = true;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Arsenal art review", antiAliasing = 1 };
            target.Create(); owned.Add(target);
            mount = WorldArt.Group(reviewCamera.transform, "Unmodified gameplay weapon mount", new Vector3(.24f, -.27f, .32f));
            RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .70f, .76f); RenderSettings.ambientEquatorColor = new Color(.39f, .43f, .47f);
            RenderSettings.ambientGroundColor = new Color(.24f, .26f, .28f); RenderSettings.ambientIntensity = 1;
            AddLight("Neutral key", new Vector3(37, -32, 0), new Color(1, .94f, .86f), 1.25f);
            AddLight("Blue fill", new Vector3(18, 120, 0), new Color(.73f, .84f, 1), .65f);
            AddLight("Neutral rim", new Vector3(26, 195, 0), new Color(1, .97f, .91f), .8f);
        }

        static void AddLight(string name, Vector3 rotation, Color color, float intensity)
        {
            var light = new GameObject("Arsenal " + name).AddComponent<Light>();
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(rotation);
            light.color = color; light.intensity = intensity; light.cullingMask = 1 << Layer; light.shadows = LightShadows.None;
        }

        void SetFirstPersonCamera()
        {
            reviewCamera.orthographic = false; reviewCamera.fieldOfView = 60; reviewCamera.nearClipPlane = .02f;
            reviewCamera.transform.SetPositionAndRotation(origin + Vector3.up * 1.62f, Quaternion.identity);
        }

        void Frame(Transform subject, Vector3 direction, float margin)
        {
            var points = WorldVertices(subject); Require(points.Count > 0, subject.name + " has no vertices to frame");
            var bounds = BoundsOf(points);
            reviewCamera.orthographic = false; reviewCamera.fieldOfView = 35; reviewCamera.nearClipPlane = .03f;
            var rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up); var inverse = Quaternion.Inverse(rotation);
            float tanY = Mathf.Tan(reviewCamera.fieldOfView * .5f * Mathf.Deg2Rad), tanX = tanY * reviewCamera.aspect, distance = .1f;
            foreach (var point in points)
            {
                Vector3 p = inverse * (point - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(p.x) * (1 + margin) / tanX - p.z, Mathf.Abs(p.y) * (1 + margin) / tanY - p.z, reviewCamera.nearClipPlane + .05f - p.z);
            }
            reviewCamera.transform.SetPositionAndRotation(bounds.center + direction.normalized * distance, rotation);
        }

        IEnumerator Capture(string file, string name, string kind, string id, string family, float progress, Transform subject)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(reviewCamera, request), "Render pipeline does not support explicit capture");
            RenderPipeline.SubmitRenderRequest(reviewCamera, request);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active; RenderTexture.active = target;
            try { texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply(); }
            finally { RenderTexture.active = previous; }
            int darkest = 765, brightest = 0, magenta = 0;
            foreach (var color in texture.GetPixels32())
            {
                int brightness = color.r + color.g + color.b;
                darkest = Math.Min(darkest, brightness); brightest = Math.Max(brightest, brightness);
                if (color.r > 200 && color.b > 200 && color.g < 65) magenta++;
            }
            Check(brightest - darkest >= 25, file + " renders nonuniform pixels");
            Check(magenta < Width * Height / 1000, file + " contains no missing-shader magenta field");
            File.WriteAllBytes(Path.Combine(directory, file + ".png"), texture.EncodeToPNG()); Destroy(texture);
            views.Add(new ViewRecord { name = name, file = file + ".png", kind = kind, weaponId = id, family = family, progress = progress,
                darkest = darkest, brightest = brightest, magentaPixels = magenta, geometry = Metrics(subject, subject.GetComponent<WeaponPresentation>()) });
        }

        Projection Project(List<Vector3> points)
        {
            var result = new Projection { vertices = points.Count };
            Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            foreach (var point in points)
            {
                var p = reviewCamera.WorldToViewportPoint(point);
                if (p.z < reviewCamera.nearClipPlane || p.z > reviewCamera.farClipPlane) continue;
                result.inFront++; min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                if (p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1) result.inside++;
            }
            result.insideFraction = points.Count == 0 ? 0 : result.inside / (float)points.Count;
            if (result.inFront > 0) { result.viewportMin = new[] { min.x, min.y }; result.viewportMax = new[] { max.x, max.y }; }
            return result;
        }

        static List<Vector3> WorldVertices(Transform subject, Func<Renderer, bool> include = null)
        {
            var result = new List<Vector3>();
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>().Where(Enabled))
            {
                if (include != null && !include(renderer)) continue;
                Mesh baked = null, mesh = MeshOf(renderer);
                try
                {
                    if (renderer is SkinnedMeshRenderer skin && mesh) { baked = new Mesh(); skin.BakeMesh(baked, false); mesh = baked; }
                    if (mesh && mesh.isReadable) foreach (var vertex in mesh.vertices) result.Add(renderer.transform.TransformPoint(vertex));
                    else
                    {
                        var bounds = renderer.bounds;
                        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                            result.Add(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                    }
                }
                finally { if (baked) Destroy(baked); }
            }
            return result;
        }

        void WriteIndex(bool passed)
        {
            var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Zone Z arsenal review</title><style>body{margin:32px;background:#151b21;color:#e0e6eb;font:15px system-ui}h1{font-size:28px}h2{margin-top:32px}section{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px}figure{margin:0;background:#202930;padding:10px;border-radius:8px}img{width:100%;display:block}figcaption{padding-top:8px}a{color:#b9d9ff}p{max-width:900px;line-height:1.5}.status{color:");
            html.Append(passed ? "#a3dfb4" : "#ffab99").Append("}</style><h1>Zone Z / Arsenal art review</h1><p class=\"status\">").Append(passed ? "Technical checks passed" : "Technical checks failed");
            html.Append("</p><p>These are real Unity renders, not illustrations. First-person views retain gameplay framing. Contact-sheet models are scaled to fit their cells. Review the hands, grip, materials and reload poses visually; passing checks does not certify those qualities.</p><p><a href=\"arsenal-art-review.json\">Full JSON evidence</a></p>");
            foreach (string kind in new[] { "contact-sheet", "equipment", "first-person", "reload", "third-person", "action" })
            {
                var items = views.Where(x => x.kind == kind).ToArray();
                if (items.Length == 0) continue;
                html.Append("<h2>").Append(WebUtility.HtmlEncode(kind)).Append("</h2><section>");
                foreach (var item in items)
                    html.Append("<figure><a href=\"").Append(item.file).Append("\"><img loading=\"lazy\" src=\"").Append(item.file).Append("\" alt=\"").Append(WebUtility.HtmlEncode(item.name)).Append("\"></a><figcaption>").Append(WebUtility.HtmlEncode(item.name)).Append("</figcaption></figure>");
                html.Append("</section>");
            }
            html.Append("</html>"); File.WriteAllText(Path.Combine(directory, "index.html"), html.ToString());
        }

        static bool Enabled(Renderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy;
        static bool Within(Transform node, Transform parent) => parent && (node == parent || node.IsChildOf(parent));
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        static Bounds BoundsOf(List<Vector3> points) { var bounds = new Bounds(points[0], Vector3.zero); foreach (var point in points) bounds.Encapsulate(point); return bounds; }
        static long Triangles(Renderer renderer)
        {
            var mesh = MeshOf(renderer); if (!mesh) return 0;
            long count = 0; for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }
        static void SetLayer(Transform node) { foreach (var child in node.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer; }
    }
}
