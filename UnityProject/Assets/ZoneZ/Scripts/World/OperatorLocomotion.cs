using UnityEngine;

namespace ZoneZ
{
    /// <summary>Presentation-only foot planner. Contacts remain in world space while the body moves.</summary>
    internal sealed class OperatorLocomotion
    {
        internal sealed class Foot
        {
            public readonly Vector3 Rest;
            public Vector3 Position, Normal = Vector3.up;
            public Quaternion Rotation = Quaternion.identity;
            public bool Swinging;
            internal Vector3 Anchor, Start, Target, TargetNormal, AirLocal, StepDirection;
            internal Quaternion ContactYaw, StartYaw, TargetYaw, StartRotation;
            internal float Elapsed, Duration, Lift;
            internal bool SwingRegion;
            public Foot(Vector3 rest) { Rest = rest; }
        }

        public readonly Foot Left, Right;
        public float Speed { get; private set; }
        public float Run { get; private set; }
        public float Cycle { get; private set; }
        public float Landing { get; private set; }
        public Vector3 LocalVelocity { get; private set; }
        public float Air { get; private set; }
        readonly RaycastHit[] groundHits = new RaycastHit[24];
        Vector3 previousPosition, velocity, travelVelocity;
        float previousVertical, idleTime;
        bool initialized, wasGrounded;

        public OperatorLocomotion(Vector3 left, Vector3 right) { Left = new Foot(left); Right = new Foot(right); }

        public void Reset() { initialized = false; velocity = Vector3.zero; Speed = Run = Cycle = Landing = Air = idleTime = 0; }

        public void Update(Transform space, Vector3 worldVelocity, bool grounded, float crouch, float dt)
        {
            dt = Mathf.Clamp(dt, 0, .1f);
            if (dt <= 0) return;
            if (!initialized || Vector3.Distance(previousPosition, space.position) > Mathf.Max(1.5f, worldVelocity.magnitude * dt * 3))
            {
                Initialize(space, crouch, grounded);
                velocity = new Vector3(worldVelocity.x, 0, worldVelocity.z);
            }
            previousPosition = space.position;
            Vector3 flat = new Vector3(worldVelocity.x, 0, worldVelocity.z);
            travelVelocity = flat;
            velocity = Vector3.Lerp(velocity, flat, 1 - Mathf.Exp(-16 * dt));
            Speed = velocity.magnitude;
            LocalVelocity = space.InverseTransformDirection(velocity);
            Run = Mathf.InverseLerp(2.5f, 5.8f, Speed) * (1 - crouch);
            bool moving = flat.magnitude > .12f;
            idleTime = moving ? 0 : idleTime + dt;
            Landing *= Mathf.Exp(-12 * dt);
            if (grounded && !wasGrounded)
            {
                Landing = Mathf.Clamp(-previousVertical * .012f, .025f, .10f);
                // A short placement blends the tucked airborne feet onto their new supports.
                BeginStep(Left, space, crouch, .12f, 0, Vector3.zero, 0);
                BeginStep(Right, space, crouch, .12f, 0, Vector3.zero, 0);
            }
            Air = Mathf.MoveTowards(Air, grounded ? 0 : 1, dt * (grounded ? 12 : 9));

            if (grounded)
            {
                // Running has a shorter support interval; swing lasts long enough to fold the knee.
                float stance = Mathf.Lerp(.64f, .43f, Run);
                float distancePerCycle = Mathf.Lerp(.55f, 2.65f, Mathf.Clamp01(Speed / 6.5f)) * Mathf.Lerp(1, .68f, crouch);
                float rate = Mathf.Clamp(Speed / distancePerCycle, 0, 2.9f);
                if (moving) Cycle = Mathf.Repeat(Cycle + dt * rate, 1);
                float duration = Mathf.Clamp((1 - stance) / Mathf.Max(.2f, rate), .15f, .38f);
                float lead = Mathf.Min(.59f, Speed / Mathf.Max(.4f, rate) * stance * .5f);
                AdvanceFoot(Left, space, crouch, dt, Cycle, stance, duration, lead, moving);
                AdvanceFoot(Right, space, crouch, dt, Mathf.Repeat(Cycle + .5f, 1), stance, duration, lead, moving);

                if (!moving && idleTime > .08f && !Left.Swinging && !Right.Swinging)
                {
                    // Settle one foot at a time: stopping, crouching and turning never drag both soles.
                    float leftError = PlacementError(Left, space, crouch), rightError = PlacementError(Right, space, crouch);
                    if (Mathf.Max(leftError, rightError) > .075f)
                        BeginStep(leftError >= rightError ? Left : Right, space, crouch, .22f, .055f, Vector3.zero, 0);
                    else if (idleTime > .4f) Cycle = .25f;
                }
            }
            else
            {
                // No ground cycle in flight. Slight asymmetry keeps a jump from looking rigid.
                AirFoot(Left, space, crouch, -.075f, dt);
                AirFoot(Right, space, crouch, .085f, dt);
                Left.SwingRegion = Right.SwingRegion = false;
            }
            wasGrounded = grounded;
            previousVertical = worldVelocity.y;
        }

