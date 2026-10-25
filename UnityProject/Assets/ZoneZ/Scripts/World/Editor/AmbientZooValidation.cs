#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZoneZ.Editor
{
    /// <summary>Checks actual public-ground placement, complete routes and escape destinations.</summary>
    internal static class AmbientZooValidation
    {
        internal static void Run(ZooWorld world, List<string> errors)
        {
            int groundAnimals = 0, checkedRoutes = 0;
            var connection = new NavMeshPath();
            foreach (var animal in world.GetComponentsInChildren<AmbientZoo>())
            {
                if (!animal.IsGroundAnimal) continue;
                groundAnimals++;
                string label = animal.name + ": ";
                Require(animal.NavigationReady, label + "placement has no ground NavMesh.", errors);
                Require(Mathf.Abs(animal.RoamOrigin.z) < 54 && Mathf.Abs(animal.RoamOrigin.x) < 58,
                    label + "ground fauna must start on public paths/lawns outside the peripheral pens.", errors);
                Require(animal.GetComponentsInChildren<Collider>(true).Length == 0 &&
                    !animal.GetComponent<Actor>() && !animal.GetComponent<NavMeshAgent>(),
                    label + "ambient fauna must stay decorative and must not obstruct players.", errors);
                if (!animal.NavigationReady) continue;
                Require(NavMesh.CalculatePath(world.TeamASpawns[0], animal.RoamOrigin, NavMesh.AllAreas, connection) &&
                    connection.status == NavMeshPathStatus.PathComplete,
                    label + "public animal position is disconnected from player routes.", errors);
                Require(NavMesh.SamplePosition(animal.transform.position, out var spawn, .05f, NavMesh.AllAreas) &&
                    Mathf.Abs(spawn.position.y - animal.transform.position.y) <= .015f,
                    label + "ground animal must rest at the actual ground height.", errors);

                int reachable = 0;
                float greatestExtent = 0;
                for (int sample = 0; sample < 12; sample++)
                {
                    if (!animal.TryPreviewRoamingPath(sample, out var corners)) continue;
                    reachable++; checkedRoutes++;
                    ValidateRoute(corners, label + "roaming sample " + sample, errors);
                    greatestExtent = Mathf.Max(greatestExtent,
                        HorizontalDistance(animal.RoamOrigin, corners[corners.Length - 1]));
                }
                Require(reachable >= 8, label + "must have at least 8 reachable roaming routes across 12 deterministic probes.", errors);
                Require(greatestExtent >= animal.RoamRadius * .6f,
                    label + "roaming is still confined to a tiny pen-sized area.", errors);

                for (int direction = 0; direction < 4; direction++)
                {
                    float angle = direction * Mathf.PI * .5f;
                    var origin = animal.RoamOrigin + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 3;
                    bool escaping = animal.TryPreviewEscapePath(origin, out var corners);
                    Require(escaping, label + "no reachable escape from gunshot direction " + direction + ".", errors);
                    if (!escaping) continue;
                    checkedRoutes++;
                    ValidateRoute(corners, label + "escape " + direction, errors);
                    Require(HorizontalDistance(corners[corners.Length - 1], origin) >
                        HorizontalDistance(animal.RoamOrigin, origin) + .7f,
                        label + "escape destination must increase distance from the gunshot.", errors);
                }
            }
            Require(groundAnimals == 5, "Expected 5 free roaming ground animals (2 squirrels, 2 rats and 1 turtle).", errors);
            Debug.Log("ZONEZ_AMBIENT_ROUTE_CHECKS: " + groundAnimals + " free ground animals, " + checkedRoutes + " complete roaming/escape routes checked.");
        }

        static void ValidateRoute(Vector3[] corners, string label, List<string> errors)
        {
            for (int corner = 1; corner < corners.Length; corner++)
            {
                var from = corners[corner - 1];
                var to = corners[corner];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .25f));
                var previous = from;
                for (int step = 1; step <= steps; step++)
                {
                    var point = Vector3.Lerp(from, to, step / (float)steps);
                    if (!NavMesh.SamplePosition(point, out var ground, .2f, NavMesh.AllAreas) ||
                        NavMesh.Raycast(previous, ground.position, out var edge, NavMesh.AllAreas))
                    {
                        errors.Add(label + " crosses a navigation boundary on segment " + corner + ".");
                        break;
                    }
                    if (Mathf.Abs(ground.position.x) > 77 || Mathf.Abs(ground.position.z) > 67 ||
                        ground.position.y < -.2f || ground.position.y > .8f)
                    {
                        errors.Add(label + " leaves playable ground on segment " + corner + ".");
                        break;
                    }
                    previous = ground.position;
                }
            }
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }

        static void Require(bool valid, string error, List<string> errors)
        {
            if (!valid) errors.Add(error);
        }
    }
}
#endif
