using UnityEngine;
using UnityEngine.AI;

namespace ZoneZ
{
    /// <summary>Original, non-combat fauna. No colliders, hit zones or reward-producing Actor components.</summary>
    public sealed class AmbientZoo : MonoBehaviour
    {
        public enum Species { Squirrel, Rat, Turtle, Pigeon }
        public Species Kind;
        Transform body, head, tail, leftWing, rightWing;
        readonly Transform[] feet = new Transform[4];
        NavMeshPath navigationPath;
        Vector3 home, target, groundPosition, alarmOrigin;
        Vector3[] route = new Vector3[0];
        float timer, phase, alarm, flight, noiseAt, moveSpeed, bodyRestHeight;
        int seed, soundCursor, routeCorner, routeSequence;

        const float GroundOffset = .008f;
        public bool IsGroundAnimal => Kind != Species.Pigeon;
        public bool NavigationReady { get; private set; }
        public Vector3 RoamOrigin => home;
        public Vector3 CurrentDestination => target;
        public float RoamRadius => Kind == Species.Squirrel ? 18 : Kind == Species.Rat ? 14 : 10;

        public static void Create(Transform parent)
        {
            // Public lawns and paths, outside the former peripheral animal pens.
            // Create after the world NavMesh has been built, so placement and roaming use it immediately.
            Spawn(parent, Species.Squirrel, new Vector3(-47, 0, 30), 1);
            Spawn(parent, Species.Squirrel, new Vector3(-5, 0, 49), 2);
            Spawn(parent, Species.Rat, new Vector3(50, 0, 22), 3);
            Spawn(parent, Species.Rat, new Vector3(0, 0, -48), 4);
            Spawn(parent, Species.Turtle, new Vector3(-21, 0, 25), 5);
            for (int i = 0; i < 4; i++) Spawn(parent, Species.Pigeon, new Vector3(53 + i * 1.4f, .05f, 60 + i % 2), 6 + i);
            Remains(parent, new Vector3(-47, .08f, 58), 18);
            Remains(parent, new Vector3(-41, .08f, 58), -27);
            if (Application.isPlaying) MenuAmbience.Create(parent);
        }

        static void Spawn(Transform parent, Species kind, Vector3 position, int seed)
        {
            var root = WorldArt.Group(parent, "Ambient " + kind + " " + seed, position);
            var animal = root.gameObject.AddComponent<AmbientZoo>();
            animal.Kind = kind; animal.home = animal.target = position; animal.seed = seed;
            animal.timer = seed * .41f; animal.soundCursor = AudioFX.LastNoiseId;
            if (animal.IsGroundAnimal)
            {
                animal.NavigationReady = TryGroundPoint(position, 3, out var ground);
                if (animal.NavigationReady)
                {
                    animal.home = animal.target = animal.groundPosition = ground;
                    root.position = ground + Vector3.up * GroundOffset;
                }
                else Debug.LogError("Ambient animal needs reachable public ground: " + root.name, animal);
            }
            animal.Build();
        }

