using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.Rendering;

namespace ZoneZ
{
    public sealed class ZooWorld : MonoBehaviour
    {
        public Vector3[] TeamASpawns, TeamBSpawns, DeathmatchSpawns, LootPoints;
        public Transform MenuCameraAnchor, PreviewAnchor;
        public NavMeshSurface Navigation;
        Transform geometry;
        Collider lawnGround;
        readonly PavedRoutes pavedRoutes = new PavedRoutes();
        Material concrete, pale, darkConcrete, stone, asphalt, soil, grass, wood, rust, metal, glass, leaf, leafLight, yellow, water;
        readonly System.Random rng = new System.Random(73019);

        public static ZooWorld Build(Transform parent)
        {
            var root = new GameObject("LAST SHIFT / Abandoned zoo"); root.transform.SetParent(parent, false);
            var world = root.AddComponent<ZooWorld>(); world.Construct(); return world;
        }

        void Construct()
        {
            Materials(); Lighting();
            geometry = WorldArt.Group(transform, "Static architecture");
            Ground(); Boundaries(); Paths();
            Entrance(new Vector3(-68, 0, 0), 90, "ЗАПАДНЫЙ ВХОД", false);
            Entrance(new Vector3(68, 0, 0), -90, "СЛУЖЕБНЫЙ ВХОД", true);
            Pavilion(new Vector3(-30, 0, 38), 18, 12, "ТЕРРАРИУМ", false);
            Aviary(new Vector3(30, 0, 38));
            OldEnclosures(new Vector3(-32, 0, -34));
            Pavilion(new Vector3(20, 0, -34), 17, 13, "КАФЕ • САФАРИ", true);
            ServiceYard(new Vector3(58, 0, -34));
            Fountain(); BattleCover(); Furnishings(); Vegetation(); PeripheralHabitats();
            Physics.SyncTransforms();
            GrassArt.Build(geometry, lawnGround);
            DefinePoints();
            if (Application.isPlaying) AmbientSound.Create(transform);
            MenuCameraAnchor = WorldArt.Group(transform, "Menu overview", new Vector3(-17, 8.4f, -17));
            MenuCameraAnchor.LookAt(new Vector3(3, 1.7f, 4));
            PreviewAnchor = WorldArt.Group(transform, "Armory studio", new Vector3(250, 20, 250));
            AddPreviewLight(new Vector3(-1.7f, 1.8f, -.7f), new Color(.78f, .88f, 1), 4);
            AddPreviewLight(new Vector3(1.1f, .8f, 1.2f), new Color(1, .77f, .51f), 3);
            Navigation = geometry.gameObject.AddComponent<NavMeshSurface>();
            Navigation.collectObjects = CollectObjects.Children;
            Navigation.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            Navigation.overrideTileSize = true; Navigation.tileSize = 128;
            Navigation.overrideVoxelSize = true; Navigation.voxelSize = .18f;
            Physics.SyncTransforms(); Navigation.BuildNavMesh();
            ProjectPointsToNavigation(TeamASpawns); ProjectPointsToNavigation(TeamBSpawns);
            ProjectPointsToNavigation(DeathmatchSpawns); ProjectPointsToNavigation(LootPoints);
            AmbientZoo.Create(transform);
            // Colliders stay separate for penetration. Batch inexpensive architecture; shared high-vertex
            // foliage/hills remain instancing-eligible instead of duplicating each sphere into a static batch.
            if (Application.isPlaying)
            {
                var batch = new List<GameObject>();
                foreach (var filter in geometry.GetComponentsInChildren<MeshFilter>())
                {
                    if (!filter.sharedMesh || filter.sharedMesh.vertexCount > 128 || filter.GetComponent<TextMesh>()) continue;
                    filter.gameObject.isStatic = true; batch.Add(filter.gameObject);
                }
                StaticBatchingUtility.Combine(batch.ToArray(), geometry.gameObject);
            }
        }

        void Materials()
        {
            concrete = WorldArt.SurfaceMat("Weathered limestone concrete", new Color(.63f, .63f, .58f), smoothness: .13f, albedoResource: "Textures/weathered-concrete");
            pale = WorldArt.SurfaceMat("Faded ivory paint", new Color(.72f, .70f, .62f), smoothness: .12f, albedoResource: "Textures/weathered-concrete");
            darkConcrete = WorldArt.SurfaceMat("Dark concrete", new Color(.38f, .42f, .41f), smoothness: .1f, albedoResource: "Textures/weathered-concrete");
            stone = WorldArt.SurfaceMat("Fountain dark slate", new Color(.30f, .36f, .35f), .03f, .31f);
            asphalt = WorldArt.SurfaceMat("Cracked walking path", new Color(.51f, .54f, .55f), smoothness: .07f, tileMeters: 3, albedoResource: "Textures/worn-asphalt");
            soil = WorldArt.SurfaceMat("Dry soil", new Color(.48f, .44f, .36f), smoothness: .04f, tileMeters: 4, albedoResource: "Textures/dry-park-ground");
            grass = WorldArt.SurfaceMat("Uncut zoo lawn", new Color(.44f, .51f, .31f), smoothness: .04f, tileMeters: 4, albedoResource: "Textures/dry-park-ground");
            wood = WorldArt.SurfaceMat("Sun bleached timber", new Color(.57f, .51f, .41f), smoothness: .14f, tileMeters: 3, albedoResource: "Textures/weathered-timber");
            rust = WorldArt.SurfaceMat("Oxidized steel", new Color(.39f, .25f, .16f), .43f);
            metal = WorldArt.SurfaceMat("Zoo green railings", new Color(.15f, .21f, .16f), .5f, .2f);
            glass = WorldArt.SurfaceMat("Thick terrarium glass", new Color(.25f, .37f, .34f), .36f, .68f);
            leaf = WorldArt.SurfaceMat("Mature foliage", new Color(.21f, .31f, .14f));
            leafLight = WorldArt.SurfaceMat("Foliage sunlight", new Color(.36f, .43f, .20f));
            yellow = WorldArt.SurfaceMat("Zoo ochre sign paint", new Color(.69f, .51f, .23f));
            water = WorldArt.SurfaceMat("Still fountain water", new Color(.16f, .29f, .27f), .3f, .86f);
        }

