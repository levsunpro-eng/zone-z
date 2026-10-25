using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    [DefaultExecutionOrder(100)]
    public sealed class ActorVisual : MonoBehaviour
    {
        public Transform Head;
        public Transform Chest;
        Transform rig, hips, leftLeg, rightLeg, leftArm, rightArm;
        float movement, gait, crouchAmount, deathAmount, pitch;
        bool crouching, dead;
        OperatorRig importedRig;
        Actor owner;
        readonly RaycastHit[] groundHits = new RaycastHit[16];
        public bool UsesImportedModel => importedRig != null;
        readonly List<Object> ownedMeshes = new List<Object>();

        public static ActorVisual Create(Transform parent, int team)
        {
            var node = WorldArt.Group(parent, "Operator visual");
            var visual = node.gameObject.AddComponent<ActorVisual>(); visual.Build(team); return visual;
        }

        void Build(int team)
        {
            owner = GetComponentInParent<Actor>();
            importedRig = OperatorRig.TryCreate(transform, team);
            if (importedRig != null)
            {
                Head = importedRig.Head;
                Chest = importedRig.Chest;
                return;
            }
            var fabric = WorldArt.Mat(team == 0 ? "Team ranger blue" : "Team expedition ochre", team == 0 ? new Color(.16f, .31f, .39f) : new Color(.56f, .31f, .17f));
            var pants = WorldArt.Mat(team == 0 ? "Team blue trousers" : "Team ochre trousers", team == 0 ? new Color(.19f, .24f, .26f) : new Color(.33f, .30f, .23f));
            var armor = WorldArt.Mat("Operator armor", new Color(.105f, .12f, .105f), .06f, .15f);
            var rubber = WorldArt.Mat("Operator boots and gloves", new Color(.057f, .064f, .062f), .05f, .14f);
            var skin = WorldArt.Mat("Operator skin", new Color(.58f, .40f, .29f), 0, .28f);
            var visor = WorldArt.Mat("Operator goggles", new Color(.1f, .18f, .18f), .5f, .75f);
            var stripe = WorldArt.Mat(team == 0 ? "Blue team marker" : "Orange team marker", team == 0 ? new Color(.33f, .76f, .87f) : new Color(1, .62f, .19f));
            rig = WorldArt.Group(transform, "Articulated rig", new Vector3(0, .91f, 0));
            hips = WorldArt.Group(rig, "Hips");
            WorldArt.Sphere(hips, "Pelvis", Vector3.zero, new Vector3(.36f, .24f, .235f), pants);
            WorldArt.Box(hips, "Utility belt", new Vector3(0, .07f, -.005f), new Vector3(.37f, .058f, .24f), armor);
            WorldArt.Box(hips, "Belt buckle", new Vector3(0, .07f, .126f), new Vector3(.054f, .042f, .016f), stripe);
            Chest = WorldArt.Group(rig, "Chest", new Vector3(0, .35f, 0));
            WorldArt.Sphere(Chest, "Jacket torso", Vector3.zero, new Vector3(.46f, .52f, .28f), fabric);
            WorldArt.Box(Chest, "Armored vest front", new Vector3(0, .015f, .103f), new Vector3(.35f, .34f, .105f), armor);
            WorldArt.Box(Chest, "Backpack", new Vector3(0, .015f, -.177f), new Vector3(.31f, .36f, .13f), armor);
            for (int i = -1; i <= 1; i++) WorldArt.Box(Chest, "Magazine pouch", new Vector3(i * .102f, -.079f, .171f), new Vector3(.086f, .135f, .055f), pants);
            for (int side = -1; side <= 1; side += 2)
            {
                WorldArt.Box(Chest, "Shoulder strap", new Vector3(side * .135f, .178f, .026f), new Vector3(.067f, .071f, .28f), armor);
                WorldArt.Box(Chest, "Team shoulder stripe", new Vector3(side * .235f, .135f, .01f), new Vector3(.009f, .063f, .095f), stripe);
            }
            WorldArt.Cylinder(rig, "Neck", new Vector3(0, .60f, 0), .069f, .1f, skin);
            Head = WorldArt.Group(rig, "Head", new Vector3(0, .73f, .004f));
            WorldArt.Sphere(Head, "Face", new Vector3(0, 0, .015f), new Vector3(.205f, .247f, .216f), skin);
            WorldArt.Sphere(Head, "Helmet shell", new Vector3(0, .062f, -.016f), new Vector3(.235f, .19f, .24f), armor);
            WorldArt.Box(Head, "Helmet brim", new Vector3(0, .048f, .093f), new Vector3(.23f, .024f, .074f), armor);
            WorldArt.Box(Head, "Goggle band", new Vector3(0, .009f, .111f), new Vector3(.198f, .057f, .042f), rubber);
            WorldArt.Box(Head, "Goggle lens", new Vector3(0, .012f, .135f), new Vector3(.157f, .039f, .01f), visor);
            WorldArt.Box(Head, "Face scarf", new Vector3(0, -.069f, .07f), new Vector3(.174f, .087f, .105f), fabric);
            WorldArt.Box(Head, "Helmet recognition", new Vector3(0, .118f, -.008f), new Vector3(.037f, .007f, .105f), stripe);
            leftLeg = Leg(rig, -.105f, fabric, pants, rubber, armor);
            rightLeg = Leg(rig, .105f, fabric, pants, rubber, armor);
            leftArm = Arm(rig, -.257f, fabric, rubber, armor);
            rightArm = Arm(rig, .257f, fabric, rubber, armor);
            foreach (var bone in new[] { hips, Chest, Head, leftLeg, rightLeg, leftArm, rightArm }) WorldArt.CombineRigidParts(bone, ownedMeshes);
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.receiveShadows = true;
        }

        static Transform Leg(Transform parent, float x, Material cloth, Material pants, Material rubber, Material armor)
        {
            var leg = WorldArt.Group(parent, x < 0 ? "Left leg" : "Right leg", new Vector3(x, -.018f, 0));
            WorldArt.Shape(leg, "Thigh", PrimitiveType.Capsule, new Vector3(0, -.20f, 0), new Vector3(.177f, .22f, .18f), pants);
            WorldArt.Shape(leg, "Shin", PrimitiveType.Capsule, new Vector3(0, -.56f, .017f), new Vector3(.14f, .20f, .15f), pants);
            WorldArt.Box(leg, "Knee pad", new Vector3(0, -.40f, .091f), new Vector3(.138f, .131f, .061f), armor);
            WorldArt.Box(leg, "Boot", new Vector3(0, -.795f, .055f), new Vector3(.16f, .17f, .28f), rubber);
            WorldArt.Box(leg, "Boot sole", new Vector3(0, -.873f, .057f), new Vector3(.168f, .025f, .293f), armor);
            WorldArt.Box(leg, "Cargo pocket", new Vector3(Mathf.Sign(x) * .076f, -.20f, -.01f), new Vector3(.045f, .145f, .119f), cloth);
            return leg;
        }

        static Transform Arm(Transform parent, float x, Material cloth, Material glove, Material armor)
        {
            var arm = WorldArt.Group(parent, x < 0 ? "Left arm" : "Right arm", new Vector3(x, .48f, 0));
            WorldArt.Shape(arm, "Upper arm", PrimitiveType.Capsule, new Vector3(0, -.142f, .02f), new Vector3(.15f, .16f, .15f), cloth);
            WorldArt.Sphere(arm, "Elbow pad", new Vector3(0, -.262f, .041f), new Vector3(.15f, .115f, .146f), armor);
            WorldArt.Beam(arm, "Forearm", new Vector3(0, -.258f, .042f), new Vector3(-Mathf.Sign(x) * .075f, -.275f, .247f), .118f, cloth);
            WorldArt.Sphere(arm, "Glove", new Vector3(-Mathf.Sign(x) * .075f, -.269f, .26f), new Vector3(.103f, .10f, .125f), glove);
            return arm;
        }

        public void SetMotion(float speed, bool crouch) { movement = Mathf.Max(0, speed); crouching = crouch; }
        public void SetDead(bool value)
        {
            if (dead && !value) { importedRig?.ResetMotion(); crouchAmount = movement = gait = 0; }
            dead = value; if (!value) deathAmount = 0;
        }
        public void SetAim(float value) { pitch = Mathf.Clamp(value > 180 ? value - 360 : value, -70, 70); }

        void LateUpdate()
        {
            if ((rig == null && importedRig == null) || (GameRoot.Instance != null && GameRoot.Instance.SimulationPaused)) return;
            Vector3 velocity = owner ? owner.CurrentVelocity : transform.forward * movement;
            bool grounded = !owner || owner.Motor && owner.Motor.enabled && owner.Motor.isGrounded || NearGround(velocity);
            AdvanceAnimation(velocity, grounded, owner ? owner.IsCrouching : crouching, Time.deltaTime);
        }

        bool NearGround(Vector3 velocity)
        {
            if (velocity.y > .3f || velocity.y < -.7f) return false;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .15f, Vector3.down, groundHits, .32f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (groundHits[i].normal.y > .55f && !groundHits[i].collider.GetComponentInParent<Actor>()) return true;
            return false;
        }

        /// <summary>Shared presentation step for live actors and deterministic animation review.</summary>
        public void AdvanceAnimation(Vector3 worldVelocity, bool grounded, bool crouch, float deltaTime)
        {
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
            if (dt <= 0) return;
            movement = new Vector2(worldVelocity.x, worldVelocity.z).magnitude;
            crouching = crouch;
            crouchAmount = Mathf.MoveTowards(crouchAmount, crouching ? 1 : 0, dt * 7);
            deathAmount = Mathf.MoveTowards(deathAmount, dead ? 1 : 0, dt * 3.5f);
            gait += dt * movement * 2.7f;
            if (importedRig != null)
            {
                importedRig.Animate(worldVelocity, grounded, crouchAmount, deathAmount, pitch, dt);
                return;
            }
            float swing = Mathf.Sin(gait) * Mathf.Min(movement / 3f, 1) * 27 * (1 - deathAmount);
            float stanceScale = Mathf.Lerp(1, 2f / 3f, crouchAmount);
            rig.localScale = new Vector3(1, stanceScale, 1);
            rig.localPosition = new Vector3(0, Mathf.Lerp(.91f * stanceScale, .20f, deathAmount), 0);
            rig.localRotation = Quaternion.Euler(deathAmount * 86, 0, deathAmount * 7);
            leftLeg.localRotation = Quaternion.Euler(swing - crouchAmount * 10, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing - crouchAmount * 10, 0, 0);
            leftArm.localRotation = Quaternion.Euler(pitch * .45f - 11 - swing * .1f, 0, -5);
            rightArm.localRotation = Quaternion.Euler(pitch * .55f - 7 + swing * .1f, 0, 4);
            Head.localRotation = Quaternion.Euler(pitch * .65f, 0, 0);
            Chest.localRotation = Quaternion.Euler(crouchAmount * 5, 0, Mathf.Sin(gait) * Mathf.Min(movement, 3) * .45f);
        }

        void OnDestroy()
        {
            foreach (var mesh in ownedMeshes) if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        }
    }
}