        void Initialize(Transform space, float crouch, bool grounded)
        {
            initialized = true; wasGrounded = grounded; previousPosition = space.position;
            Cycle = .25f; Air = grounded ? 0 : 1; Landing = 0;
            foreach (var foot in new[] { Left, Right })
            {
                foot.Position = Ground(space, Neutral(foot, crouch), foot.Rest.y, out var normal);
                foot.Anchor = foot.Start = foot.Target = foot.Position; foot.Normal = foot.TargetNormal = normal;
                foot.AirLocal = space.InverseTransformPoint(foot.Position);
                foot.ContactYaw = foot.StartYaw = foot.TargetYaw = space.rotation;
                foot.Rotation = AlignToGround(space.rotation, normal);
                foot.Swinging = foot.SwingRegion = false; foot.Elapsed = 0;
            }
        }

        static Vector3 Neutral(Foot foot, float crouch)
        {
            return foot.Rest + new Vector3(Mathf.Sign(foot.Rest.x) * (.022f + crouch * .033f), 0, crouch * .065f);
        }

        float PlacementError(Foot foot, Transform space, float crouch)
        {
            Vector3 delta = space.TransformPoint(Neutral(foot, crouch)) - foot.Anchor; delta.y = 0;
            return Mathf.Max(delta.magnitude, Quaternion.Angle(foot.ContactYaw, space.rotation) * .004f);
        }

        void BeginStep(Foot foot, Transform space, float crouch, float duration, float lift, Vector3 travel, float lead)
        {
            foot.Swinging = true; foot.Elapsed = 0; foot.Duration = duration; foot.Lift = lift;
            foot.Start = foot.Position; foot.StartYaw = foot.ContactYaw; foot.StartRotation = foot.Rotation;
            foot.StepDirection = travel.normalized;
            PlanTarget(foot, space, crouch, travel, duration, lead);
        }

        void PlanTarget(Foot foot, Transform space, float crouch, Vector3 travel, float remaining, float lead, float dt = 0)
        {
            Vector3 local = Neutral(foot, crouch);
            Vector3 direction = travel.sqrMagnitude > .001f ? travel.normalized : Vector3.zero;
            Vector3 localDirection = space.InverseTransformDirection(direction);
            // Stagger the passing feet slightly when sidestepping, so the boots do not cross through each other.
            local.z += Mathf.Abs(localDirection.x) * Mathf.Sign(foot.Rest.x) * .07f;
            Vector3 localLead = localDirection * lead;
            float strafe = Mathf.InverseLerp(.55f, .9f, Mathf.Abs(localDirection.x));
            if (Mathf.Sign(foot.Rest.x) != Mathf.Sign(localDirection.x)) localLead.x *= 1 - strafe * .88f;
            Vector3 target = space.TransformPoint(local + localLead) + travel * remaining;
            // Ground() accepts coordinates relative to the current actor. Forecast only the horizontal travel.
            Vector3 desired = Ground(space, space.InverseTransformPoint(target), foot.Rest.y, out var normal);
            foot.Target = dt > 0 ? Vector3.MoveTowards(foot.Target, desired, dt * (3 + travel.magnitude * 1.3f)) : desired;
            foot.TargetNormal = normal;
            float toeYaw = Mathf.Clamp(localDirection.x * 15 * (localDirection.z < -.1f ? -1 : 1), -15, 15);
            foot.TargetYaw = space.rotation * Quaternion.Euler(0, toeYaw, 0);
        }