        void Lighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.40f, .48f, .57f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .35f, .36f);
            RenderSettings.ambientGroundColor = new Color(.15f, .16f, .14f);
            RenderSettings.ambientIntensity = .8f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.42f, .48f, .52f); RenderSettings.fogStartDistance = 110; RenderSettings.fogEndDistance = 260;
            var sunObject = new GameObject("Late afternoon sun"); sunObject.transform.SetParent(transform, false); sunObject.transform.rotation = Quaternion.Euler(32, -38, 0);
            var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = new Color(1, .86f, .70f); sun.intensity = .95f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .88f; sun.shadowBias = .018f; sun.shadowNormalBias = .22f; RenderSettings.sun = sun;
            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                var sky = new Material(shader) { name = "ZoneZ evening sky" };
                sky.SetColor("_SkyTint", new Color(.43f, .49f, .57f)); sky.SetColor("_GroundColor", new Color(.25f, .27f, .25f)); sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetFloat("_Exposure", .78f);
                RenderSettings.skybox = sky;
            }
            QualitySettings.shadowDistance = 70;
            WorldLook.Create(transform);
        }

        GameObject Box(string name, Vector3 p, Vector3 size, Material mat, string material = "concrete", Vector3 angle = default, Transform parent = null)
            => WorldArt.Box(parent != null ? parent : geometry, name, p, size, mat, true, material, angle);

        void Ground()
        {
            // The soil ends at the lawn's underside. Two top faces at y=0 caused
            // depth fighting across the entire field when the camera moved.
            Box("Whole playable ground", new Vector3(0, -.385f, 0), new Vector3(180, .63f, 160), soil, "ground");
            lawnGround = Box("Zoo grass field", new Vector3(0, -.035f, 0), new Vector3(158, .07f, 138), grass, "ground").GetComponent<Collider>();
            for (int i = 0; i < 18; i++)
            {
                float angle = i * Mathf.PI * 2 / 18;
                var p = new Vector3(Mathf.Cos(angle) * 142, -4, Mathf.Sin(angle) * 122);
                var mound = WorldArt.Sphere(geometry, "Distant wooded hill", p, new Vector3(65 + Next(0, 25), 24 + Next(0, 18), 55), i % 2 == 0 ? grass : soil);
                mound.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void Boundaries()
        {
            Box("West perimeter wall", new Vector3(-80, 2.4f, 0), new Vector3(1.1f, 4.8f, 140), concrete);
            Box("East perimeter wall", new Vector3(80, 2.4f, 0), new Vector3(1.1f, 4.8f, 140), concrete);
            Box("North perimeter wall", new Vector3(0, 2.4f, 70), new Vector3(160, 4.8f, 1.1f), concrete);
            Box("South perimeter wall", new Vector3(0, 2.4f, -70), new Vector3(160, 4.8f, 1.1f), concrete);
            for (int x = -78; x <= 78; x += 12) for (int side = -1; side <= 1; side += 2)
            {
                Box("Boundary pier", new Vector3(x, 2.65f, side * 69.3f), new Vector3(1.45f, 5.3f, 1.6f), darkConcrete);
                Box("Boundary cap", new Vector3(x, 5.34f, side * 69.3f), new Vector3(1.7f, .16f, 1.8f), pale);
            }
            for (int z = -60; z <= 60; z += 12) for (int side = -1; side <= 1; side += 2)
                Box("Side pier", new Vector3(side * 79.3f, 2.65f, z), new Vector3(1.6f, 5.3f, 1.45f), darkConcrete);
        }

        void Path(Vector3 center, Vector3 size, float angle = 0)
        {
            pavedRoutes.Add(center, size, angle);
        }

        void Paths()
        {
            Path(Vector3.zero, new Vector3(146, 0, 13));
            Path(new Vector3(0, 0, 38), new Vector3(142, 0, 8));
            Path(new Vector3(0, 0, -34), new Vector3(142, 0, 9));
            Path(new Vector3(-58, 0, 1), new Vector3(8, 0, 83));
            Path(new Vector3(58, 0, 1), new Vector3(8, 0, 83));
            Path(new Vector3(-18, 0, 19), new Vector3(6, 0, 39), -14);
            Path(new Vector3(40, 0, 17), new Vector3(6, 0, 37), 12);
            Path(new Vector3(-43, 0, -15), new Vector3(6, 0, 31), -12);
            Path(new Vector3(8, 0, -17), new Vector3(7, 0, 36), 10);
            Path(new Vector3(0, 0, 55), new Vector3(58, 0, 6));
            Path(new Vector3(0, 0, -55), new Vector3(56, 0, 6));
            pavedRoutes.Build(geometry, asphalt);
            for (int x = -66; x <= 66; x += 12) for (int side = -1; side <= 1; side += 2)
            {
                if (Mathf.Abs(x) < 11) continue;
                Box("Path edge stone", new Vector3(x, .11f, side * 6.6f), new Vector3(5.6f, .22f, .23f), pale, "stone");
            }
        }

        void Entrance(Vector3 p, float yaw, string title, bool service)
        {
            var root = WorldArt.Group(geometry, title, p, new Vector3(0, yaw, 0));
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Entrance pier", new Vector3(side * 5.8f, 2.8f, 0), new Vector3(1.3f, 5.6f, 1.3f), concrete, "concrete", parent: root);
                Box("Gatehouse", new Vector3(side * 9, 1.7f, -2), new Vector3(4.8f, 3.4f, 4.1f), pale, "concrete", parent: root);
                Box("Ticket window", new Vector3(side * 9, 1.9f, .064f), new Vector3(2.2f, 1.1f, .08f), glass, "glass", parent: root);
                Box("Gatehouse roof", new Vector3(side * 9, 3.5f, -2), new Vector3(5.3f, .25f, 4.6f), darkConcrete, "concrete", parent: root);
            }
            Box("Zoo entrance arch", new Vector3(0, 5.7f, 0), new Vector3(13, 1.1f, .85f), service ? metal : yellow, service ? "metal" : "concrete", parent: root);
            WorldArt.Label(root, service ? "SERVICE / СЛУЖЕБНЫЙ" : "ЗООПАРК • ПОСЛЕДНЯЯ СМЕНА", new Vector3(0, 5.68f, .441f), .15f, new Color(.90f, .88f, .73f));
            for (int side = -1; side <= 1; side += 2) Fence(root, new Vector3(side * 10, 1.2f, 7), 7, 2.4f, 0);
        }

        void Pavilion(Vector3 p, float width, float depth, string title, bool cafe)
        {
            var root = WorldArt.Group(geometry, title, p);
            Box("Pavilion slab", new Vector3(0, .08f, 0), new Vector3(width, .16f, depth), pale, "concrete", parent: root);
            float door = 4.2f; float segment = (width - door) / 2;
            for (int zSide = -1; zSide <= 1; zSide += 2)
            {
                for (int side = -1; side <= 1; side += 2)
                    Box("Broken front wall wing", new Vector3(side * (door / 2 + segment / 2), 1.65f, zSide * depth * .5f), new Vector3(segment, 3.3f, .4f), cafe ? pale : concrete, "concrete", parent: root);
                Box("Door lintel", new Vector3(0, 3.5f, zSide * depth * .5f), new Vector3(width + .35f, .45f, .7f), darkConcrete, "concrete", parent: root);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Side wall lower", new Vector3(side * width / 2, .65f, 0), new Vector3(.4f, 1.3f, depth), concrete, "concrete", parent: root);
                Box("Thick side glass", new Vector3(side * width / 2, 2.15f, 0), new Vector3(.18f, 1.7f, depth - .6f), glass, "glass", parent: root);
                for (int z = -2; z <= 2; z++) Box("Window mullion", new Vector3(side * width / 2, 2.2f, z * (depth - .5f) / 5), new Vector3(.32f, 2.2f, .16f), metal, "metal", parent: root);
            }
            // Broken roof leaves daylight and sightlines through the doorway while retaining a recognizable pavilion silhouette.
            Box("Surviving roof west", new Vector3(-width * .31f, 3.89f, 0), new Vector3(width * .39f, .25f, depth + 1), darkConcrete, "concrete", parent: root);
            Box("Surviving roof east", new Vector3(width * .35f, 3.92f, 0), new Vector3(width * .29f, .22f, depth + 1), darkConcrete, "concrete", parent: root);
            Box("Signboard", new Vector3(0, 3.65f, -depth * .5f - .40f), new Vector3(width * .72f, .75f, .12f), cafe ? wood : yellow, cafe ? "wood" : "metal", parent: root);
            WorldArt.Label(root, title, new Vector3(0, 3.66f, -depth * .5f - .475f), .20f, new Color(.89f, .87f, .70f), new Vector3(0, 180, 0));
            if (cafe)
            {
                Box("Timber cafe counter", new Vector3(-4.5f, .65f, .8f), new Vector3(1.15f, 1.3f, 6), wood, "wood", parent: root);
                Box("Counter top", new Vector3(-4.5f, 1.33f, .8f), new Vector3(1.45f, .12f, 6.4f), pale, "stone", parent: root);
                for (int i = 0; i < 3; i++) CafeTable(root, new Vector3(3.6f, .18f, -3.5f + i * 3.2f));
                var awning = WorldArt.SurfaceMat("Faded cafe canvas", new Color(.41f, .45f, .27f));
                for (int i = 0; i < 5; i++) WorldArt.Box(root, "Terrace canvas strip", new Vector3(-5.5f + i * 2.6f, 3.1f, -depth / 2 - 2), new Vector3(2.5f, .08f, 3.4f), i % 2 == 0 ? awning : pale, true, "fabric", new Vector3(9, 0, 0));
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Box("Exhibit rock base", new Vector3(side * 5.9f, .45f, .7f), new Vector3(3.3f, .75f, 6), darkConcrete, "stone", parent: root);
                    Box("Exhibit glass front", new Vector3(side * 4.2f, 1.56f, .7f), new Vector3(.12f, 1.4f, 6), glass, "glass", parent: root);
                    for (int j = 0; j < 3; j++) WorldArt.Sphere(root, "Terrarium boulder", new Vector3(side * 5.8f, 1.02f, -1.2f + j * 1.8f), new Vector3(1.3f, .65f, .9f), stone, true);
                }
                WorldArt.Label(root, "REPTILES / РЕПТИЛИИ", new Vector3(0, 2.8f, depth / 2 - .3f), .12f, Color.black);
            }
        }

        void Aviary(Vector3 p)
        {
            var root = WorldArt.Group(geometry, "Abandoned aviary", p);
            Box("Aviary stone plinth", new Vector3(0, .16f, 0), new Vector3(17, .32f, 12), concrete, "stone", parent: root);
            for (int side = -1; side <= 1; side += 2)
            {
                Fence(root, new Vector3(side * 8, 1.8f, 0), 12, 3.6f, 90);
                Fence(root, new Vector3(side * 5.3f, 1.8f, 6), 5.3f, 3.6f, 0);
                Fence(root, new Vector3(side * 5.3f, 1.8f, -6), 5.3f, 3.6f, 0);
            }
            for (int x = -8; x <= 8; x += 4)
            {
                WorldArt.Beam(root, "Aviary roof rib", new Vector3(x, 3.7f, -6), new Vector3(x, 5.0f, 0), .10f, rust, true, "metal");
                WorldArt.Beam(root, "Aviary roof rib", new Vector3(x, 5, 0), new Vector3(x, 3.7f, 6), .10f, rust, true, "metal");
            }
            Box("Aviary central perch", new Vector3(0, 1.0f, .5f), new Vector3(4, 2, 2.2f), wood, "wood", parent: root);
            Box("Aviary label", new Vector3(0, 4.3f, -6.05f), new Vector3(6, .7f, .16f), yellow, "metal", parent: root);
            WorldArt.Label(root, "ПТИЧНИК", new Vector3(0, 4.29f, -6.15f), .20f, new Color(.17f, .22f, .15f), new Vector3(0, 180, 0));
            for (int i = 0; i < 3; i++) WorldArt.Beam(root, "Weathered bird branch", new Vector3(-3, 1.8f + i * .3f, -2 + i * 2), new Vector3(4, 2.2f + i * .3f, -1 + i * 2), .13f, wood, true, "wood");
        }

        void OldEnclosures(Vector3 p)
        {
            var root = WorldArt.Group(geometry, "Old mammal enclosures", p);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Low enclosure boundary", new Vector3(side * 7, .62f, 0), new Vector3(.7f, 1.24f, 17), stone, "stone", parent: root);
                Fence(root, new Vector3(side * 7, 2.1f, 0), 17, 1.8f, 90);
                Box("North enclosure corner", new Vector3(side * 4.8f, 1.35f, 8.5f), new Vector3(4.8f, 2.7f, .55f), concrete, "concrete", parent: root);
                Box("South enclosure corner", new Vector3(side * 4.8f, .65f, -8.5f), new Vector3(4.8f, 1.3f, .6f), concrete, "concrete", parent: root);
            }
            Box("Animal shelter left wall", new Vector3(-3, 1.45f, 2), new Vector3(.28f, 2.9f, 4), wood, "wood", parent: root);
            Box("Animal shelter back wall", new Vector3(0, 1.45f, 4), new Vector3(6, 2.9f, .3f), wood, "wood", parent: root);
            Box("Animal shelter roof", new Vector3(0, 3, 2), new Vector3(6.6f, .2f, 4.6f), wood, "wood", new Vector3(0, 0, -4), root);
            Crate(root, new Vector3(4, .1f, -3), 1.4f, 9);
            Box("Old enclosure sign", new Vector3(0, 2.8f, -8.7f), new Vector3(4.6f, .65f, .13f), wood, "wood", parent: root);
            WorldArt.Label(root, "СТАРЫЕ ВОЛЬЕРЫ", new Vector3(0, 2.8f, -8.78f), .13f, new Color(.83f, .8f, .59f), new Vector3(0, 180, 0));
        }

        void ServiceYard(Vector3 p)
        {
            var root = WorldArt.Group(geometry, "Service yard", p);
            Box("Service asphalt", new Vector3(0, .04f, 0), new Vector3(26, .09f, 24), darkConcrete, "concrete", parent: root);
            Container(root, new Vector3(7, 0, -5), 12, new Color(.33f, .36f, .27f));
            Container(root, new Vector3(-7, 0, 7), 7, new Color(.47f, .28f, .17f));
            for (int i = 0; i < 4; i++) Crate(root, new Vector3(-7 + i * 2.5f, 0, -6), 1.5f, i * 8 - 8);
            for (int i = 0; i < 3; i++) BarrelProp(root, new Vector3(1 + i * 1.1f, 0, 6));
            Box("Loading barrier", new Vector3(0, .6f, 10), new Vector3(8, 1.2f, .7f), concrete, "concrete", parent: root);
            WorldArt.Label(root, "СЛУЖЕБНЫЙ ДВОР", new Vector3(7, 2.4f, -11.1f), .14f, new Color(.82f, .78f, .59f), new Vector3(0, 180, 0));
        }

        void Container(Transform parent, Vector3 p, float length, Color color)
        {
            var root = WorldArt.Group(parent, "Shipping container", p);
            var paint = WorldArt.SurfaceMat("Container " + color, color, .4f);
            Box("Container solid shell", new Vector3(0, 1.35f, 0), new Vector3(2.8f, 2.7f, length), paint, "metal", parent: root);
            for (int i = 0; i <= (int)(length / .5f); i++) for (int side = -1; side <= 1; side += 2)
                WorldArt.Box(root, "Container corrugation", new Vector3(side * 1.417f, 1.35f, -length / 2 + i * .5f), new Vector3(.04f, 2.55f, .065f), paint);
            for (int side = -1; side <= 1; side += 2)
                WorldArt.Box(root, "Container door lock", new Vector3(side * .75f, 1.3f, -length / 2 - .025f), new Vector3(.045f, 2.3f, .06f), rust);
        }

        void Fountain()
        {
            var root = WorldArt.Group(geometry, "Central dry fountain");
            WorldArt.Cylinder(root, "Fountain ground ring", new Vector3(0, .08f, 0), 6.3f, .16f, pale, true, "stone");
            WorldArt.Cylinder(root, "Fountain basin body", new Vector3(0, .5f, 0), 4.45f, .90f, stone, true, "stone");
            WorldArt.Cylinder(root, "Stagnant shallow water", new Vector3(0, .966f, 0), 4.15f, .025f, water);
            for (int i = 0; i < 20; i++)
            {
                float angle = i * Mathf.PI * 2 / 20;
                Box("Basin rim segment", new Vector3(Mathf.Sin(angle) * 4.3f, 1.03f, Mathf.Cos(angle) * 4.3f), new Vector3(1.39f, .26f, .46f), pale, "stone", new Vector3(0, angle * Mathf.Rad2Deg, 0), root);
            }
            WorldArt.Cylinder(root, "Statue plinth", new Vector3(0, 1.9f, 0), .82f, 1.9f, pale, true, "stone");
            // Abstract original heron sculpture; unmistakable zoo landmark and solid central cover.
            WorldArt.Sphere(root, "Heron sculpted body", new Vector3(0, 3.23f, 0), new Vector3(1.20f, .82f, .72f), stone, true);
            WorldArt.Beam(root, "Heron sculpted neck", new Vector3(.30f, 3.3f, 0), new Vector3(.46f, 4.50f, 0), .23f, stone, true, "stone");
            WorldArt.Sphere(root, "Heron head", new Vector3(.47f, 4.5f, 0), new Vector3(.43f, .32f, .33f), stone, true);
            WorldArt.Taper(root, "Heron beak", new Vector3(.47f, 4.51f, .42f), new Vector3(.20f, .16f, 0), new Vector3(.015f, .015f, 0), .68f, stone);
            WorldArt.Beam(root, "Heron leg A", new Vector3(-.23f, 2.83f, 0), new Vector3(-.23f, 3.12f, 0), .10f, stone, true, "stone");
            WorldArt.Beam(root, "Heron leg B", new Vector3(.23f, 2.83f, 0), new Vector3(.23f, 3.12f, 0), .10f, stone, true, "stone");
            WorldArt.Label(root, "ПОСЛЕДНЯЯ СМЕНА\nЗООПАРК 1978", new Vector3(0, 1.72f, -.825f), .10f, new Color(.18f, .23f, .20f), new Vector3(0, 180, 0));
        }

        void BattleCover()
        {
            // Entrance information walls create an open dogleg, shielding every opposing start.
            for (int side = -1; side <= 1; side += 2)
            {
                Wall(new Vector3(side * 61, 0, 0), 6, 2.5f, 90);
                var board = WorldArt.Group(geometry, "Zoo route information", new Vector3(side * 61.42f, 1.5f, 0), new Vector3(0, side * 90, 0));
                WorldArt.Box(board, "Visitor map backing", Vector3.zero, new Vector3(3.2f, 1.65f, .08f), yellow, true, "metal");
                WorldArt.Label(board, "ПОСЛЕДНЯЯ СМЕНА\nТЕРРАРИУМ  /  ФОНТАН  /  КАФЕ", new Vector3(0, 0, -.045f), .072f, new Color(.18f, .22f, .14f), new Vector3(0, 180, 0));
            }
            // Staggered walls interrupt the main lane without sealing any of the three routes.
            Wall(new Vector3(-37, 0, 4), 12, 1.8f, -14);
            Wall(new Vector3(37, 0, -4), 12, 1.8f, 14);
            Wall(new Vector3(-17, 0, 13), 13, 2.6f, 90);
            Wall(new Vector3(17, 0, -13), 13, 2.6f, 90);
            Wall(new Vector3(-45, 0, 22), 13, 2.5f, -18);
            Wall(new Vector3(48, 0, -19), 9, 1.7f, 25);
            Wall(new Vector3(20, 0, 23), 12, 2.3f, 7);
            Wall(new Vector3(-16, 0, -22), 12, 2.0f, -9);
            Wall(new Vector3(-28, 0, 55), 8, 2.3f, 90);
            Wall(new Vector3(28, 0, 55), 8, 2.3f, 90);
            Wall(new Vector3(-26, 0, -55), 8, 2.3f, 90);
            Wall(new Vector3(28, 0, -55), 8, 2.3f, 90);
            var crates = new[] { new Vector3(-49,0,-4),new Vector3(-26,0,-4),new Vector3(24,0,5),new Vector3(48,0,5),new Vector3(-9,0,28),new Vector3(10,0,45),new Vector3(3,0,-40),new Vector3(-51,0,-49),new Vector3(46,0,44),new Vector3(-8,0,-8),new Vector3(9,0,9) };
            for (int i = 0; i < crates.Length; i++) { Crate(geometry, crates[i], i % 3 == 0 ? 1.85f : 1.4f, i * 13); if (i % 4 == 0) Crate(geometry, crates[i] + new Vector3(.2f, 1.85f, 0), 1.1f, i * 8); }
            Planter(new Vector3(-17, 0, 4), 5); Planter(new Vector3(16, 0, -5), 5);
            Planter(new Vector3(-6, 0, 17), 7); Planter(new Vector3(6, 0, -17), 7);
            Planter(new Vector3(47, 0, 27), 4); Planter(new Vector3(-50, 0, -27), 4);
        }

        void Wall(Vector3 p, float length, float height, float yaw)
        {
            var root = WorldArt.Group(geometry, "Broken dividing wall", p, new Vector3(0, yaw, 0));
            Box("Solid wall", new Vector3(0, height / 2, 0), new Vector3(length, height, .7f), concrete, "concrete", parent: root);
            Box("Weathered wall cap", new Vector3(0, height + .06f, 0), new Vector3(length + .14f, .16f, .91f), pale, "stone", parent: root);
            for (int side = -1; side <= 1; side += 2) Box("Wall end pillar", new Vector3(side * length / 2, height * .55f, 0), new Vector3(.95f, height * 1.1f, .99f), darkConcrete, "concrete", parent: root);
            // Thin raised surface details add damp bases and runoff below the cap without changing cover.
            for (int side = -1; side <= 1; side += 2)
            {
                WorldArt.Box(root, "Damp wall footing", new Vector3(0, .13f, side * .356f), new Vector3(length - .25f, .26f, .012f), darkConcrete);
                for (int i = 0; i < 4; i++)
                {
                    float x = -length * .38f + i * length * .25f;
                    float streak = height * (.19f + .055f * (i % 3));
                    WorldArt.Box(root, "Rain runoff on masonry", new Vector3(x, height - streak * .5f, side * .357f), new Vector3(.08f + .04f * (i % 2), streak, .014f), darkConcrete);
                }
            }
        }

        void Planter(Vector3 p, float length)
        {
            var root = WorldArt.Group(geometry, "Overgrown solid planter", p);
            Box("Planter masonry", new Vector3(0, .62f, 0), new Vector3(length, 1.24f, 1.8f), stone, "stone", parent: root);
            Box("Planter soil", new Vector3(0, 1.245f, 0), new Vector3(length - .2f, .05f, 1.6f), soil, "ground", parent: root);
            for (int i = 0; i < 4; i++)
            {
                var position = new Vector3((i - 1.5f) * length / 4, 1.50f, 0);
                var size = new Vector3(length / 3.6f, .65f, 1.46f);
                if (!FoliageArt.Shrub(root, position, size, i)) WorldArt.Sphere(root, "Planter shrubs", position, size, leaf);
            }
        }

        void Crate(Transform parent, Vector3 p, float size, float yaw)
        {
            var root = WorldArt.Group(parent, "Penetrable timber crate", p, new Vector3(0, yaw, 0));
            var body = Box("Timber crate body", new Vector3(0, size / 2, 0), Vector3.one * size, wood, "wood", parent: root);
            var tag = body.GetComponent<SurfaceTag>(); tag.logicalGroup = root.gameObject;
            for (int face = -1; face <= 1; face += 2)
            {
                for (int plank = 0; plank < 5; plank++) WorldArt.Box(root, "Crate plank seam", new Vector3(0, .02f + plank * size / 5, face * (size * .5f + .008f)), new Vector3(size - .04f, .012f, .01f), darkConcrete);
                WorldArt.Box(root, "Crate diagonal brace", new Vector3(0, size / 2, face * (size / 2 + .035f)), new Vector3(size * 1.25f, .11f, .06f), wood, false, "wood", new Vector3(0, 0, 43));
            }
            WorldArt.Box(root, "Crate top batten", new Vector3(0, size + .026f, 0), new Vector3(size, .05f, .13f), wood);
        }

        void Fence(Transform parent, Vector3 p, float length, float height, float yaw)
        {
            var root = WorldArt.Group(parent, "Open metal fence", p, new Vector3(0, yaw, 0));
            for (float x = -length / 2; x <= length / 2 + .01f; x += .44f)
                Box("Fence upright", new Vector3(x, 0, 0), new Vector3(.047f, height, .047f), metal, "metal", parent: root);
            Box("Fence top rail", new Vector3(0, height * .44f, 0), new Vector3(length, .08f, .08f), rust, "metal", parent: root);
            Box("Fence lower rail", new Vector3(0, -height * .35f, 0), new Vector3(length, .06f, .07f), metal, "metal", parent: root);
        }

        void Furnishings()
        {
            for (int x = -48; x <= 48; x += 24)
            {
                Bench(new Vector3(x, 0, 9), 180); Bench(new Vector3(x + 7, 0, -9), 0);
                Lamp(new Vector3(x - 5, 0, 11)); Lamp(new Vector3(x + 12, 0, -11));
            }
            SignPost(new Vector3(-10, 0, -10), "ТЕРРАРИУМ ←", "ПТИЧНИК →");
            SignPost(new Vector3(11, 0, 10), "← ВОЛЬЕРЫ", "КАФЕ →");
            SignPost(new Vector3(-55, 0, 20), "ЗООПАРК / ZOO", "ОТКРЫТ ДО 18:00");
            for (int i = 0; i < 10; i++)
            {
                float x = -56 + i * 12.4f; float z = i % 2 == 0 ? 11.9f : -12.2f;
                BarrelProp(geometry, new Vector3(x, 0, z));
            }
            // Low litter is purely decorative and never catches character feet.
            for (int i = 0; i < 44; i++)
            {
                float x = Next(-72, 72), z = Next(-61, 61);
                WorldArt.Box(geometry, "Fallen paper", new Vector3(x, .065f, z), new Vector3(.28f, .005f, .39f), i % 2 == 0 ? pale : yellow, false, "paper", new Vector3(0, Next(0, 360), 0));
            }
        }

        void Bench(Vector3 p, float yaw)
        {
            var root = WorldArt.Group(geometry, "Old wooden bench", p, new Vector3(0, yaw, 0));
            for (int i = 0; i < 4; i++) Box("Wooden seat slat", new Vector3(0, .48f, -.21f + i * .14f), new Vector3(2.3f, .085f, .12f), wood, "wood", parent: root);
            for (int i = 0; i < 3; i++) Box("Wooden back slat", new Vector3(0, .78f + i * .14f, .26f), new Vector3(2.3f, .11f, .07f), wood, "wood", new Vector3(-7, 0, 0), root);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Iron bench leg", new Vector3(side * .82f, .24f, 0), new Vector3(.09f, .48f, .48f), metal, "metal", parent: root);
                Box("Back iron support", new Vector3(side * .82f, .70f, .32f), new Vector3(.07f, .74f, .07f), metal, "metal", parent: root);
            }
            foreach (var surface in root.GetComponentsInChildren<SurfaceTag>())
                if (surface.MaterialId == "wood") surface.logicalGroup = root.gameObject;
        }

        void CafeTable(Transform parent, Vector3 p)
        {
            var root = WorldArt.Group(parent, "Cafe timber table", p);
            WorldArt.Cylinder(root, "Table top", new Vector3(0, .82f, 0), .8f, .12f, wood, true, "wood");
            WorldArt.Cylinder(root, "Table support", new Vector3(0, .39f, 0), .07f, .78f, metal, true, "metal");
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Cafe chair seat", new Vector3(side * 1.14f, .47f, 0), new Vector3(.53f, .09f, .56f), wood, "wood", parent: root);
                Box("Cafe chair back", new Vector3(side * 1.4f, .8f, 0), new Vector3(.08f, .66f, .54f), wood, "wood", parent: root);
                for (int z = -1; z <= 1; z += 2) Box("Chair metal leg", new Vector3(side * 1.14f, .23f, z * .19f), new Vector3(.04f, .46f, .045f), metal, "metal", parent: root);
            }
        }

        void BarrelProp(Transform parent, Vector3 p)
        {
            var root = WorldArt.Group(parent, "Old metal barrel", p);
            WorldArt.Cylinder(root, "Barrel body", new Vector3(0, .56f, 0), .36f, 1.12f, rust, true, "metal");
            for (int i = 0; i < 3; i++) WorldArt.Cylinder(root, "Barrel rim", new Vector3(0, .07f + i * .50f, 0), .377f, .045f, metal);
        }

        void Lamp(Vector3 p)
        {
            var root = WorldArt.Group(geometry, "Vintage park light", p);
            WorldArt.Cylinder(root, "Lamppost base", new Vector3(0, .17f, 0), .24f, .34f, concrete, true, "concrete");
            WorldArt.Cylinder(root, "Lamppost", new Vector3(0, 2.25f, 0), .065f, 4.2f, metal, true, "metal");
            WorldArt.Sphere(root, "Lamp globe", new Vector3(0, 4.4f, 0), new Vector3(.53f, .69f, .53f), pale, true, "glass");
            WorldArt.Cylinder(root, "Lamp shade", new Vector3(0, 4.77f, 0), .38f, .10f, metal, true, "metal");
        }

        void SignPost(Vector3 p, string first, string second)
        {
            var root = WorldArt.Group(geometry, "Zoo directions", p);
            Box("Timber sign post", new Vector3(0, 1.55f, 0), new Vector3(.17f, 3.1f, .17f), wood, "wood", parent: root);
            Box("Direction board one", new Vector3(0, 2.73f, 0), new Vector3(3.4f, .55f, .11f), wood, "wood", parent: root);
            Box("Direction board two", new Vector3(0, 2.03f, 0), new Vector3(3.4f, .55f, .11f), yellow, "wood", parent: root);
            WorldArt.Label(root, first, new Vector3(0, 2.73f, -.065f), .10f, new Color(.93f, .90f, .76f), new Vector3(0, 180, 0));
            WorldArt.Label(root, second, new Vector3(0, 2.03f, -.065f), .10f, new Color(.15f, .19f, .14f), new Vector3(0, 180, 0));
        }

        void Vegetation()
        {
            var positions = new[] { new Vector2(-70,57),new Vector2(-58,58),new Vector2(-45,61),new Vector2(-15,64),new Vector2(3,65),new Vector2(20,63),new Vector2(44,61),new Vector2(64,59),new Vector2(73,44),new Vector2(73,23),new Vector2(75,-21),new Vector2(71,-54),new Vector2(51,-61),new Vector2(21,-63),new Vector2(-6,-64),new Vector2(-23,-62),new Vector2(-47,-62),new Vector2(-70,-55),new Vector2(-72,-29),new Vector2(-73,25),new Vector2(-48,13),new Vector2(-29,18),new Vector2(-5,22),new Vector2(9,26),new Vector2(55,13),new Vector2(-60,-12),new Vector2(-28,-16),new Vector2(31,-19),new Vector2(48,-8) };
            foreach (var p in positions) Tree(new Vector3(p.x, 0, p.y), Next(5.4f, 8.2f));
            for (int i = 0; i < 60; i++)
            {
                float x = Next(-75, 75), z = (i % 2 == 0 ? 1 : -1) * Next(59, 66);
                var position = new Vector3(x, .45f, z);
                var size = new Vector3(Next(1.5f, 3), Next(.6f, 1.3f), Next(1.3f, 2.8f));
                if (!FoliageArt.Shrub(geometry, position, size, i)) WorldArt.Sphere(geometry, "Peripheral bramble", position, size, i % 3 == 0 ? leafLight : leaf);
            }
        }

        void Tree(Vector3 p, float height)
        {
            var root = WorldArt.Group(geometry, "Penetrable mature tree", p);
            bool texturedCrown = FoliageArt.Crown(root, height, Mathf.RoundToInt(p.x + p.z * 3));
            var trunk = WorldArt.Cylinder(root, "Wooden trunk", new Vector3(0, height * .36f, 0), .29f, height * .72f, wood, true, "wood");
            trunk.GetComponent<SurfaceTag>().logicalGroup = root.gameObject;
            for (int i = 0; i < 3; i++)
            {
                float angle = (i * 120 + Next(0, 30)) * Mathf.Deg2Rad;
                var end = new Vector3(Mathf.Sin(angle) * 1.5f, height * .77f, Mathf.Cos(angle) * 1.5f);
                var branch = WorldArt.Beam(root, "Solid timber branch", new Vector3(0, height * .49f, 0), end, .18f, wood, true, "wood");
                branch.GetComponent<SurfaceTag>().logicalGroup = root.gameObject;
                if (!texturedCrown) WorldArt.Sphere(root, "Tree canopy cluster", end + Vector3.up * .6f, new Vector3(3.5f, 2.5f, 3.4f), i == 0 ? leafLight : leaf);
            }
            if (!texturedCrown) WorldArt.Sphere(root, "Tree crown", new Vector3(0, height, 0), new Vector3(3.8f, 2.9f, 3.8f), leaf);
        }

        void DefinePoints()
        {
            TeamASpawns = new Vector3[12]; TeamBSpawns = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                TeamASpawns[i] = new Vector3(-70 + (i % 3) * 2, .25f, -3 + (i / 3) * 2);
                TeamBSpawns[i] = new Vector3(70 - (i % 3) * 2, .25f, -3 + (i / 3) * 2);
            }
            float[,] dm = { {-67,-10},{-67,10},{-57,35},{-48,49},{-34,49},{-20,30},{-5,49},{8,32},{27,49},{39,30},{54,48},{63,29},{67,10},{67,-10},{58,-26},{51,-47},{31,-43},{14,-26.6f},{0,-48},{-14,-29},{-29,-47},{-43,-29},{-59,-43},{-58,-20} };
            DeathmatchSpawns = new Vector3[24]; for (int i = 0; i < 24; i++) DeathmatchSpawns[i] = new Vector3(dm[i, 0], .25f, dm[i, 1]);
            float[,] loot = { {-47,30},{-24,42.5f},{3,40},{29,42},{50,22},{37,0},{46,-31},{18,-39},{-4,-36},{-32,-39},{-49,-19},{-29,0} };
            LootPoints = new Vector3[12]; for (int i = 0; i < 12; i++) LootPoints[i] = new Vector3(loot[i, 0], .25f, loot[i, 1]);
        }

        public Vector3 SpawnPoint(int team, int index, bool deathmatch)
        {
            var set = deathmatch ? DeathmatchSpawns : team == 0 ? TeamASpawns : TeamBSpawns;
            return set[(index % set.Length + set.Length) % set.Length];
        }

        void PeripheralHabitats()
        {
            // Small ground fauna now roam the public park; their former cages are gone.
            Habitat(new Vector3(55, 0, 61), 12, 8, "ГОЛУБИ");
            Habitat(new Vector3(-44, 0, 58), 12, 7, "ВОЛЬЕР ЗАКРЫТ");
        }

        void Habitat(Vector3 center, float width, float depth, string label)
        {
            var root = WorldArt.Group(geometry, label, center);
            for (int side = -1; side <= 1; side += 2)
            {
                Fence(root, new Vector3(0, 1.2f, side * depth / 2), width, 2.4f, 0);
                Fence(root, new Vector3(side * width / 2, 1.2f, 0), depth, 2.4f, 90);
            }
            Box("Habitat information panel",new Vector3(0,1.8f,-depth/2-.06f),new Vector3(3,.55f,.10f),yellow,"metal",parent:root);
            WorldArt.Label(root,label,new Vector3(0,1.8f,-depth/2-.12f),.085f,new Color(.15f,.20f,.13f),new Vector3(0,180,0));
        }

        void ProjectPointsToNavigation(Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++)
                if (NavMesh.SamplePosition(points[i], out var hit, .8f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y-points[i].y)<.6f)
                    points[i] = hit.position + Vector3.up * .08f;
                else Debug.LogError("Unreachable authored world point " + points[i], this);
        }

        public void ValidateNavigation(List<string> errors)
        {
            if (TeamASpawns.Length != 12 || TeamBSpawns.Length != 12 || DeathmatchSpawns.Length != 24 || LootPoints.Length != 12)
                errors.Add("World requires 12+12 team spawns, 24 DM spawns and 12 loot points.");
            var groups = new[] { TeamASpawns, TeamBSpawns, DeathmatchSpawns, LootPoints };
            var names = new[] { "Team A", "Team B", "DM", "Loot" };
            var path = new NavMeshPath();
            for (int group = 0; group < groups.Length; group++) for (int i = 0; i < groups[group].Length; i++)
            {
                var point = groups[group][i];
                string pointId = group == 2 ? "DM" + (i + 1).ToString("D2") : group == 3 ? "L" + (i + 1).ToString("D2") : names[group] + " " + (i + 1);
                if (!NavMesh.SamplePosition(point, out var nav, .35f, NavMesh.AllAreas))
                { errors.Add(pointId + " at " + point + " has no NavMesh."); continue; }
                if (!NavMesh.CalculatePath(TeamASpawns[0], nav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    errors.Add(pointId + " at " + point + " is disconnected from the main map.");
                var overlaps = Physics.OverlapCapsule(point + Vector3.up * .42f, point + Vector3.up * 1.45f, .36f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var overlap in overlaps) errors.Add(pointId + " at " + point + " intersects " + overlap.name);
                if (group < 3) for (int j = 0; j < i; j++)
                {
                    Vector3 separation = point - groups[group][j]; separation.y = 0;
                    if (separation.sqrMagnitude < .72f * .72f) errors.Add(pointId + " overlaps another spawn capsule.");
                }
            }
            foreach (var collider in geometry.GetComponentsInChildren<Collider>())
                if (!collider.isTrigger && !collider.GetComponent<SurfaceTag>()) errors.Add("Missing surface on " + collider.name);
            foreach (var a in TeamASpawns) foreach (var b in TeamBSpawns)
                if (!Physics.Linecast(a + Vector3.up * 1.62f, b + Vector3.up * 1.62f, ~0, QueryTriggerInteraction.Ignore))
                    errors.Add("Opposing team starts have a direct firing line.");
            // The screen ends and original entrance fences must leave usable central-lane exits.
            foreach (int side in new[] { -1, 1 }) foreach (int passage in new[] { -1, 1 })
            {
                var gate = new Vector3(side * 61, .12f, passage * 5);
                if (!NavMesh.SamplePosition(gate, out var hit, .4f, NavMesh.AllAreas)) errors.Add("Entrance dogleg has no NavMesh at " + gate);
                if (Physics.CheckCapsule(gate + Vector3.up * .42f, gate + Vector3.up * 1.45f, .36f, ~0, QueryTriggerInteraction.Ignore))
                    errors.Add("Entrance dogleg is blocked at " + gate);
            }
        }

        void AddPreviewLight(Vector3 position, Color color, float intensity)
        {
            var node = WorldArt.Group(PreviewAnchor, "Studio softbox", position); var light = node.gameObject.AddComponent<Light>(); light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = 5; light.shadows = LightShadows.None;
        }
        float Next(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
