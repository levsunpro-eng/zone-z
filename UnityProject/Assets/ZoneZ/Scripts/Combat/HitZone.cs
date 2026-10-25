using UnityEngine;

namespace ZoneZ
{
    public sealed class HitZone : MonoBehaviour
    {
        public Actor Owner;
        public string Group = "chest";
        public static HitZone AddBox(Transform parent, Actor owner, string group, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Hit_" + group); go.transform.SetParent(parent, false); go.transform.localPosition = center;
            var zone = go.AddComponent<HitZone>(); zone.Owner = owner; zone.Group = group;
            var collider = go.AddComponent<BoxCollider>(); collider.size = size; collider.isTrigger = true; return zone;
        }
        public static HitZone AddSphere(Transform parent, Actor owner, string group, Vector3 center, float radius)
        {
            var go = new GameObject("Hit_" + group); go.transform.SetParent(parent, false); go.transform.localPosition = center;
            var zone = go.AddComponent<HitZone>(); zone.Owner = owner; zone.Group = group;
            var collider = go.AddComponent<SphereCollider>(); collider.radius = radius; collider.isTrigger = true; return zone;
        }
    }
}
