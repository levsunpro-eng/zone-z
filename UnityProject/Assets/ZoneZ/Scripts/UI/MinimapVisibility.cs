using UnityEngine;

namespace ZoneZ
{
    /// <summary>Minimap contacts come from the local view, without shared spotting or reveal history.</summary>
    public static class MinimapVisibility
    {
        public static bool CanShow(Actor observer, Actor candidate, Camera viewCamera, bool alliesRequireVisibility = false)
        {
            if (!Live(observer) || !Live(candidate) || observer == candidate || !observer.Match || candidate.Match != observer.Match)
                return false;
            if (!observer.Match.IsEnemy(observer, candidate) && !alliesRequireVisibility) return true;
            return IsVisible(observer, candidate, viewCamera);
        }

        public static bool IsVisible(Actor observer, Actor candidate, Camera viewCamera)
        {
            if (!Live(observer) || !Live(candidate) || observer == candidate || !viewCamera || !viewCamera.isActiveAndEnabled)
                return false;

            // Use the actual stance-scaled hitboxes: standing-height guesses would reveal a
            // crouched actor behind a low obstacle. Side/top samples also allow real peeks.
            Transform hitboxes = candidate.Hitboxes ? candidate.Hitboxes : candidate.transform.Find("Hitboxes");
            if (hitboxes)
            {
                if (VisibleZone(observer, candidate, viewCamera, hitboxes.Find("Hit_head"))) return true;
                if (VisibleZone(observer, candidate, viewCamera, hitboxes.Find("Hit_chest"))) return true;
                return false;
            }

            // A presentation fixture or a future actor without standard hitbox names still
            // needs an actual trace hitting that actor before it can become a contact.
            float eyeHeight = candidate.EyePosition.y - candidate.transform.position.y;
            return VisibleSample(observer, candidate, viewCamera, candidate.EyePosition)
                || VisibleSample(observer, candidate, viewCamera, candidate.transform.position + Vector3.up * eyeHeight * .78f);
        }

        static bool Live(Actor actor) => actor && actor.Alive && actor.isActiveAndEnabled;

        static bool VisibleZone(Actor observer, Actor candidate, Camera viewCamera, Transform zone)
        {
            if (!zone || !zone.gameObject.activeInHierarchy) return false;
            var collider = zone.GetComponent<Collider>();
            if (!collider || !collider.enabled) return false;
            Bounds bounds = collider.bounds;
            Vector3 center = bounds.center;
            if (VisibleSample(observer, candidate, viewCamera, center)) return true;
            if (VisibleSample(observer, candidate, viewCamera, center + Vector3.up * bounds.extents.y * .65f)) return true;
            Vector3 side = viewCamera.transform.right;
            float width = Mathf.Abs(side.x) * bounds.extents.x + Mathf.Abs(side.y) * bounds.extents.y + Mathf.Abs(side.z) * bounds.extents.z;
            return VisibleSample(observer, candidate, viewCamera, center + side * width * .65f)
                || VisibleSample(observer, candidate, viewCamera, center - side * width * .65f);
        }

        static bool VisibleSample(Actor observer, Actor candidate, Camera viewCamera, Vector3 point)
        {
            Vector3 viewport = viewCamera.WorldToViewportPoint(point);
            if (viewport.z < viewCamera.nearClipPlane || viewport.z > viewCamera.farClipPlane
                || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return false;
            Vector3 origin = viewCamera.transform.position;
            Vector3 line = point - origin;
            // CombatTrace excludes the observer's movement/weapon/hitbox colliders and
            // checks solids containing the camera, as well as ordinary ray obstructions.
            // Minimap sight never penetrates wood, glass, or any other solid surface.
            var trace = CombatTrace.Cast(origin, line, line.magnitude + .02f, observer, false);
            return trace.Target == candidate;
        }
    }
}
