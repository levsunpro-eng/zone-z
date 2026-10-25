#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZoneZ.Editor
{
    public static class OperatorValidation
    {
        [MenuItem("Zone Z/Validate imported operator")]
        public static void Run()
        {
            var holder = new GameObject("Imported operator validation");
            Mesh baked = null;
            try
            {
                var actor = ActorVisual.Create(holder.transform, 0);
                Require(actor.UsesImportedModel, "ActorVisual used the procedural fallback instead of the CC0 operator.");
                Require(actor.Head && actor.Chest, "Imported operator is missing its public Head/Chest anchors.");
                Require(actor.GetComponentsInChildren<Collider>(true).Length == 0, "Imported art added a gameplay collider.");
                var renderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>();
                Require(renderers.Length == 1, "Expected one imported skinned body renderer.");
                var renderer = renderers[0];
                Require(renderer.sharedMesh && renderer.bones.Length > 0, "Imported body has no mesh or skeleton.");
                int triangles = renderer.sharedMesh.triangles.Length / 3;
                Require(triangles == 13150, "Imported operator mesh does not match the verified CC0 export (13,150 tris).");
                Require(renderer.sharedMaterials.Length == 6, "Imported operator needs six distinct surface materials.");
                Require(renderer.sharedMaterials.All(m => m && m.shader && m.shader.name != "Hidden/InternalErrorShader"), "Imported operator has a missing material/shader.");
                Require(renderer.sharedMesh.boneWeights.All(w => w.weight0 + w.weight1 + w.weight2 + w.weight3 > .99f), "Imported operator has a vertex without normalized skin weights.");
                var imported = typeof(ActorVisual).GetField("importedRig", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actor);
                var pose = imported.GetType().GetMethod("Pose", BindingFlags.Public | BindingFlags.Instance);
                baked = new Mesh();
                Bounds standing = default, crouching = default;
                foreach (var sample in new[]
                {
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 4f, 1.2f, 0f, 0f, 18f },
                    new[] { 0f, 0f, 1f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f }
                })
                {
                    pose.Invoke(imported, sample.Cast<object>().ToArray());
                    renderer.BakeMesh(baked);
                    var vertices = baked.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray();
                    Require(vertices.All(v => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z)), "A pose produced non-finite skinned geometry.");
                    var bounds = new Bounds(vertices[0], Vector3.zero);
                    foreach (var vertex in vertices) bounds.Encapsulate(vertex);
                    Require(bounds.size.magnitude < 5, "An operator pose exploded outside the humanoid bounds.");
                    if (sample[0] == 0 && sample[2] == 0 && sample[3] == 0) standing = bounds;
                    if (sample[2] == 1) crouching = bounds;
                }
                Require(standing.size.y > 1.6f && standing.size.y < 1.9f, "Standing operator is not at the expected 1.8 m scale.");
                Require(crouching.max.y < standing.max.y - .30f, "Crouch pose did not lower the imported body.");
                LocomotionValidation.Run();
                Debug.Log($"ZONEZ_OPERATOR_VALIDATION_OK: imported=true, triangles={triangles}, skinnedRenderers=1, materialSlots=6, bones={renderer.bones.Length}, standingBounds={standing}, crouchingBounds={crouching}; standing/walk/crouch/death skinning checked.");
            }
            finally
            {
                if (baked) UnityEngine.Object.DestroyImmediate(baked);
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
