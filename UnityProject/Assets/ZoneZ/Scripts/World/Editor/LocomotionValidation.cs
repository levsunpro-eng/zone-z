#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZoneZ.Editor
{
    /// <summary>Exercises the same time-stepped animation as gameplay against actual imported bones.</summary>
    public static class LocomotionValidation
    {
        const float FloorY = 1000f;
        const float Dt = 1f / 60f;
        static readonly Vector3 Origin = new Vector3(0, FloorY, 0);

        sealed class Leg
        {
            public Transform Hip, Knee, Ankle;
            public float ThighLength, ShinLength;
            public Vector3 StandingAnkle;
            public float KneeAngle => Vector3.Angle(Hip.position - Knee.position, Ankle.position - Knee.position);
        }

        sealed class Subject : IDisposable
        {
            public readonly GameObject Holder;
            public readonly ActorVisual Visual;
            public readonly Leg[] Legs;
            readonly Transform[] transforms;
            readonly Vector3[] scales;
            readonly SkinnedMeshRenderer renderer;
            readonly Mesh baked = new Mesh();
            readonly ContactProbe[] contacts;

            sealed class ContactProbe
            {
                public Leg Leg;
                public object State;
                public FieldInfo Position, Swinging;
            }

            public Subject(Vector3 origin)
            {
                Holder = new GameObject("Locomotion validation actor");
                Holder.transform.position = origin;
                Visual = ActorVisual.Create(Holder.transform, 0);
                Visual.enabled = false;
                Require(Visual.UsesImportedModel, "locomotion validation requires the imported operator");
                transforms = Visual.GetComponentsInChildren<Transform>(true);
                scales = transforms.Select(t => t.localScale).ToArray();
                renderer = Visual.GetComponentInChildren<SkinnedMeshRenderer>();
                Legs = new[] { MakeLeg("L"), MakeLeg("R") };
                foreach (var leg in Legs) leg.StandingAnkle = Local(leg.Ankle);
                // The planned world contact is the contract the actual IK bone must reach.
                // Reading it avoids mistaking a clamped, skating ankle for a valid fixed-length leg.
                var rig = typeof(ActorVisual).GetField("importedRig", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Visual);
                var planner = rig.GetType().GetField("locomotion", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
                contacts = new[] { "Left", "Right" }.Select(name =>
                {
                    var state = planner.GetType().GetField(name).GetValue(planner);
                    var type = state.GetType();
                    var rest = (Vector3)type.GetField("Rest").GetValue(state);
                    return new ContactProbe
                    {
                        Leg = Legs.OrderBy(l => Mathf.Abs(l.StandingAnkle.x - rest.x)).First(),
                        State = state, Position = type.GetField("Position"), Swinging = type.GetField("Swinging")
                    };
                }).ToArray();
            }

            Leg MakeLeg(string suffix)
            {
                Transform Find(string name) => transforms.Single(t => t.name == name + "." + suffix);
                var leg = new Leg { Hip = Find("Muslo"), Knee = Find("Espinilla"), Ankle = Find("Tarso") };
                leg.ThighLength = Vector3.Distance(leg.Hip.position, leg.Knee.position);
                leg.ShinLength = Vector3.Distance(leg.Knee.position, leg.Ankle.position);
                return leg;
            }

            public Vector3 Local(Transform bone) => Holder.transform.InverseTransformPoint(bone.position);

            public void Step(Vector3 velocity, bool grounded, bool crouch, float dt, bool translate = true)
            {
                if (translate) Holder.transform.position += velocity * dt;
                Visual.AdvanceAnimation(velocity, grounded, crouch, dt);
                CheckSkeleton();
            }

            public void Settle(bool crouch = false, float duration = 1.5f, float dt = Dt)
            {
                int steps = Mathf.CeilToInt(duration / dt);
                for (int i = 0; i < steps; i++) Step(Vector3.zero, true, crouch, dt);
            }

            public void CheckSkeleton()
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    Require(Finite(t.position) && Finite(t.localScale) && Finite(t.localRotation), "non-finite transform: " + t.name);
                    Require(Vector3.Distance(scales[i], t.localScale) < .0001f, "animation squashed/scaled bone: " + t.name);
                }
                foreach (var leg in Legs)
                {
                    Require(Mathf.Abs(Vector3.Distance(leg.Hip.position, leg.Knee.position) - leg.ThighLength) < .002f,
                        "thigh length changed: " + leg.Hip.name);
                    Require(Mathf.Abs(Vector3.Distance(leg.Knee.position, leg.Ankle.position) - leg.ShinLength) < .002f,
                        "shin length changed: " + leg.Knee.name);
                }
            }

            public float LowestVertex()
            {
                renderer.BakeMesh(baked);
                float lowest = float.PositiveInfinity;
                foreach (var vertex in baked.vertices)
                {
                    var world = renderer.transform.TransformPoint(vertex);
                    Require(Finite(world), "non-finite skinned vertex in locomotion pose");
                    lowest = Mathf.Min(lowest, world.y - FloorY);
                }
                return lowest;
            }

            public float SupportError()
            {
                float error = 0;
                foreach (var contact in contacts)
                    if (!(bool)contact.Swinging.GetValue(contact.State))
                        error = Mathf.Max(error, Vector3.Distance(contact.Leg.Ankle.position, (Vector3)contact.Position.GetValue(contact.State)));
                return error;
            }

            public void CheckSettledFeet(string label)
            {
                var before = Legs.Select(l => l.Ankle.position).ToArray();
                Settle(duration: .4f);
                for (int i = 0; i < Legs.Length; i++)
                {
                    var local = Local(Legs[i].Ankle);
                    Require(Vector3.Distance(before[i], Legs[i].Ankle.position) < .015f, label + ": foot kept moving after settling");
                    Require(Mathf.Abs(local.y - Legs[i].StandingAnkle.y) < .06f, label + ": foot remained raised");
                }
                Require(LowestVertex() >= -.045f, label + ": sole penetrates the floor");
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(baked);
                UnityEngine.Object.DestroyImmediate(Holder);
            }
        }

        sealed class MotionResult
        {
            public float AlongRange, AcrossRange, MeanLift, MeanHeadHeight;
            public Vector3[] SettledFeet;
            public int[] Contacts = new int[2];
        }

        [MenuItem("Zone Z/Validate operator locomotion")]
        public static void Run()
        {
            GameObject floor = null;
            try
            {
                // Own and remove only our fixtures. This also works inside WorldValidation's unsaved scene.
                // The floor is far above every gameplay collider, so the foot probes cannot hit the map.
                floor = new GameObject("Locomotion validation floor");
                floor.transform.position = Origin + Vector3.down * .5f;
                floor.AddComponent<BoxCollider>().size = new Vector3(100, 1, 100);
                Physics.SyncTransforms();

                var walk = CheckMotion(Vector3.forward * 1.6f, Dt, "forward walk");
                var run = CheckMotion(Vector3.forward * 6f, Dt, "run");
                CheckMotion(Vector3.back * 1.6f, Dt, "backward walk");
                CheckMotion(Vector3.left * 1.6f, Dt, "left strafe");
                CheckMotion(Vector3.right * 1.6f, Dt, "right strafe");
                CheckMotion(new Vector3(1.13f, 0, 1.13f), Dt, "diagonal walk");
                CheckCrouchAndRespawn();
                CheckAirAndLanding();
                CheckStartStopReverse();
                CheckAirborneInitialization();
                var at30 = CheckMotion(Vector3.forward * 1.6f, 1f / 30f, "30 Hz walk");
                var at120 = CheckMotion(Vector3.forward * 1.6f, 1f / 120f, "120 Hz walk");
                CheckFrameRate(walk, at30, "30 Hz");
                CheckFrameRate(walk, at120, "120 Hz");
                Debug.Log($"ZONEZ_LOCOMOTION_VALIDATION_OK: actual skinned bones; forward/back/left/right/diagonal, run 6 m/s, planted contacts, crouch, stop/start/reverse reach and continuity, respawn including airborne, air/landing, 30/60/120 Hz; walkRange={walk.AlongRange:F3}, runRange={run.AlongRange:F3}, walkContacts={walk.Contacts[0]}/{walk.Contacts[1]}, runContacts={run.Contacts[0]}/{run.Contacts[1]}.");
            }
            finally
            {
                if (floor) UnityEngine.Object.DestroyImmediate(floor);
                Physics.SyncTransforms();
            }
        }

        static MotionResult CheckMotion(Vector3 velocity, float dt, string label)
        {
            using (var subject = new Subject(Origin))
            {
                subject.Settle(dt: dt);
                var result = new MotionResult();
                var direction = velocity.normalized;
                var across = Vector3.Cross(Vector3.up, direction);
                var minAlong = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var maxAlong = new[] { float.NegativeInfinity, float.NegativeInfinity };
                var minAcross = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var maxAcross = new[] { float.NegativeInfinity, float.NegativeInfinity };
                var previousFeet = subject.Legs.Select(l => l.Ankle.position).ToArray();
                float liftSum = 0, headSum = 0;
                int samples = 0;
                int steps = Mathf.RoundToInt(3f / dt);
                for (int frame = 0; frame < steps; frame++)
                {
                    subject.Step(velocity, true, false, dt);
                    for (int leg = 0; leg < 2; leg++)
                    {
                        var local = subject.Local(subject.Legs[leg].Ankle);
                        var world = subject.Legs[leg].Ankle.position;
                        if (frame * dt >= .5f)
                        {
                            minAlong[leg] = Mathf.Min(minAlong[leg], Vector3.Dot(local, direction));
                            maxAlong[leg] = Mathf.Max(maxAlong[leg], Vector3.Dot(local, direction));
                            minAcross[leg] = Mathf.Min(minAcross[leg], Vector3.Dot(local, across));
                            maxAcross[leg] = Mathf.Max(maxAcross[leg], Vector3.Dot(local, across));
                            // Ankle motion during a heel/toe roll is allowed. Sliding at travel speed is not.
                            if (Vector3.Distance(world, previousFeet[leg]) / dt < Mathf.Max(.45f, velocity.magnitude * .12f))
                                result.Contacts[leg]++;
                            liftSum += Mathf.Max(0, local.y - subject.Legs[leg].StandingAnkle.y);
                        }
                        previousFeet[leg] = world;
                    }
                    if (frame * dt >= .5f) { headSum += subject.Local(subject.Visual.Head).y; samples++; }
                }
                for (int leg = 0; leg < 2; leg++)
                {
                    result.AlongRange += (maxAlong[leg] - minAlong[leg]) * .5f;
                    result.AcrossRange += (maxAcross[leg] - minAcross[leg]) * .5f;
                    Require(result.Contacts[leg] * dt >= .07f, label + ": no sustained world contact on foot " + leg);
                }
                result.MeanLift = liftSum / (samples * 2);
                result.MeanHeadHeight = headSum / samples;
                Require(result.AlongRange > .12f, label + ": feet do not travel through a meaningful stride");
                Require(result.AlongRange > result.AcrossRange * 1.2f, label + ": feet stride across the movement direction");
                Require(result.MeanLift > .006f, label + ": feet shuffle without a swing clearance");
                subject.Settle(dt: dt);
                subject.CheckSettledFeet(label + " stop");
                result.SettledFeet = subject.Legs.Select(l => subject.Local(l.Ankle)).ToArray();
                return result;
            }
        }

        static void CheckCrouchAndRespawn()
        {
            using (var subject = new Subject(Origin))
            {
                subject.Settle();
                float head = subject.Local(subject.Visual.Head).y;
                var kneeAngles = subject.Legs.Select(l => l.KneeAngle).ToArray();
                for (int i = 0; i < 90; i++)
                {
                    subject.Step(Vector3.zero, true, true, Dt);
                    if (i % 10 == 0) Require(subject.LowestVertex() >= -.045f, "crouch transition pushes soles through the floor");
                }
                Require(subject.Local(subject.Visual.Head).y < head - .25f, "crouch did not lower the body");
                for (int i = 0; i < 2; i++) Require(subject.Legs[i].KneeAngle < kneeAngles[i] - 15f, "crouch did not bend knee " + i);
                for (int i = 0; i < 120; i++)
                {
                    subject.Step(Vector3.forward * 1.1f, true, true, Dt);
                    if (i % 20 == 0) Require(subject.LowestVertex() >= -.05f, "crouch walking pushes soles through the floor");
                }
                subject.Settle();
                subject.CheckSettledFeet("stand from crouch");
                subject.Visual.SetDead(true);
                for (int i = 0; i < 45; i++) subject.Step(Vector3.zero, true, false, Dt);
                subject.Holder.transform.position = Origin + new Vector3(17, 0, 11);
                subject.Visual.SetDead(false);
                subject.Step(Vector3.zero, true, false, Dt);
                subject.Settle();
                subject.CheckSettledFeet("respawn");
                foreach (var leg in subject.Legs)
                    Require(Vector3.Distance(subject.Local(leg.Ankle), leg.StandingAnkle) < .13f,
                        "respawn kept a foot contact at the previous life position");
            }
        }

        static void CheckAirAndLanding()
        {
            using (var subject = new Subject(Origin))
            {
                subject.Settle();
                for (int i = 0; i < 60; i++) subject.Step(Vector3.forward * 3f, true, false, Dt);
                subject.Holder.transform.position += Vector3.up * .9f;
                var velocity = Vector3.forward * 3f;
                for (int i = 0; i < 45; i++) subject.Step(velocity, false, false, Dt);
                var airPose = subject.Legs.Select(l => subject.Local(l.Ankle)).ToArray();
                for (int i = 0; i < 30; i++)
                {
                    subject.Step(velocity, false, false, Dt);
                    for (int leg = 0; leg < 2; leg++)
                        Require(Vector3.Distance(subject.Local(subject.Legs[leg].Ankle), airPose[leg]) < .055f,
                            "airborne legs continue the ground stride cycle");
                }
                var position = subject.Holder.transform.position;
                position.y = FloorY;
                subject.Holder.transform.position = position;
                subject.Step(new Vector3(0, -4f, 0), true, false, Dt, translate: false);
                for (int i = 0; i < 60; i++)
                {
                    subject.Step(Vector3.zero, true, false, Dt);
                    if (i % 10 == 0) Require(subject.LowestVertex() >= -.05f, "landing pushes soles through the floor");
                }
                subject.CheckSettledFeet("landing");
            }
        }

        static void CheckFrameRate(MotionResult reference, MotionResult sample, string label)
        {
            Require(Mathf.Abs(sample.AlongRange - reference.AlongRange) < Mathf.Max(.09f, reference.AlongRange * .3f), label + ": stride changed substantially with frame rate");
            Require(Mathf.Abs(sample.MeanLift - reference.MeanLift) < .025f, label + ": swing clearance changed substantially with frame rate");
            Require(Mathf.Abs(sample.MeanHeadHeight - reference.MeanHeadHeight) < .035f, label + ": body height changed substantially with frame rate");
            for (int leg = 0; leg < 2; leg++)
                Require(Vector3.Distance(sample.SettledFeet[leg], reference.SettledFeet[leg]) < .09f, label + ": stopped stance depends on frame rate");
        }

        static void CheckStartStopReverse()
        {
            using (var subject = new Subject(Origin))
            {
                subject.Settle();
                Vector3 velocity = Vector3.zero;
                var targets = new[] { Vector3.forward * 6, Vector3.zero, Vector3.back * 6, Vector3.zero,
                    Vector3.left * 4, Vector3.right * 4, Vector3.forward * 4, Vector3.zero };
                var seconds = new[] { 1.1f, .32f, 1.1f, 1.5f, .75f, .75f, .75f, 1.5f };
                float badSupportTime = 0;
                float maxSupportError = 0, maxFootDelta = 0;
                for (int part = 0; part < targets.Length; part++)
                {
                    int frames = Mathf.RoundToInt(seconds[part] / Dt);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        var feet = subject.Legs.Select(l => subject.Local(l.Ankle)).ToArray();
                        var knees = subject.Legs.Select(l => l.KneeAngle).ToArray();
                        // Match the motor's acceleration, then include a direct bot-like strafe reversal.
                        velocity = part == 5 ? targets[part] : Vector3.MoveTowards(velocity, targets[part], 32 * Dt);
                        subject.Step(velocity, true, false, Dt);
                        float supportError = subject.SupportError();
                        maxSupportError = Mathf.Max(maxSupportError, supportError);
                        badSupportTime = supportError > .065f ? badSupportTime + Dt : 0;
                        Require(supportError < .22f, $"transition {part}: planned support is outside leg reach ({supportError:F3} m)");
                        Require(badSupportTime <= .13f, $"transition {part}: planted ankle skates away from its world contact ({supportError:F3} m)");
                        for (int leg = 0; leg < 2; leg++)
                        {
                            float delta = Vector3.Distance(feet[leg], subject.Local(subject.Legs[leg].Ankle));
                            maxFootDelta = Mathf.Max(maxFootDelta, delta);
                            Require(delta < .28f, $"transition {part}: foot {leg} snapped {delta:F3} m in one frame");
                            Require(Mathf.Abs(knees[leg] - subject.Legs[leg].KneeAngle) < 60f,
                                $"transition {part}: knee {leg} snapped between poses");
                        }
                    }
                }
                subject.CheckSettledFeet("rapid direction changes");
                Debug.Log($"ZONEZ_LOCOMOTION_TRANSITIONS: maxSupportError={maxSupportError:F4} m, maxLocalFootDelta={maxFootDelta:F4} m/frame.");
            }
        }

        static void CheckAirborneInitialization()
        {
            using (var subject = new Subject(Origin + Vector3.up * 1.2f))
            {
                subject.Step(Vector3.zero, false, false, Dt);
                CheckFreshAirFeet(subject, "initial airborne spawn");
                subject.Visual.SetDead(true);
                for (int i = 0; i < 40; i++) subject.Step(Vector3.zero, false, false, Dt);
                subject.Holder.transform.position = Origin + new Vector3(10, 1.2f, 10);
                subject.Visual.SetDead(false);
                subject.Step(Vector3.zero, false, false, Dt);
                CheckFreshAirFeet(subject, "airborne respawn");
            }
        }

        static void CheckFreshAirFeet(Subject subject, string label)
        {
            foreach (var leg in subject.Legs)
            {
                var local = subject.Local(leg.Ankle);
                Require(Mathf.Abs(local.x - leg.StandingAnkle.x) < .055f, label + ": feet collapsed to an uninitialized local origin");
                Require(local.y > leg.StandingAnkle.y - .04f, label + ": a stale contact pulls the foot below its body");
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException("Locomotion: " + message); }
    }
}
#endif