        void AdvanceFoot(Foot foot, Transform space, float crouch, float dt, float phase, float stance, float duration, float lead, bool moving)
        {
            bool swingRegion = phase >= stance;
            float behind = Vector3.Dot(space.TransformPoint(Neutral(foot, crouch)) - foot.Anchor, travelVelocity.normalized);
            bool recover = behind > Mathf.Max(.28f, lead + .10f) || behind < -.75f;
            Vector3 localTravel = space.InverseTransformDirection(travelVelocity);
            bool sideStep = Mathf.Abs(localTravel.x) > Mathf.Abs(localTravel.z) * 1.6f && Mathf.Abs(localTravel.x) > .2f;
            bool leadingFoot = Mathf.Sign(foot.Rest.x) == Mathf.Sign(localTravel.x);
            float lateralSupport = space.InverseTransformPoint(foot.Anchor).x * Mathf.Sign(foot.Rest.x);
            // Lift the leading foot before the moving pelvis passes it; the trailing foot closes the gap.
            if (sideStep && leadingFoot && lateralSupport < .08f + Mathf.Abs(localTravel.x) * .055f) recover = true;
            if (moving && !foot.Swinging && (swingRegion && !foot.SwingRegion || recover))
                BeginStep(foot, space, crouch, duration, Mathf.Lerp(.085f, .17f, Run) * Mathf.Lerp(1, .7f, crouch), travelVelocity, lead);
            foot.SwingRegion = moving && swingRegion;
            if (foot.Swinging && moving && foot.StepDirection.sqrMagnitude > .1f && Vector3.Dot(foot.StepDirection, travelVelocity.normalized) < .25f)
                // Redirect from the current pose, rather than moving a late-swing endpoint through the other leg.
                BeginStep(foot, space, crouch, Mathf.Min(.20f, duration), .065f, travelVelocity, lead * .65f);
            if (foot.Swinging)
            {
                foot.Elapsed += dt;
                float t = Mathf.Clamp01(foot.Elapsed / foot.Duration);
                // Replanning early in swing handles reversals. Freeze the landing before contact.
                if (t < .9f) PlanTarget(foot, space, crouch, moving ? travelVelocity : Vector3.zero, foot.Duration - foot.Elapsed, moving ? lead : 0, dt);
                float smooth = t * t * (3 - 2 * t);
                foot.Position = Vector3.Lerp(foot.Start, foot.Target, smooth) + Vector3.up * (Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2) * foot.Lift);
                foot.Normal = Vector3.Slerp(foot.Normal, foot.TargetNormal, smooth);
                foot.ContactYaw = Quaternion.Slerp(foot.StartYaw, foot.TargetYaw, smooth);
                float landingRoll = moving ? -8 * Mathf.Lerp(1, .45f, crouch) : 0;
                Quaternion landingRotation = AlignToGround(foot.TargetYaw, foot.TargetNormal) * Quaternion.Euler(landingRoll, 0, 0);
                foot.Rotation = Quaternion.Slerp(foot.StartRotation, landingRotation, smooth) * Quaternion.Euler(12 * Mathf.Sin(t * Mathf.PI), 0, 0);
                foot.Position += Vector3.up * (Mathf.Abs(Mathf.Sin(landingRoll * Mathf.Deg2Rad)) * .105f * Mathf.SmoothStep(0, 1, (t - .7f) / .3f));
                if (sideStep)
                {
                    Vector3 localFoot = space.InverseTransformPoint(foot.Position);
                    localFoot.x = Mathf.Sign(foot.Rest.x) * Mathf.Max(.06f, localFoot.x * Mathf.Sign(foot.Rest.x));
                    foot.Position = space.TransformPoint(localFoot);
                }
                if (t >= 1) { foot.Swinging = false; foot.Anchor = foot.Target; foot.ContactYaw = foot.TargetYaw; }
            }
            else
            {
                float support = Mathf.Clamp01(phase / stance);
                float heel = -8 * (1 - Mathf.SmoothStep(0, 1, support / .22f));
                float toe = 19 * Mathf.SmoothStep(0, 1, (support - .73f) / .27f);
                float roll = moving ? (heel + toe) * Mathf.Lerp(1, .45f, crouch) * Mathf.Clamp01(Speed) : 0;
                // Preserve the sole contact while the ankle rises for heel-to-toe roll.
                foot.Position = foot.Anchor + Vector3.up * (Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad)) * .105f);
                foot.Rotation = AlignToGround(foot.ContactYaw, foot.Normal) * Quaternion.Euler(roll, 0, 0);
            }
        }

        void AirFoot(Foot foot, Transform space, float crouch, float stagger, float dt)
        {
            foot.Swinging = false;
            Vector3 local = Neutral(foot, crouch) + new Vector3(0, .18f + Mathf.Clamp(previousVertical * .012f, -.04f, .05f), stagger);
            // A local pose follows the body in flight, rather than leaving an anchor on the floor.
            if (wasGrounded) foot.AirLocal = space.InverseTransformPoint(foot.Position);
            foot.AirLocal = Vector3.Lerp(foot.AirLocal, local, 1 - Mathf.Exp(-18 * dt));
            foot.Position = space.TransformPoint(foot.AirLocal);
            foot.ContactYaw = space.rotation;
            foot.Rotation = Quaternion.Slerp(foot.Rotation, space.rotation * Quaternion.Euler(12, 0, 0), 1 - Mathf.Exp(-18 * dt));
            foot.Normal = Vector3.up;
        }

        Vector3 Ground(Transform space, Vector3 local, float ankleHeight, out Vector3 normal)
        {
            Vector3 point = space.TransformPoint(local);
            Vector3 ray = new Vector3(point.x, space.position.y + .65f, point.z);
            int count = Physics.RaycastNonAlloc(ray, Vector3.down, groundHits, 1.45f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity; normal = Vector3.up;
            Vector3 contact = new Vector3(point.x, space.position.y, point.z);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.normal.y < .55f || hit.distance >= nearest || hit.collider.GetComponentInParent<Actor>()) continue;
                nearest = hit.distance; contact = hit.point; normal = hit.normal;
            }
            // The source ankle's rest height includes the boot sole thickness.
            return contact + Vector3.up * ankleHeight;
        }

        static Quaternion AlignToGround(Quaternion yaw, Vector3 normal)
        {
            return Quaternion.FromToRotation(Vector3.up, normal) * yaw;
        }
    }
}
