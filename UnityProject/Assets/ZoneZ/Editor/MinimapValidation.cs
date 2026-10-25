#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZoneZ.Editor
{
    /// <summary>Exercises minimap sight with the same hit volumes and casts used during a match.</summary>
    public static class MinimapValidation
    {
        static readonly Vector3 Origin = new Vector3(2000, 2000, 2000);

        [MenuItem("Zone Z/Validate minimap visibility")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run minimap validation outside Play Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("Save or close the untitled scene before minimap validation. Open scenes have not been changed.");
            }
            var previous = SceneManager.GetActiveScene();
            Scene temporary = default;
            try
            {
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temporary);
                var holder = new GameObject("Minimap validation fixture").transform;
                holder.position = Origin;
                var match = Child(holder, "Match").AddComponent<MatchDirector>();
                match.ModeId = "team"; match.Phase = MatchPhase.Playing;
                var observer = Subject(holder, match, "Observer", 0, Vector3.zero);
                var enemy = Subject(holder, match, "Enemy", 1, Vector3.forward * 10);
                var ally = Subject(holder, match, "Ally", 0, Vector3.back * 10);
                match.Local = observer;
                var camera = Child(holder, "View camera").AddComponent<Camera>();
                camera.transform.localPosition = Vector3.up * 1.62f;
                camera.nearClipPlane = .035f; camera.farClipPlane = 80;
                camera.fieldOfView = 60; camera.aspect = 16f / 9f;
                Physics.SyncTransforms();

                Check(MinimapVisibility.CanShow(observer, enemy, camera), "visible enemy; observer colliders are ignored");
                Check(!MinimapVisibility.CanShow(observer, observer, camera), "self belongs to the separate local marker");
                Check(MinimapVisibility.CanShow(observer, ally, camera), "ally is shown outside the observer view");
                Check(MinimapVisibility.CanShow(observer, ally, null), "allies do not require a visibility camera");
                Check(!MinimapVisibility.CanShow(observer, ally, camera, true), "optional visible-only ally policy");
                Check(!MinimapVisibility.CanShow(observer, enemy, null), "enemies require the active view camera");

                foreach (string material in new[] { "concrete", "wood", "glass" })
                {
                    var wall = Solid(holder, material + " wall", new Vector3(0, 1.8f, 5), new Vector3(4, 4, .3f), material);
                    ally.transform.localPosition = new Vector3(1, 0, 10);
                    Physics.SyncTransforms();
                    Check(!MinimapVisibility.CanShow(observer, enemy, camera), material + " blocks minimap sight");
                    Check(MinimapVisibility.CanShow(observer, ally, camera), material + " does not remove an ally");
                    Check(!MinimapVisibility.CanShow(observer, ally, camera, true), material + " blocks the optional visible-only ally policy");
                    ally.transform.localPosition = Vector3.back * 10;
                    UnityEngine.Object.DestroyImmediate(wall);
                    Physics.SyncTransforms();
                    Check(MinimapVisibility.CanShow(observer, enemy, camera), "enemy returns immediately when " + material + " cover is removed");
                }

                enemy.transform.localPosition = Vector3.back * 10;
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "enemy behind the camera is hidden");
                enemy.transform.localPosition = new Vector3(20, 0, 10);
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "enemy outside camera FOV is hidden");
                enemy.transform.localPosition = Vector3.forward * 100;
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "enemy beyond the far clip is hidden");
                enemy.transform.localPosition = Vector3.forward * 10;
                Physics.SyncTransforms();
                camera.enabled = false;
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "disabled camera cannot spot an enemy");
                camera.enabled = true;

                enemy.Alive = false;
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "dead enemy is hidden");
                enemy.Alive = true;
                ally.Alive = false;
                Check(!MinimapVisibility.CanShow(observer, ally, camera), "dead ally is hidden");
                ally.Alive = true;
                enemy.gameObject.SetActive(false);
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "inactive enemy is hidden");
                enemy.gameObject.SetActive(true);
                observer.Alive = false;
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "a dead observer cannot spot");
                observer.Alive = true;
                Physics.SyncTransforms();

                ally.transform.localPosition = Vector3.forward * 5;
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "a nearer actor occludes the enemy");
                Check(MinimapVisibility.CanShow(observer, ally, camera, true), "visible ally satisfies the optional policy");
                ally.transform.localPosition = Vector3.back * 10;
                Physics.SyncTransforms();
                enemy.Team = observer.Team; match.ModeId = "deathmatch";
                Check(MinimapVisibility.CanShow(observer, enemy, camera), "same-team actor in deathmatch is still a visible enemy");
                Check(!MinimapVisibility.CanShow(observer, ally, camera), "deathmatch does not expose a same-team actor behind the camera");
                enemy.Team = 1; match.ModeId = "team";

                var lowWall = Solid(holder, "Head peek wall", new Vector3(0, .78f, 5), new Vector3(4, 1.56f, .3f), "concrete");
                Physics.SyncTransforms();
                Check(MinimapVisibility.CanShow(observer, enemy, camera), "standing head peek is visible");
                enemy.SetStance(true, 1.2f);
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "crouched head does not leak above cover");
                enemy.SetStance(false, 1.8f);
                UnityEngine.Object.DestroyImmediate(lowWall);
                var corner = Solid(holder, "Corner peek", new Vector3(-1, 1.5f, 5), new Vector3(2, 3, .3f), "concrete");
                Physics.SyncTransforms();
                Check(MinimapVisibility.CanShow(observer, enemy, camera), "a visible side of the head can peek around a corner");
                UnityEngine.Object.DestroyImmediate(corner);

                // The clear part is outside the viewport; the on-screen part is occluded.
                // Independent frustum and sight checks for different points would leak this enemy.
                camera.fieldOfView = 10; camera.aspect = 1;
                enemy.transform.localPosition = new Vector3(.93f, 0, 10);
                var edge = Solid(holder, "Frustum-edge cover", new Vector3(-9.55f, 1.5f, 5), new Vector3(20, 3, .1f), "concrete");
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "the same sample must be both on-screen and unobstructed");
                UnityEngine.Object.DestroyImmediate(edge);
                camera.fieldOfView = 60; camera.aspect = 16f / 9f;
                enemy.transform.localPosition = Vector3.forward * 10;

                var containing = Solid(holder, "Camera inside solid", new Vector3(0, 1.62f, 0), Vector3.one * .5f, "concrete");
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "solid containing the camera blocks sight");
                UnityEngine.Object.DestroyImmediate(containing);
                var revealWall = Solid(holder, "Recon cover", new Vector3(0, 1.8f, 5), new Vector3(4, 4, .3f), "wood");
                observer.Ability = observer.gameObject.AddComponent<AbilityController>();
                observer.Ability.Owner = observer; observer.Ability.EquippedId = "recon_pulse";
                observer.Ability.Revealed[enemy] = enemy.transform.position;
                Physics.SyncTransforms();
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "recon reveal does not expose an occluded enemy");
                UnityEngine.Object.DestroyImmediate(revealWall);
                Physics.SyncTransforms();
                Check(MinimapVisibility.CanShow(observer, enemy, camera), "uncovered enemy is visible again");
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                Check(!MinimapVisibility.CanShow(observer, enemy, camera), "enemy disappears immediately after looking away; no retained contact");

                Debug.Log("ZONEZ_MINIMAP_VALIDATION_OK: local view/FOV, concrete/wood/glass, camera inside solid, own/other actor colliders, allies, deathmatch, dead/inactive actors, crouch/head/corner peeks, same-sample frustum test, no recon or contact retention.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                Physics.SyncTransforms();
            }
        }

        static Actor Subject(Transform holder, MatchDirector match, string name, int team, Vector3 position)
        {
            var actor = Child(holder, name).AddComponent<Actor>();
            actor.Match = match; actor.Team = team; actor.Alive = true; actor.HP = 100;
            actor.transform.localPosition = position;
            actor.Aim = Child(actor.transform, "Aim").transform; actor.Aim.localPosition = Vector3.up * 1.62f;
            actor.Motor = actor.gameObject.AddComponent<CharacterController>();
            actor.Motor.height = 1.8f; actor.Motor.radius = .31f; actor.Motor.center = Vector3.up * .9f;
            var hitboxes = Child(actor.transform, "Hitboxes").transform;
            typeof(Actor).GetProperty("Hitboxes").SetValue(actor, hitboxes);
            var head = HitZone.AddSphere(hitboxes, actor, "head", Vector3.up * 1.64f, .175f).GetComponent<SphereCollider>();
            typeof(Actor).GetField("headCollider", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(actor, head);
            HitZone.AddBox(hitboxes, actor, "chest", Vector3.up * 1.28f, new Vector3(.46f, .43f, .3f));
            match.Actors.Add(actor);
            return actor;
        }

        static GameObject Solid(Transform holder, string name, Vector3 position, Vector3 size, string material)
        {
            var solid = Child(holder, name); solid.transform.localPosition = position;
            solid.AddComponent<BoxCollider>().size = size;
            solid.AddComponent<SurfaceTag>().MaterialId = material;
            return solid;
        }

        static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Minimap visibility: " + label);
        }
    }
}
#endif
