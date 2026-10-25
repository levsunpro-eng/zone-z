using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    // Original synthesized audio: no downloaded weapon recordings or copied game assets.
    public static class AudioFX
    {
        public struct NoiseEvent
        {
            public int Id;
            public Actor Source;
            public Vector3 Position;
            public float Time, Radius;
        }
        public static readonly List<NoiseEvent> Noises = new List<NoiseEvent>();
        public static int LastNoiseId { get; private set; }
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<int, AudioSource> actorSources = new Dictionary<int, AudioSource>();
        static AudioSource uiSource;

        public static void Forget(Actor actor)
        {
            if (object.ReferenceEquals(actor, null)) return;
            actorSources.Remove(actor.GetInstanceID());
            Noises.RemoveAll(noise => noise.Source == actor || !noise.Source);
        }

        public static void ReportNoise(Actor actor, float radius) { if (actor) Hear(actor, radius); }

        public static void Shot(Actor actor, WeaponSpec weapon, bool alternative = false)
        {
            if (!actor || weapon == null) return;
            string style = weapon.Category == "sniper" ? "sniper" : weapon.Category == "shotgun" ? "shotgun" : weapon.Category == "melee" ? "swish" : weapon.Category == "taser" ? "electric" : "gun";
            bool silenced = weapon.HasSilencer && (alternative || weapon.Id == "weapon_mp5sd");
            if (silenced) style = "silenced";
            float pitch = weapon.Category == "pistol" ? 1.13f : weapon.Category == "smg" ? 1.28f : weapon.Category == "machinegun" ? .82f : 1;
            pitch *= 1 + (weapon.RecoilSeed % 13 - 6) * .015f;
            PlayActor(actor, style, actor.Human ? .55f : .7f, pitch, 70);
            Hear(actor, weapon.Category == "melee" ? 5 : silenced ? 24 : 62);
        }

        public static void Reload(Actor actor) => PlayActor(actor, "reload", .2f, 1, 12);
        public static void Footstep(Actor actor, bool quiet)
        {
            if (!actor) return;
            bool wood = false;
            if (Physics.Raycast(actor.transform.position + Vector3.up * .3f, Vector3.down, out var hit, 1, ~0, QueryTriggerInteraction.Ignore))
            {
                var material = hit.collider.GetComponentInParent<SurfaceTag>(); wood = material && material.MaterialId == "wood";
            }
            PlayActor(actor, wood ? "stepwood" : "step", quiet ? .08f : .18f, .92f + actor.Id % 5 * .035f, 18);
            if (!quiet) Hear(actor, 14);
        }
        public static void Hit(bool killed) => PlayUI(killed ? "kill" : "hit", killed ? .35f : .2f);
        public static void Hurt() => PlayUI("hurt", .18f);
        public static void Pickup() => PlayUI("pickup", .25f);
        public static void UIClick() => PlayUI("click", .28f, true);
        public static void InterfaceClick() => UIClick();
        public static void Explosion(Vector3 position, float volume = .8f) => PlayAt(position, "explosion", volume, 1, 85);
        public static void Impact(Vector3 position, bool wood, bool flesh) => PlayAt(position, flesh ? "flesh" : wood ? "stepwood" : "impact", flesh ? .12f : .07f, 1, 20);
        public static void Ability(Vector3 position) => PlayAt(position, "electric", .45f, .8f, 40);

        static void Hear(Actor actor, float radius)
        {
            if (!actor.Match) return;
            Noises.RemoveAll(x => !x.Source || actor.Match.MatchTime - x.Time > 3 || x.Time > actor.Match.MatchTime);
            Noises.Add(new NoiseEvent { Id = ++LastNoiseId, Source = actor, Position = actor.EyePosition, Time = actor.Match.MatchTime, Radius = radius });
            if (Noises.Count > 160) Noises.RemoveRange(0, Noises.Count - 160);
        }

        static void PlayActor(Actor actor, string key, float volume, float pitch, float range)
        {
            if (!actor) return;
            int id = actor.GetInstanceID();
            if (!actorSources.TryGetValue(id, out var source) || !source)
            {
                source = actor.gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 2;
                actorSources[id] = source;
            }
            source.spatialBlend = actor.Human ? 0 : 1; source.maxDistance = range; source.pitch = pitch;
            source.PlayOneShot(Clip(key), volume * (key.StartsWith("step", System.StringComparison.Ordinal) ? FootstepVolume : WeaponVolume));
        }

        static void PlayUI(string key, float volume, bool interfaceChannel = false)
        {
            if (!uiSource)
            {
                var go = new GameObject("CombatUIAudio"); if (GameRoot.Instance) go.transform.SetParent(GameRoot.Instance.transform);
                uiSource = go.AddComponent<AudioSource>(); uiSource.playOnAwake = false; uiSource.spatialBlend = 0; uiSource.ignoreListenerPause = true;
            }
            uiSource.PlayOneShot(Clip(key), volume * (interfaceChannel ? InterfaceVolume : EffectsVolume));
        }

        static void PlayAt(Vector3 position, string key, float volume, float pitch, float range)
        {
            // Editor simulations have no playback lifetime and cannot schedule Destroy.
            if (!Application.isPlaying) return;
            var go = new GameObject("Sound_" + key); go.transform.position = position;
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 1;
            source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 2; source.maxDistance = range;
            source.pitch = pitch; source.clip = Clip(key); source.volume = volume * EffectsVolume; source.Play();
            Object.Destroy(go, source.clip.length / Mathf.Max(.1f, pitch) + .1f);
        }

        // GameRoot applies the profile volume once on AudioListener; sources retain relative mix levels.
        static GameProfile Profile => GameRoot.Instance && GameRoot.Instance.Store != null ? GameRoot.Instance.Profile : null;
        static float WeaponVolume => Profile?.weaponVolume ?? 1;
        static float FootstepVolume => Profile?.footstepVolume ?? 1;
        public static float EffectsVolume => Profile?.effectsVolume ?? 1;
        static float InterfaceVolume => Profile?.interfaceVolume ?? .7f;

        static AudioClip Clip(string key)
        {
            if (clips.TryGetValue(key, out var cached) && cached) return cached;
            const int sampleRate = 22050;
            float duration = key == "explosion" ? 1.0f : key == "sniper" ? .44f : key == "shotgun" ? .29f : key == "kill" ? .17f : key == "pickup" ? .3f : key == "click" ? .045f : .13f;
            int length = Mathf.CeilToInt(duration * sampleRate);
            var samples = new float[length];
            uint random = 19349663;
            float low = 0;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)sampleRate, progress = i / (float)length;
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                float noise = ((random & 65535) / 32768f) - 1;
                low = Mathf.Lerp(low, noise, .08f);
                float envelope = Mathf.Exp(-progress * (key == "explosion" ? 5 : 9)) * Mathf.Min(1, t * 1300);
                float signal;
                switch (key)
                {
                    case "click": signal = Mathf.Sin(t * Mathf.PI * 2 * 1800) * .3f + noise * .05f; break;
                    case "hit": signal = Mathf.Sin(t * Mathf.PI * 2 * 1350) * .5f; break;
                    case "kill": signal = (Mathf.Sin(t * Mathf.PI * 2 * 1050) + Mathf.Sin(t * Mathf.PI * 2 * 1580)) * .35f; break;
                    case "pickup": signal = Mathf.Sin(t * Mathf.PI * 2 * (700 + progress * 1100)) * .45f; break;
                    case "electric": signal = Mathf.Sin(t * Mathf.PI * 2 * (190 - progress * 100)) * .6f + noise * .25f; break;
                    case "reload": signal = noise * .7f * (progress < .18f || progress > .5f && progress < .65f ? 1 : .03f); break;
                    case "step": signal = low * 2 + noise * .07f; break;
                    case "stepwood": signal = Mathf.Sin(t * Mathf.PI * 2 * 180) * .4f + low; break;
                    case "flesh": signal = low * 2; break;
                    case "hurt": signal = Mathf.Sin(t * Mathf.PI * 2 * (130 - progress * 50)) * .4f + low; break;
                    case "swish": signal = noise * .35f * Mathf.Sin(progress * Mathf.PI); break;
                    case "impact": signal = noise * .25f + Mathf.Sin(t * Mathf.PI * 2 * 2400) * .4f; break;
                    case "silenced": signal = Mathf.Sin(t * Mathf.PI * 2 * (280 - progress * 150)) * .4f + noise * .15f; break;
                    case "explosion": signal = low * 3 + noise * .22f + Mathf.Sin(t * Mathf.PI * 2 * 47) * .35f; break;
                    case "sniper": signal = noise * .65f + low * 2 + Mathf.Sin(t * Mathf.PI * 2 * 68) * .5f; break;
                    case "shotgun": signal = noise * .7f + low * 2; break;
                    default: signal = noise * .65f + Mathf.Sin(t * Mathf.PI * 2 * (110 - progress * 65)) * .45f; break;
                }
                samples[i] = Mathf.Clamp(signal * envelope, -.9f, .9f);
            }
            var clip = AudioClip.Create("ZoneZ_" + key, length, 1, sampleRate, false); clip.SetData(samples, 0); clips[key] = clip; return clip;
        }
    }
}
