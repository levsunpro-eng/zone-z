using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public static partial class WeaponModelFactory
    {
        static readonly Dictionary<Vector3, Mesh> BeveledBlocks = new Dictionary<Vector3, Mesh>();

        // Unit bounds preserve existing sockets, animation pivots and children of magazines/pumps.
        // Only the large manufactured blocks use these small chamfers; fine details retain their shape.
        static Mesh BeveledBlock(Vector3 size)
        {
            if (BeveledBlocks.TryGetValue(size, out var cached) && cached) return cached;
            float bevel = Mathf.Min(.005f, Mathf.Min(size.x, size.y, size.z) * .18f);
            Vector3 half = size * .5f, inner = half - Vector3.one * bevel;
            var vertices = new List<Vector3>(96); var uv = new List<Vector2>(96); var triangles = new List<int>(132);

            void Face(Vector3[] points, Vector3 outward)
            {
                int first = vertices.Count;
                int normalAxis = Mathf.Abs(outward.x) >= Mathf.Abs(outward.y) && Mathf.Abs(outward.x) >= Mathf.Abs(outward.z) ? 0
                    : Mathf.Abs(outward.y) >= Mathf.Abs(outward.z) ? 1 : 2;
                foreach (var point in points)
                {
                    var unit = new Vector3(point.x / size.x, point.y / size.y, point.z / size.z);
                    vertices.Add(unit);
                    uv.Add(normalAxis == 0 ? new Vector2(unit.z + .5f, unit.y + .5f)
                        : normalAxis == 1 ? new Vector2(unit.x + .5f, unit.z + .5f) : new Vector2(unit.x + .5f, unit.y + .5f));
                }
                bool reverse = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), outward) < 0;
                for (int i = 1; i < points.Length - 1; i++)
                {
                    triangles.Add(first); triangles.Add(first + (reverse ? i + 1 : i)); triangles.Add(first + (reverse ? i : i + 1));
                }
            }

            for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                var points = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    points[i][axis] = sign * half[axis];
                    points[i][u] = (i == 0 || i == 3 ? -1 : 1) * inner[u];
                    points[i][v] = (i < 2 ? -1 : 1) * inner[v];
                }
                var normal = Vector3.zero; normal[axis] = sign; Face(points, normal);
            }
            for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
                for (int sa = -1; sa <= 1; sa += 2) for (int sb = -1; sb <= 1; sb += 2)
                {
                    int c = 3 - a - b;
                    var points = new Vector3[4];
                    points[0][a] = points[3][a] = sa * half[a]; points[0][b] = points[3][b] = sb * inner[b];
                    points[1][a] = points[2][a] = sa * inner[a]; points[1][b] = points[2][b] = sb * half[b];
                    points[0][c] = points[1][c] = -inner[c]; points[2][c] = points[3][c] = inner[c];
                    var normal = Vector3.zero; normal[a] = sa; normal[b] = sb; Face(points, normal);
                }
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                Face(new[] {
                    new Vector3(x * half.x, y * inner.y, z * inner.z),
                    new Vector3(x * inner.x, y * half.y, z * inner.z),
                    new Vector3(x * inner.x, y * inner.y, z * half.z)
                }, new Vector3(x, y, z));

            var mesh = new Mesh { name = "Chamfered weapon block" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); BeveledBlocks[size] = mesh; return mesh;
        }
    }
}