        void Build()
        {
            bool bird = Kind == Species.Pigeon, turtle = Kind == Species.Turtle;
            var fur = WorldArt.Mat("Fauna " + Kind, bird ? new Color(.38f,.42f,.46f) : turtle ? new Color(.32f,.34f,.18f) : Kind == Species.Squirrel ? new Color(.49f,.28f,.12f) : new Color(.27f,.25f,.23f));
            var pale = WorldArt.Mat("Fauna pale underside", new Color(.64f,.58f,.44f));
            var dark = WorldArt.Mat("Animal eyes", new Color(.017f,.02f,.015f), 0, .7f);
            var pink = WorldArt.Mat("Animal nose and feet", new Color(.49f,.28f,.25f));
            body = WorldArt.Group(transform, "Animated body", new Vector3(0, bird ? .19f : turtle ? .12f : .15f, 0));
            bodyRestHeight = body.localPosition.y;
            WorldArt.Sphere(body, turtle ? "Domed shell" : "Body", Vector3.zero, turtle ? new Vector3(.43f,.25f,.56f) : bird ? new Vector3(.23f,.30f,.38f) : new Vector3(.20f,.23f,.42f), fur);
            if (turtle)
            {
                for (int i = -1; i <= 1; i++) WorldArt.Sphere(body, "Shell scute", new Vector3(i * .106f,.093f,.015f), new Vector3(.095f,.067f,.32f), pale);
            }
            else WorldArt.Sphere(body, "Pale breast", new Vector3(0,-.026f,.104f), new Vector3(.17f,.20f,.22f), pale);
            head = WorldArt.Group(body, "Head", new Vector3(0, bird ? .20f : .03f, bird ? .12f : .22f));
            WorldArt.Sphere(head, "Head shape", Vector3.zero, Vector3.one * (bird ? .16f : .15f), fur);
            WorldArt.Sphere(head, "Muzzle", new Vector3(0,-.015f,.077f), new Vector3(.09f,.065f,.095f), turtle ? fur : pale);
            for (int side = -1; side <= 1; side += 2)
            {
                WorldArt.Sphere(head, "Eye", new Vector3(side * .064f,.026f,.042f), Vector3.one * .027f, dark);
                if (!bird && !turtle) WorldArt.Sphere(head, "Ear", new Vector3(side * .057f,.081f,-.017f), new Vector3(.048f,.079f,.023f), Kind == Species.Rat ? pink : fur);
            }
            if (bird)
            {
                WorldArt.Taper(head,"Beak",new Vector3(0,-.005f,.104f),new Vector3(.038f,.028f,0),new Vector3(.005f,.006f,0),.079f,pink);
                leftWing = Wing(-1,fur); rightWing = Wing(1,fur);
                tail = WorldArt.Group(body,"Tail",new Vector3(0,-.028f,-.20f));
                for(int i=-1;i<=1;i++) WorldArt.Box(tail,"Tail feather",new Vector3(i*.034f,0,-.09f),new Vector3(.036f,.015f,.21f),fur,false,"feather",new Vector3(-14,i*8,0));
            }
            else if (!turtle)
            {
                tail=WorldArt.Group(body,"Tail",new Vector3(0,-.015f,-.19f));
                if(Kind==Species.Squirrel)
                {
                    WorldArt.Sphere(tail,"Bushy tail",new Vector3(0,.12f,-.115f),new Vector3(.15f,.37f,.19f),fur);
                    WorldArt.Sphere(tail,"Tail curl",new Vector3(0,.30f,-.063f),new Vector3(.14f,.15f,.18f),fur);
                }
                else for(int i=0;i<5;i++) WorldArt.Beam(tail,"Rat tail segment",new Vector3(i*.013f,-.025f,-i*.07f),new Vector3((i+1)*.013f,-.025f,-(i+1)*.07f),.018f-i*.0025f,pink);
            }
            for(int i=0;i<(bird?2:4);i++)
            {
                feet[i]=WorldArt.Group(transform,"Foot "+i,new Vector3((i%2==0?-1:1)*(turtle?.18f:.08f),bird?.038f:.021f,bird?0:i<2?.13f:-.13f));
                WorldArt.Sphere(feet[i],"Paw",Vector3.zero,new Vector3(turtle?.10f:.05f,.04f,turtle?.14f:.11f),bird?pink:fur);
            }
        }

        Transform Wing(int side,Material material)
        {
            var wing=WorldArt.Group(body,side<0?"Left wing":"Right wing",new Vector3(side*.10f,.06f,.015f));
            WorldArt.Sphere(wing,"Folded wing",new Vector3(side*.025f,-.025f,-.057f),new Vector3(.075f,.18f,.28f),material);
            for(int i=0;i<4;i++) WorldArt.Box(wing,"Flight feather",new Vector3(side*(.045f+i*.031f),-.024f,-.11f-i*.014f),new Vector3(.04f,.022f,.20f),material,false,"feather",new Vector3(0,side*-12,0));
            return wing;
        }

        static void Remains(Transform parent,Vector3 position,float yaw)
        {
            var root=WorldArt.Group(parent,"Old non-graphic remains",position,new Vector3(0,yaw,0));
            var bone=WorldArt.Mat("Old animal bone",new Color(.64f,.60f,.45f));
            WorldArt.Beam(root,"Spine",new Vector3(0,.04f,-.36f),new Vector3(0,.04f,.31f),.045f,bone);
            for(int i=0;i<5;i++) for(int side=-1;side<=1;side+=2)
                WorldArt.Beam(root,"Rib",new Vector3(0,.07f,-.22f+i*.1f),new Vector3(side*.19f,.025f,-.27f+i*.1f),.028f,bone);
            WorldArt.Sphere(root,"Weathered skull",new Vector3(0,.07f,.39f),new Vector3(.17f,.11f,.22f),bone);
        }

        static bool TryGroundPoint(Vector3 point, float projection, out Vector3 ground)
        {
            ground = default;
            if (Mathf.Abs(point.x) > 77 || Mathf.Abs(point.z) > 67) return false;
            if (!NavMesh.SamplePosition(point, out var hit, projection, NavMesh.AllAreas)) return false;
            // Avoid nearby roof/planter islands when projecting authored lawn-level destinations.
            if (hit.position.y < -.2f || hit.position.y > .8f ||
                Mathf.Abs(hit.position.x) > 77 || Mathf.Abs(hit.position.z) > 67) return false;
            ground = hit.position;
            return true;
        }

