using System;
using UnityEngine;
using UnityEngine.AI;

namespace ZoneZ
{
    /// <summary>Static plan drawn from authored roads, walkable ground and architectural colliders.</summary>
    internal sealed class MinimapAtlas
    {
        public const int Width = 256, Height = 224;
        const float West = -82, East = 82, South = -72, North = 72;
        readonly Color32[] backdrop = new Color32[Width * Height], pixels = new Color32[Width * Height];
        public readonly Texture2D Texture;

        public MinimapAtlas(ZooWorld world)
        {
            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            { name = "Zoo tactical map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(18, 25, 27, 255);
            var navigation = NavMesh.CalculateTriangulation();
            for (int i = 0; i < navigation.indices.Length; i += 3)
            {
                var a = navigation.vertices[navigation.indices[i]];
                var b = navigation.vertices[navigation.indices[i + 1]];
                var c = navigation.vertices[navigation.indices[i + 2]];
                if (a.y > .8f || b.y > .8f || c.y > .8f || a.y < -.2f || b.y < -.2f || c.y < -.2f) continue;
                Triangle(Point(a), Point(b), Point(c), new Color32(53, 64, 64, 255));
            }
            var architecture = world.transform.Find("Static architecture");
            foreach (var filter in architecture.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "Visitor paved routes" || !filter.sharedMesh) continue;
                var mesh = filter.sharedMesh; var vertices = mesh.vertices; var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                    Triangle(Point(filter.transform.TransformPoint(vertices[triangles[i]])),
                        Point(filter.transform.TransformPoint(vertices[triangles[i + 1]])),
                        Point(filter.transform.TransformPoint(vertices[triangles[i + 2]])), new Color32(86, 100, 102, 255));
            }
            foreach (var collider in architecture.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || collider.bounds.max.y < .42f || collider.bounds.min.y > 2.7f) continue;
                var tag = collider.GetComponent<SurfaceTag>();
                if (!tag || tag.MaterialId == "ground") continue;
                var color = tag.MaterialId == "wood" ? new Color32(132, 115, 87, 255) :
                    tag.MaterialId == "glass" ? new Color32(92, 130, 134, 255) : new Color32(148, 155, 148, 255);
                if (collider is BoxCollider box)
                {
                    var size = box.size * .5f;
                    var a = Point(box.transform.TransformPoint(box.center + new Vector3(-size.x, 0, -size.z)));
                    var b = Point(box.transform.TransformPoint(box.center + new Vector3(size.x, 0, -size.z)));
                    var c = Point(box.transform.TransformPoint(box.center + new Vector3(size.x, 0, size.z)));
                    var d = Point(box.transform.TransformPoint(box.center + new Vector3(-size.x, 0, size.z)));
                    Triangle(a, b, c, color); Triangle(a, c, d, color);
                }
                else
                {
                    var center = Point(collider.bounds.center);
                    var extent = collider.bounds.extents;
                    Ellipse(center, Mathf.Max(1, extent.x / (East - West) * Width),
                        Mathf.Max(1, extent.z / (North - South) * Height), color);
                }
            }
            Array.Copy(pixels, backdrop, pixels.Length);
        }

        public static Vector2 NormalizedPoint(Vector3 world) => new Vector2(
            (world.x - West) / (East - West), (North - world.z) / (North - South));
        static Vector2 Point(Vector3 world) => new Vector2(
            (world.x - West) / (East - West) * (Width - 1), (world.z - South) / (North - South) * (Height - 1));

        public void BeginFrame() => Array.Copy(backdrop, pixels, pixels.Length);

        public void Marker(Vector3 position, MinimapMarkerKind kind, float yaw)
        {
            var point = Point(position);
            if (point.x < 3 || point.x > Width - 4 || point.y < 3 || point.y > Height - 4) return;
            Ellipse(point, kind == MinimapMarkerKind.Self ? 5.7f : 4.4f, kind == MinimapMarkerKind.Self ? 5.7f : 4.4f, new Color32(9, 14, 17, 255));
            if (kind == MinimapMarkerKind.Ally) Ellipse(point, 3.2f, 3.2f, new Color32(79, 191, 247, 255));
            else if (kind == MinimapMarkerKind.Enemy)
            {
                var color = new Color32(255, 93, 86, 255);
                Triangle(point + Vector2.up * 4, point + Vector2.left * 4, point + Vector2.down * 4, color);
                Triangle(point + Vector2.up * 4, point + Vector2.down * 4, point + Vector2.right * 4, color);
            }
            else
            {
                float angle = yaw * Mathf.Deg2Rad;
                var forward = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                var side = new Vector2(forward.y, -forward.x);
                Triangle(point + forward * 7, point - forward * 4 + side * 4,
                    point - forward * 4 - side * 4, new Color32(255, 222, 128, 255));
                Ellipse(point, 1.3f, 1.3f, new Color32(255, 251, 225, 255));
            }
        }

        public void Apply()
        {
            Texture.SetPixels32(pixels); Texture.Apply(false, false);
        }

        void Triangle(Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            float area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < .001f) return;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, Width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, Width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, Height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, Height - 1);
            float sign = Mathf.Sign(area);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                if (Cross(b - a, p - a) * sign >= -.01f && Cross(c - b, p - b) * sign >= -.01f && Cross(a - c, p - c) * sign >= -.01f)
                    pixels[y * Width + x] = color;
            }
        }

        void Ellipse(Vector2 center, float rx, float ry, Color32 color)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - rx)), x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(center.x + rx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - ry)), y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(center.y + ry));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                float dx = (x + .5f - center.x) / rx, dy = (y + .5f - center.y) / ry;
                if (dx * dx + dy * dy <= 1) pixels[y * Width + x] = color;
            }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
