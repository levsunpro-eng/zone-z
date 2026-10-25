using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneZ
{
    /// <summary>CC0 operator mesh with joint-based presentation; never creates gameplay colliders.</summary>
    internal sealed class OperatorRig
    {
        readonly Transform space, model, spine, neck, head;
        readonly Transform[] bones;
        readonly Quaternion[] restRotations;
        readonly Vector3[] restPositions;
        readonly Limb leftArm, rightArm, leftLeg, rightLeg;
        readonly Vector3 modelPosition;
        readonly Quaternion modelRotation;
        readonly Actor owner;
        readonly OperatorLocomotion locomotion;
        float pelvisDrop, breathingTime;
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        static GameObject prefab;

        sealed class Limb
        {
            public Transform Upper, Lower, End;
            public Vector3 RestEnd;
            public Quaternion RestEndRotation;
            public float UpperLength, LowerLength;
        }

        public static OperatorRig TryCreate(Transform parent, int team)
        {
            if (!prefab) prefab = Resources.Load<GameObject>("Art/Characters/Operator");
            if (!prefab) return null;
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = "CC0 tactical operator";
            var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in instance.GetComponentsInChildren<Transform>(true)) found[t.name] = t;
            foreach (var name in new[] { "Cabeza", "Pecho", "Cuello", "Brazo.L", "Antebrazo.L", "HuesoMuneca.L", "Brazo.R", "Antebrazo.R", "HuesoMuneca.R", "Muslo.L", "Espinilla.L", "Tarso.L", "Muslo.R", "Espinilla.R", "Tarso.R" })
                if (!found.ContainsKey(name))
                {
                    Debug.LogWarning("Operator asset lacks bone " + name + "; using procedural fallback.");
                    instance.SetActive(false);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(instance); else UnityEngine.Object.DestroyImmediate(instance);
                    return null;
                }
            return new OperatorRig(parent, instance, found, team);
        }

        OperatorRig(Transform parent, GameObject instance, Dictionary<string, Transform> found, int team)
        {
            space = parent; model = instance.transform; owner = parent.GetComponentInParent<Actor>();
            foreach (var animator in instance.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var source = renderer.sharedMaterials;
                for (int i = 0; i < source.Length; i++) source[i] = OperatorMaterials.Get(source[i] ? source[i].name : "Operator Fabric", team);
                renderer.sharedMaterials = source;
                renderer.receiveShadows = true; renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.quality = SkinQuality.Bone4;
                // Conservative bounds cover crouching, weapon reach, and the grounded death pose.
                renderer.localBounds = new Bounds(new Vector3(0, .9f, 0), new Vector3(3, 3, 3));
            }
            modelPosition = model.localPosition; modelRotation = model.localRotation;
            spine = found["Pecho"]; neck = found["Cuello"]; head = found["Cabeza"];
            var armA = MakeLimb(found["Brazo.L"], found["Antebrazo.L"], found["HuesoMuneca.L"]);
            var armB = MakeLimb(found["Brazo.R"], found["Antebrazo.R"], found["HuesoMuneca.R"]);
            if (space.InverseTransformPoint(armA.Upper.position).x < space.InverseTransformPoint(armB.Upper.position).x) { leftArm = armA; rightArm = armB; }
            else { leftArm = armB; rightArm = armA; }
            var legA = MakeLimb(found["Muslo.L"], found["Espinilla.L"], found["Tarso.L"]);
            var legB = MakeLimb(found["Muslo.R"], found["Espinilla.R"], found["Tarso.R"]);
            if (legA.RestEnd.x < legB.RestEnd.x) { leftLeg = legA; rightLeg = legB; } else { leftLeg = legB; rightLeg = legA; }
            locomotion = new OperatorLocomotion(leftLeg.RestEnd, rightLeg.RestEnd);
            bones = instance.GetComponentsInChildren<Transform>(true);
            restRotations = new Quaternion[bones.Length]; restPositions = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) { restRotations[i] = bones[i].localRotation; restPositions[i] = bones[i].localPosition; }
            // Stable public anchors keep existing users independent of the imported skeleton's naming.
            Head = WorldArt.Group(head, "Operator head anchor");
            Head.position = space.TransformPoint(new Vector3(0, 1.64f, 0));
            Chest = WorldArt.Group(spine, "Operator chest anchor");
            Chest.position = space.TransformPoint(new Vector3(0, 1.28f, 0));
            AddTeamMarkers(team);
            Pose(0, 0, 0, 0, 0);
        }

        Limb MakeLimb(Transform upper, Transform lower, Transform end)
        {
            return new Limb { Upper = upper, Lower = lower, End = end,
                RestEnd = space.InverseTransformPoint(end.position), RestEndRotation = Quaternion.Inverse(space.rotation) * end.rotation,
                UpperLength = Vector3.Distance(upper.position, lower.position), LowerLength = Vector3.Distance(lower.position, end.position) };
        }

        void AddTeamMarkers(int team)
        {
            var marker = OperatorMaterials.Get("Operator Team", team);
            // Small original cloth patches make teams readable without recoloring exposed skin or metal.
            foreach (var arm in new[] { leftArm, rightArm })
            {
                var patch = WorldArt.Box(space, "Team cloth shoulder patch", Vector3.zero, new Vector3(.012f, .065f, .064f), marker);
                float sign = arm == leftArm ? -1 : 1;
                patch.transform.position = arm.Upper.position + space.right * (sign * .066f) + space.forward * .045f;
                patch.transform.rotation = space.rotation;
                patch.transform.SetParent(arm.Upper, true);
            }
        }

        public void ResetMotion() { locomotion.Reset(); pelvisDrop = 0; }

        public void Animate(Vector3 worldVelocity, bool grounded, float crouch, float death, float pitch, float dt)
        {
            locomotion.Update(space, worldVelocity * (1 - death), grounded, crouch, dt);
            breathingTime += dt * 1.9f;
            RestoreBones();
            float travel = Mathf.Clamp01(locomotion.Speed / 2) * (1 - death);
            float phase = locomotion.Cycle * Mathf.PI * 2;
            float bob = -Mathf.Cos(phase * 2) * Mathf.Lerp(.009f, .021f, locomotion.Run) * travel * (1 - locomotion.Air);
            model.localPosition = modelPosition + new Vector3(Mathf.Sin(phase) * .012f * travel,
                -crouch * .53f - travel * .055f - locomotion.Run * .075f + bob - locomotion.Landing, -crouch * .055f);
            // Lower the pelvis enough to reach a support, rather than stretching the skeleton.
            float drop = Mathf.Max(RequiredDrop(leftLeg, locomotion.Left.Position), RequiredDrop(rightLeg, locomotion.Right.Position));
            pelvisDrop = Mathf.Lerp(pelvisDrop, Mathf.Min(.18f, drop), 1 - Mathf.Exp(-24 * dt));
            model.localPosition += Vector3.down * pelvisDrop;
            float lean = Mathf.Clamp(locomotion.LocalVelocity.z * 1.2f, -5, 7) * (1 - locomotion.Air);
            float bank = Mathf.Clamp(-locomotion.LocalVelocity.x * .6f, -4, 4);
            PoseTorso(pitch, crouch, lean, bank, Mathf.Sin(phase) * 2 * travel);
            spine.position += space.up * (Mathf.Sin(breathingTime) * .0025f * (1 - travel) * (1 - death));
            PoseContact(leftLeg, locomotion.Left);
            PoseContact(rightLeg, locomotion.Right);
            PoseArms(crouch);
            Fall(crouch, death);
        }

        void RestoreBones()
        {
            for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = restRotations[i]; bones[i].localPosition = restPositions[i]; }
        }

        // Stateless preview API used by the asset inspector and existing model validation.
        public void Pose(float speed, float gait, float crouch, float death, float pitch)
        {
            RestoreBones();
            float travel = Mathf.Clamp01(speed / 3.8f) * (1 - death);
            float bob = Mathf.Sin(gait * 2) * .012f * travel;
            model.localPosition = modelPosition + new Vector3(0, -crouch * .53f - travel * .07f + bob, -crouch * .055f);
            PoseTorso(pitch, crouch, travel * 3, 0, 0);
            PoseLeg(leftLeg, gait, travel, crouch);
            PoseLeg(rightLeg, gait + Mathf.PI, travel, crouch);
            PoseArms(crouch);
            Fall(crouch, death);
        }

        void PoseTorso(float pitch, float crouch, float lean, float bank, float twist)
        {
            // Upper-body pitch follows aim, while legs continue to carry the body.
            spine.rotation = Quaternion.AngleAxis(bank, space.forward) * Quaternion.AngleAxis(twist, space.up)
                * Quaternion.AngleAxis(pitch * .20f + crouch * 8 + lean, space.right) * spine.rotation;
            neck.rotation = Quaternion.AngleAxis(pitch * .16f, space.right) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(-twist, space.up) * Quaternion.AngleAxis(pitch * .34f - lean * .5f, space.right) * head.rotation;
        }

        void PoseContact(Limb leg, OperatorLocomotion.Foot foot)
        {
            Vector3 local = space.InverseTransformPoint(foot.Position);
            var pole = new Vector3(leg.RestEnd.x + Mathf.Sign(leg.RestEnd.x) * .035f, .48f, .85f);
            pole.x += Mathf.Clamp(local.x - leg.RestEnd.x, -.3f, .3f) * .35f;
            Solve(leg, foot.Position, space.TransformPoint(pole));
            leg.End.rotation = foot.Rotation * leg.RestEndRotation;
        }

        static float RequiredDrop(Limb leg, Vector3 target)
        {
            Vector3 delta = leg.Upper.position - target;
            float reach = (leg.UpperLength + leg.LowerLength) * .987f;
            float height = Mathf.Sqrt(Mathf.Max(.01f, reach * reach - delta.x * delta.x - delta.z * delta.z));
            return Mathf.Max(0, delta.y - height);
        }

        void PoseArms(float crouch)
        {
            Transform weapon = owner && owner.Weapon && owner.Weapon.Presentation ? owner.Weapon.Presentation.transform : null;
            Vector3 trigger = weapon ? weapon.TransformPoint(new Vector3(.018f, -.072f, -.045f)) : space.TransformPoint(new Vector3(.20f, 1.27f - crouch * .53f, .16f));
            Vector3 support = weapon ? weapon.TransformPoint(new Vector3(-.025f, -.026f, .20f)) : space.TransformPoint(new Vector3(.13f, 1.30f - crouch * .53f, .40f));
            if (owner && owner.Weapon && owner.Weapon.Reloading)
                support += space.TransformVector(new Vector3(-.04f, -.13f, -.08f)) * Mathf.Sin(owner.Weapon.ReloadProgress * Mathf.PI);
            Solve(rightArm, trigger, space.TransformPoint(new Vector3(.48f, 1.04f - crouch * .53f, -.04f)));
            Solve(leftArm, support, space.TransformPoint(new Vector3(-.34f, 1.00f - crouch * .53f, .17f)));
            // Finger grip is baked into the imported rest pose. The source finger
            // axes differ; rotating them all around local X twists the skin.

        }

        void Fall(float crouch, float death)
        {
            if (death > 0)
            {
                // Rotate about the pelvis rather than about the feet so the corpse settles onto the floor.
                Quaternion fall = Quaternion.Euler(-death * 84, 0, death * 5);
                var pelvisPivot = new Vector3(0, 1.0f - crouch * .53f, 0);
                model.localPosition += pelvisPivot - fall * pelvisPivot + Vector3.down * (.76f * death);
                model.localRotation = fall * modelRotation;
            }
        }

        void PoseLeg(Limb leg, float phase, float travel, float crouch)
        {
            float stride = Mathf.Cos(phase) * .22f * travel * Mathf.Lerp(1, .55f, crouch);
            float lift = Mathf.Max(0, Mathf.Sin(phase)) * .09f * travel;
            var target = leg.RestEnd + new Vector3(Mathf.Sign(leg.RestEnd.x) * (.026f + crouch * .04f), lift, stride + crouch * .10f);
            Solve(leg, space.TransformPoint(target), space.TransformPoint(new Vector3(leg.RestEnd.x, .5f, .8f)));
            leg.End.rotation = space.rotation * leg.RestEndRotation;
        }

        static void Solve(Limb limb, Vector3 target, Vector3 pole)
        {
            Vector3 start = limb.Upper.position, delta = target - start;
            float length = delta.magnitude;
            if (length < .0001f || limb.UpperLength < .0001f || limb.LowerLength < .0001f) return;
            Vector3 direction = delta / length;
            float reach = Mathf.Clamp(length, Mathf.Abs(limb.UpperLength - limb.LowerLength) + .001f, limb.UpperLength + limb.LowerLength - .001f);
            float along = (limb.UpperLength * limb.UpperLength - limb.LowerLength * limb.LowerLength + reach * reach) / (2 * reach);
            float height = Mathf.Sqrt(Mathf.Max(0, limb.UpperLength * limb.UpperLength - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(pole - start, direction).normalized;
            if (bend.sqrMagnitude < .001f) bend = Vector3.Cross(direction, Vector3.right).normalized;
            Vector3 elbow = start + direction * along + bend * height;
            limb.Upper.rotation = Quaternion.FromToRotation(limb.Lower.position - start, elbow - start) * limb.Upper.rotation;
            limb.Lower.rotation = Quaternion.FromToRotation(limb.End.position - limb.Lower.position, start + direction * reach - limb.Lower.position) * limb.Lower.rotation;
        }

    }
}
