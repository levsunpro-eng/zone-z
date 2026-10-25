using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public struct CombatTraceResult
    {
        public Actor Target;
        public HitZone Zone;
        public Collider Collider;
        public Vector3 Point;
        public float Distance, Retention;
        public bool Hit => Collider != null;
    }

    // Shared by bullets, electric discharge, knife sweeps and the ability laser.
    // The movement capsule is deliberately not a damage volume.
    public static class CombatTrace
    {
        public static CombatTraceResult Cast(Vector3 origin, Vector3 direction, float range, Actor owner, bool penetrateWood, float sweepRadius = 0)
        {
            direction = direction.normalized;
            range = Mathf.Max(0, range);
            var result = new CombatTraceResult { Point = origin + direction * range, Distance = range, Retention = 1 };
            if (direction.sqrMagnitude < .5f || range <= 0) return result;
            var crossed = new HashSet<int>();
            // Unity casts do not report a collider containing the ray origin. In particular,
            // this must never let a camera intersecting a solid wall shoot through it.
            var overlaps = Physics.OverlapSphere(origin, .002f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(overlaps, (a, b) => a.isTrigger.CompareTo(b.isTrigger));
            foreach (var collider in overlaps)
            {
                if ((collider.ClosestPoint(origin) - origin).sqrMagnitude > .000001f) continue;
                if (Consider(collider, origin, 0, owner, penetrateWood, crossed, ref result)) return result;
            }
            RaycastHit[] hits = sweepRadius > 0
                ? Physics.SphereCastAll(origin, sweepRadius, direction, range, ~0, QueryTriggerInteraction.Collide)
                : Physics.RaycastAll(origin, direction, range, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                // A melee sweep's radius must not extend its documented attack reach.
                float distance = sweepRadius > 0 ? Vector3.Distance(origin, hit.point) : hit.distance;
                if (distance > range + .0001f) continue;
                if (Consider(hit.collider, hit.point, distance, owner, penetrateWood, crossed, ref result)) return result;
            }
            return result;
        }

        static bool Consider(Collider collider, Vector3 point, float distance, Actor owner, bool penetrateWood, HashSet<int> crossed, ref CombatTraceResult result)
        {
            var zone = collider.GetComponent<HitZone>();
            var target = zone ? zone.Owner : collider.GetComponentInParent<Actor>();
            if (target)
            {
                if (target == owner || !target.Alive || !zone) return false;
                result.Target = target; result.Zone = zone;
            }
            else
            {
                if (collider.isTrigger) return false;
                var surface = collider.GetComponentInParent<SurfaceTag>();
                if (penetrateWood && surface && surface.Penetrable)
                {
                    if (crossed.Add(surface.ObstacleId)) result.Retention *= .75f;
                    return false;
                }
            }
            result.Collider = collider; result.Point = point; result.Distance = distance;
            return true;
        }
    }
}
