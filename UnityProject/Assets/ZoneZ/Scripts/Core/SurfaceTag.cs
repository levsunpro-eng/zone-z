using UnityEngine;
namespace ZoneZ
{
    public sealed class SurfaceTag : MonoBehaviour
    {
        public string MaterialId = "concrete";
        public GameObject logicalGroup;
        public int ObstacleId => logicalGroup ? logicalGroup.GetInstanceID() : gameObject.GetInstanceID();
        public bool Penetrable => MaterialId == "wood";
    }
}
