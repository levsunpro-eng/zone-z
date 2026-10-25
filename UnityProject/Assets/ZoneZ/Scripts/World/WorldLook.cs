using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZoneZ
{
    public static class WorldLook
    {
        public const string ProfileResource = "World/DuskLook";

        public static void Create(Transform parent)
        {
            var volume = WorldArt.Group(parent, "Late afternoon color grading").gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1;
            volume.sharedProfile = Resources.Load<VolumeProfile>(ProfileResource);
            if (!volume.sharedProfile)
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                Configure(profile);
                volume.profile = profile;
            }
        }

        public static void Configure(VolumeProfile profile)
        {
            if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-.18f);
            color.contrast.Override(9);
            color.saturation.Override(-13);
            color.colorFilter.Override(new Color(.97f, .985f, 1f));
            if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.10f);
            vignette.smoothness.Override(.45f);
        }

        public static void ConfigureCamera(Camera camera)
        {
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }
    }
}