        /// <summary>Deterministic, non-moving route probe for editor validation.</summary>
        public bool TryPreviewRoamingPath(int sample, out Vector3[] corners)
            => TrySelectGroundRoute(false, sample, Vector3.zero, out corners);

        /// <summary>Uses the same reachable, away-from-noise destinations as a runtime escape.</summary>
        public bool TryPreviewEscapePath(Vector3 noiseOrigin, out Vector3[] corners)
            => TrySelectGroundRoute(true, 0, noiseOrigin, out corners);

        bool TrySelectGroundRoute(bool escape, int sample, Vector3 noiseOrigin, out Vector3[] corners)
        {
            corners = new Vector3[0];
            if (!IsGroundAnimal || !NavigationReady) return false;
            if (navigationPath == null) navigationPath = new NavMeshPath();
            var random = new System.Random(unchecked(seed * 4099 + sample * 7919));
            var away = groundPosition - noiseOrigin; away.y = 0;
            if (away.sqrMagnitude < .01f) away = new Vector3(Mathf.Sin(seed), 0, Mathf.Cos(seed));
            away.Normalize();
            float threatDistance = HorizontalDistance(groundPosition, noiseOrigin);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector3 candidate;
                if (escape)
                {
                    float distance = Kind == Species.Turtle ? 2.2f : Kind == Species.Rat ? 7 : 10;
                    distance *= .75f + (float)random.NextDouble() * .6f;
                    var direction = Quaternion.Euler(0, -65 + (float)random.NextDouble() * 130, 0) * away;
                    candidate = groundPosition + direction * distance;
                }
                else
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float distance = Mathf.Lerp(3, RoamRadius, Mathf.Sqrt((float)random.NextDouble()));
                    candidate = home + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * distance;
                }
                if (!TryGroundPoint(candidate, 2, out var destination)) continue;
                if (HorizontalDistance(groundPosition, destination) < 1) continue;
                if (escape && HorizontalDistance(destination, noiseOrigin) < threatDistance + .75f) continue;
                if (!NavMesh.CalculatePath(groundPosition, destination, NavMesh.AllAreas, navigationPath) ||
                    navigationPath.status != NavMeshPathStatus.PathComplete) continue;
                var points = navigationPath.corners;
                if (points.Length < 2) continue;
                float length = 0;
                bool groundLevel = true;
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i].y < -.2f || points[i].y > .8f) groundLevel = false;
                    if (i > 0) length += Vector3.Distance(points[i - 1], points[i]);
                }
                if (!groundLevel || length > RoamRadius * 3 || !CanFollowGroundRoute(points)) continue;
                corners = points;
                return true;
            }
            return false;
        }

        static bool CanFollowGroundRoute(Vector3[] corners)
        {
            // Complete paths can put corners exactly on a NavMesh edge. Reject routes
            // whose projected ground steps would trip the movement boundary guard.
            for (int corner = 1; corner < corners.Length; corner++)
            {
                var from = corners[corner - 1];
                var to = corners[corner];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .25f));
                var previous = from;
                for (int step = 1; step <= steps; step++)
                {
                    var point = Vector3.Lerp(from, to, step / (float)steps);
                    if (!TryGroundPoint(point, .2f, out var ground) ||
                        NavMesh.Raycast(previous, ground, out var edge, NavMesh.AllAreas)) return false;
                    previous = ground;
                }
            }
            return true;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }

        void HearRecentShots()
        {
            foreach (var noise in AudioFX.Noises)
            {
                if (noise.Id <= soundCursor || noise.Radius < 20 || !noise.Source || !noise.Source.Match) continue;
                float age = noise.Source.Match.MatchTime - noise.Time;
                if (age < 0 || age > 1.25f) continue;
                float range = Mathf.Min(20, noise.Radius);
                if ((noise.Position - transform.position).sqrMagnitude >= range * range) continue;
                alarm = Kind == Species.Pigeon ? 5 : Kind == Species.Turtle ? 3 : 4;
                alarmOrigin = noise.Position;
                timer = 0;
            }
            soundCursor = AudioFX.LastNoiseId;
        }

        bool MoveOnGround(float dt)
        {
            if (!NavigationReady) return false;
            if (timer <= 0)
            {
                bool found = TrySelectGroundRoute(alarm > 0, ++routeSequence, alarmOrigin, out var corners);
                route = corners; routeCorner = 1;
                timer = found ? float.PositiveInfinity : 1;
                if (found) target = route[route.Length - 1];
            }
            bool hasRoute = routeCorner < route.Length;
            float walkSpeed = Kind == Species.Turtle ? .105f : Kind == Species.Rat ? .52f : .75f;
            float escapeSpeed = Kind == Species.Turtle ? .14f : Kind == Species.Rat ? 1.6f : 2.1f;
            moveSpeed = Mathf.MoveTowards(moveSpeed, hasRoute ? alarm > 0 ? escapeSpeed : walkSpeed : 0,
                dt * (Kind == Species.Turtle ? .3f : 4));
            var previous = groundPosition;
            float remainingStep = moveSpeed * dt;
            while (hasRoute && remainingStep > 0)
            {
                var nextCorner = route[routeCorner];
                float distance = Vector3.Distance(groundPosition, nextCorner);
                float step = Mathf.Min(distance, remainingStep);
                var candidate = Vector3.MoveTowards(groundPosition, nextCorner, step);
                // Reproject every small step for the road/lawn height; never round a corner through a wall.
                if (!TryGroundPoint(candidate, .2f, out var ground) ||
                    NavMesh.Raycast(groundPosition, ground, out var boundary, NavMesh.AllAreas))
                {
                    route = new Vector3[0]; routeCorner = 0; timer = .35f; moveSpeed = 0;
                    break;
                }
                groundPosition = ground;
                remainingStep -= step;
                if (distance <= step + .001f)
                {
                    routeCorner++;
                    hasRoute = routeCorner < route.Length;
                    if (!hasRoute)
                    {
                        // Short foraging pauses make roaming readable; turtles linger between slow walks.
                        timer = alarm > 0 ? .1f : Kind == Species.Turtle ? 3 + seed * .23f : 1.1f + seed * .19f;
                        route = new Vector3[0]; routeCorner = 0;
                    }
                }
            }
            transform.position = groundPosition + Vector3.up * GroundOffset;
            var direction = groundPosition - previous; direction.y = 0;
            bool moving = direction.sqrMagnitude > .000000001f;
            if (moving) transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(direction), dt * (Kind == Species.Turtle ? 2.5f : 9));
            return moving;
        }

        bool MovePigeon(float dt)
        {
            if (timer <= 0)
            {
                timer = alarm > 0 ? 3 : 3.5f + seed * .13f;
                float angle = phase * .71f + seed * 2.13f;
                target = home + new Vector3(Mathf.Sin(angle) * 1.1f, 0, Mathf.Cos(angle) * 1.1f);
            }
            var direction = target - transform.position; direction.y = 0;
            bool moving = direction.sqrMagnitude > .008f;
            moveSpeed = .32f;
            if (moving)
            {
                transform.position += direction.normalized * Mathf.Min(direction.magnitude, dt * moveSpeed);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), dt * 5);
            }
            flight = Mathf.MoveTowards(flight, alarm > 0 ? 1 : 0, dt * (alarm > 0 ? 1.6f : .7f));
            var p = transform.position; p.y = home.y + flight * (2.3f + Mathf.Sin(phase * 3 + seed) * .17f); transform.position = p;
            return moving;
        }

        void Update()
        {
            if (GameRoot.Instance && GameRoot.Instance.Paused) return;
            float dt = Time.deltaTime; phase += dt; alarm = Mathf.Max(0, alarm - dt); timer -= dt; noiseAt -= dt;
            if (noiseAt <= 0) { noiseAt = .25f; HearRecentShots(); }
            bool moving = IsGroundAnimal ? MoveOnGround(dt) : MovePigeon(dt);
            float gait = Kind == Species.Turtle ? 4 : Kind == Species.Squirrel ? 12 : 9;
            var bodyPosition = body.localPosition;
            bodyPosition.y = bodyRestHeight + (moving ? Mathf.Abs(Mathf.Sin(phase * gait)) *
                (Kind == Species.Squirrel ? .014f : Kind == Species.Turtle ? .002f : .004f) : 0);
            body.localPosition = bodyPosition;
            head.localRotation = Quaternion.Euler(moving ? Mathf.Sin(phase * gait) * 6 : Mathf.Sin(phase * 1.3f + seed) * 8,
                Mathf.Sin(phase + seed) * (Kind == Species.Turtle ? 7 : 14), 0);
            if (tail) tail.localRotation = Quaternion.Euler(0, Mathf.Sin(phase * (moving ? 9 : 2)) * 12, 0);
            for (int i = 0; i < feet.Length; i++) if (feet[i])
                feet[i].localRotation = Quaternion.Euler(moving ? Mathf.Sin(phase * moveSpeed * 18 + i * Mathf.PI) * 24 : 0, 0, 0);
            if (leftWing)
            {
                float spread = flight * (45 + Mathf.Sin(phase * 23) * 42);
                leftWing.localRotation = Quaternion.Euler(0, 0, -spread); rightWing.localRotation = Quaternion.Euler(0, 0, spread);
            }
        }
    }
}
