using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZoneZ.Editor
{
    public static class BotDifficultyValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Vector3 Origin = new Vector3(3600, 3600, 3600);
        static readonly List<GameObject> created = new List<GameObject>();
        static GameRoot root;
        static MatchDirector match;

        public static void Run()
        {
            var previous = GameRoot.Instance;
            float previousTimeScale = Time.timeScale;
            CursorLockMode previousCursorLock = Cursor.lockState;
            bool previousCursorVisible = Cursor.visible;
            try
            {
                var fixture = New("Root"); fixture.SetActive(false);
                root = fixture.AddComponent<GameRoot>(); root.Catalog = GameCatalog.Load(); GameRoot.Instance = root;
                match = fixture.AddComponent<MatchDirector>(); match.Phase = MatchPhase.Playing; match.ModeId = "deathmatch";
                root.Match = match;
                SettingsValidation();
                DamageValidation();
                TimingValidation();
                AimValidation();
                Debug.Log("ZONEZ_BOT_DIFFICULTY_VALIDATION_OK: bot-only damage, shield/armor order, remote roles, queued damage, actual lifesteal, reaction/tracking/burst timing, geometric aim misses, invalid settings");
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--) if (created[i]) Object.DestroyImmediate(created[i]);
                created.Clear(); GameRoot.Instance = previous;
                Time.timeScale = previousTimeScale; Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
            }
        }

        static void SettingsValidation()
        {
            var defaults = BotDifficultySettings.FromJson(null);
            Near(defaults.DamageMultiplier, .6f, "missing settings retain reduced bot damage");
            Check(defaults.ReactionMinSeconds > .5f && defaults.ReactionMaxSeconds >= defaults.ReactionMinSeconds, "default reaction gives player time to respond");
            Check(defaults.TrackingMinSeconds >= .2f && defaults.TrackingMaxSeconds >= defaults.TrackingMinSeconds, "default tracking retains observations between physics ticks");
            Check(defaults.AimYawDegrees > 2 && defaults.AimPitchDegrees > 1, "default aim has angular error");
            Near(root.Catalog.BotDifficulty.DamageMultiplier, defaults.DamageMultiplier, "shipped catalog loads intended bot damage");
            var custom = BotDifficultySettings.FromJson(new JObject { ["damageMultiplier"] = .4, ["reactionMinSeconds"] = .3, ["reactionMaxSeconds"] = .5 });
            Near(custom.DamageMultiplier, .4f, "JSON overrides bot damage");
            Near(custom.TrackingMinSeconds, defaults.TrackingMinSeconds, "partial config retains defaults");
            Check(ReferenceEquals(BotDifficultySettings.FromJson(JValue.CreateNull()), BotDifficultySettings.Defaults), "null config uses defaults");

            Invalid(new JArray(), "config array");
            Invalid(new JObject { ["damageMultiplier"] = "0.6" }, "numeric strings");
            Invalid(new JObject { ["damageMultiplier"] = true }, "boolean multiplier");
            Invalid(new JObject { ["damageMultiplier"] = JValue.CreateNull() }, "null multiplier");
            Invalid(new JObject { ["damageMultiplier"] = double.NaN }, "NaN multiplier");
            Invalid(new JObject { ["reactionMinSeconds"] = double.PositiveInfinity }, "infinite timing");
            Invalid(new JObject { ["damageMultiplier"] = -.1 }, "negative damage");
            Invalid(new JObject { ["damageMultiplier"] = 4 }, "unbounded damage");
            Invalid(new JObject { ["trackingMinSeconds"] = 0 }, "zero tracking interval");
            Invalid(new JObject { ["aimYawDegrees"] = 46 }, "excessive aim error");
            Invalid(new JObject { ["turnDegreesPerSecond"] = 0 }, "immobile turn speed");
            Invalid(new JObject { ["burstMinShots"] = 1.5 }, "fractional burst count");
            Invalid(new JObject { ["burstMaxShots"] = 101 }, "unbounded burst count");
            Invalid(new JObject { ["reactionMinSeconds"] = 2 }, "reversed reaction bounds");
            Invalid(new JObject { ["trackingMinSeconds"] = 1 }, "reversed tracking bounds");
            Invalid(new JObject { ["burstMinShots"] = 5 }, "reversed burst bounds");
            Invalid(new JObject { ["burstPauseMinSeconds"] = 2 }, "reversed pause bounds");
        }

        static void DamageValidation()
        {
            var bot = Actor("Bot", 0); var victim = Actor("Victim", 1);
            var brain = bot.gameObject.AddComponent<BotBrain>(); brain.Owner = bot; brain.enabled = false;
            Check(bot.IsBotControlled, "disabled AI remains a bot for damage attribution");
            Reset(victim); victim.ApplyDamage(100, bot, ignoreArmor: true);
            Near(victim.HP, 40, "raw 100 bot damage becomes 60 HP");
            Near(victim.DamageHistory[0].Amount, 60, "damage history records reduced health damage");

            Reset(victim); victim.Armor = 100; victim.ApplyDamage(100, bot, armorRatio: 1.5f);
            Near(victim.HP, 55, "armor processes reduced incoming damage");
            Near(victim.Armor, 92.5f, "armor consumes points after bot scale");
            Reset(victim); victim.Armor = 1; victim.ApplyDamage(100, bot, armorRatio: 1.5f);
            Near(victim.HP, 42, "armor exhaustion applies to reduced damage");
            Near(victim.Armor, 0, "armor exhaustion clamps to zero");
            Reset(victim); victim.Armor = 100; victim.ApplyDamage(100, bot, hitgroup: "legs");
            Near(victim.HP, 40, "leg damage is scaled without armor");
            Near(victim.Armor, 100, "scaled leg damage preserves armor");

            Reset(victim); victim.Armor = 100; victim.Ability.ApplyLoot("protective_field");
            victim.ApplyDamage(50, bot);
            Near(victim.Shield, 10, "shield absorbs reduced bot damage");
            Near(victim.HP, 100, "shielded hit does not leak to health");
            victim.ApplyDamage(50, bot);
            Near(victim.Shield, 0, "second reduced hit exhausts shield");
            Near(victim.HP, 85, "shield spill receives armor after bot scale");
            Near(victim.Armor, 97.5f, "only shield spill consumes armor");

            bot.Human = true;
            Check(!bot.IsBotControlled, "local human with old brain is not a bot");
            Reset(victim); victim.ApplyDamage(50, bot, ignoreArmor: true); Near(victim.HP, 50, "local player damage remains unchanged");
            bot.Human = false; bot.RemoteControlled = true;
            Check(!bot.IsBotControlled, "remote human with old brain is not a bot");
            Reset(victim); victim.ApplyDamage(50, bot, ignoreArmor: true); Near(victim.HP, 50, "remote player damage remains unchanged");
            bot.RemoteControlled = false;
            var scripted = Actor("Actor without AI", 2);
            Check(!scripted.IsBotControlled, "actor without brain is not inferred to be a bot");
            Reset(victim); victim.ApplyDamage(50, scripted, ignoreArmor: true); Near(victim.HP, 50, "actor without brain retains base damage");
            Reset(victim); victim.ApplyDamage(50, null, ignoreArmor: true); Near(victim.HP, 50, "environmental damage remains unchanged");

            Reset(victim);
            var queue = (Queue<Action>)typeof(MatchDirector).GetField("damageBatch", Private).GetValue(match);
            queue.Enqueue(() => victim.ApplyDamage(100, bot, ignoreArmor: true));
            Near(victim.HP, 100, "deferred damage waits for batch resolution");
            match.FlushDamage(); Near(victim.HP, 40, "queued raw hit is reduced exactly once during resolution");
            Check(victim.DamageHistory.Count == 1, "queue produces one damage record");
            match.FlushDamage(); Near(victim.HP, 40, "empty flush cannot repeat damage");

            Reset(bot); bot.HP = 50; bot.Ability.ApplyLoot("vampirism");
            Reset(victim); victim.ApplyDamage(50, bot, ultimate: true, ignoreArmor: true);
            Near(victim.HP, 70, "ordinary ultimate HP damage uses bot scale");
            Near(bot.HP, 57.5f, "lifesteal uses reduced actual health damage");
            Reset(victim); victim.HP = 12; bot.HP = 50;
            victim.ApplyDamage(100, bot, ultimate: true, ignoreArmor: true);
            Near(bot.HP, 53, "overkill lifesteal uses only remaining HP");
            Near(victim.DamageHistory[0].Amount, 12, "overkill history uses only remaining HP");

            var network = root.gameObject.AddComponent<NetworkSession>(); root.Network = network;
            try
            {
                typeof(NetworkSession).GetProperty("IsClient").SetValue(network, true);
                Check(bot.IsNetworkReplica && !bot.IsBotControlled, "network replicas are not authoritative bots");
                Reset(victim); victim.ApplyDamage(50, bot, ignoreArmor: true);
                Near(victim.HP, 100, "client replicas do not resolve damage locally");
            }
            finally
            {
                typeof(NetworkSession).GetProperty("IsClient").SetValue(network, false);
                root.Network = null; Object.DestroyImmediate(network);
            }
        }

        static void TimingValidation()
        {
            var settings = root.Catalog.BotDifficulty;
            var random = new System.Random(4181);
            var timing = new BotCombatTiming();
            Check(!timing.ReadyToTrigger(100), "new bot cannot shoot without acquisition");
            for (int sample = 0; sample < 64; sample++)
            {
                float now = sample * 10;
                timing.Acquire(now, settings, random);
                Between(timing.ReactionReadyAt - now, settings.ReactionMinSeconds, settings.ReactionMaxSeconds, "reaction delay bounds");
                Check(!timing.ReadyToTrigger(timing.ReactionReadyAt - .001f), "bot waits until reaction deadline");
                Check(timing.ReadyToTrigger(timing.ReactionReadyAt), "bot can trigger at reaction deadline");
                Check(timing.TrackingDue(now), "newly acquired target can be sampled immediately");
                timing.RecordTrackingSample(now, settings, random);
                Between(timing.NextTrackingAt - now, settings.TrackingMinSeconds, settings.TrackingMaxSeconds, "tracking delay bounds");
                Check(!timing.TrackingDue(timing.NextTrackingAt - .001f), "tracking retains old observation before deadline");
                Check(timing.TrackingDue(timing.NextTrackingAt), "tracking samples at its deadline");

                int shots = timing.BurstShotsRemaining;
                Between(shots, settings.BurstMinShots, settings.BurstMaxShots, "burst shot bounds");
                float fireAt = timing.ReactionReadyAt;
                for (int shot = 0; shot < shots; shot++)
                {
                    Check(timing.ReadyToTrigger(fireAt), "burst accepts each configured trigger");
                    timing.AcceptedTrigger(fireAt, settings, random);
                    if (shot < shots - 1) Check(timing.BurstShotsRemaining == shots - shot - 1, "accepted trigger consumes one burst shot");
                }
                Between(timing.BurstResumeAt - fireAt, settings.BurstPauseMinSeconds, settings.BurstPauseMaxSeconds, "burst pause bounds");
                Check(!timing.ReadyToTrigger(timing.BurstResumeAt - .001f), "completed burst pauses fire");
                Check(timing.ReadyToTrigger(timing.BurstResumeAt), "next burst resumes at deadline");
                float reaction = timing.ReactionReadyAt;
                timing.ResetBurst(fireAt, settings, random);
                Near(timing.ReactionReadyAt, reaction, "weapon change cannot bypass reaction delay");
                timing.Clear(); Check(!timing.ReadyToTrigger(now + 100), "target reset clears firing readiness");
                Check(timing.BurstShotsRemaining == 0, "reset discards previous burst");
                timing.Acquire(now + 2, settings, random);
                Check(!timing.ReadyToTrigger(now + 2), "reacquisition has a fresh reaction delay");
            }
        }

        static void AimValidation()
        {
            var shooter = Actor("Aim shooter", 3); var target = Actor("Aim target", 4);
            shooter.transform.position = Origin;
            target.transform.position = Origin + Vector3.forward * 25;
            var brain = shooter.gameObject.AddComponent<BotBrain>(); brain.Owner = shooter; brain.enabled = false;
            HitZone.AddBox(target.transform, target, "chest", Vector3.up * 1.25f, new Vector3(.46f, .43f, .3f));
            Physics.SyncTransforms();
            Vector3 start = Origin + Vector3.up * 1.25f;
            Check(CombatTrace.Cast(start, Vector3.forward, 30, shooter, true).Target == target, "unperturbed aim intersects torso");
            int misses = 0; float largestError = 0; Vector2 first = Vector2.zero;
            for (int sample = 0; sample < 128; sample++)
            {
                typeof(BotBrain).GetMethod("PickAimError", Private).Invoke(brain, null);
                var error = (Vector2)typeof(BotBrain).GetField("aimErrorGoal", Private).GetValue(brain);
                Check(!float.IsNaN(error.x) && !float.IsNaN(error.y), "aim error remains finite");
                Between(error.x, -root.Catalog.BotDifficulty.AimYawDegrees, root.Catalog.BotDifficulty.AimYawDegrees, "horizontal aim bound");
                Between(error.y, -root.Catalog.BotDifficulty.AimPitchDegrees, root.Catalog.BotDifficulty.AimPitchDegrees, "vertical aim bound");
                if (sample == 0) first = error;
                largestError = Mathf.Max(largestError, (error - first).magnitude);
                Vector3 direction = Quaternion.Euler(error.y, error.x, 0) * Vector3.forward;
                if (CombatTrace.Cast(start, direction, 30, shooter, true).Target != target) misses++;
            }
            Check(largestError > 2, "aim samples vary across the encounter");
            Check(misses >= 64, "default sampled aim errors can miss a torso at 25 metres");
            // Resetting an encounter must remove its old bias and pending trigger state.
            var timing = (BotCombatTiming)typeof(BotBrain).GetField("combatTiming", Private).GetValue(brain);
            timing.Acquire(match.MatchTime, root.Catalog.BotDifficulty, new System.Random(7));
            brain.StopNavigation();
            Near(((Vector2)typeof(BotBrain).GetField("aimErrorGoal", Private).GetValue(brain)).magnitude, 0, "stopping AI clears old aim bias");
            Check(!timing.ReadyToTrigger(match.MatchTime + 100), "stopping AI clears combat readiness");
        }

        static Actor Actor(string name, int team)
        {
            var actor = New(name).AddComponent<Actor>(); actor.Match = match; actor.Team = team; actor.DisplayName = name;
            actor.MaxHP = 100; actor.Id = match.Actors.Count;
            actor.transform.position = Origin + Vector3.left * (20 + actor.Id * 2);
            actor.Ability = actor.gameObject.AddComponent<AbilityController>(); actor.Ability.Initialize(actor, "frag_grenade");
            match.Actors.Add(actor); Reset(actor); return actor;
        }
        static void Reset(Actor actor)
        {
            actor.Alive = true; actor.HP = 100; actor.Armor = 0; actor.SpawnProtectedUntil = 0;
            actor.DamageHistory.Clear(); actor.Ability.ResetForSpawn(false);
        }
        static GameObject New(string name) { var go = new GameObject("Bot validation " + name); created.Add(go); return go; }
        static void Invalid(JToken token, string label)
        {
            try { BotDifficultySettings.FromJson(token); }
            catch (FormatException) { return; }
            throw new InvalidOperationException("Bot difficulty validation: config must reject " + label);
        }
        static void Between(float value, float minimum, float maximum, string label) => Check(value >= minimum - .0001f && value <= maximum + .0001f, label + " (got " + value + ")");
        static void Near(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) <= .001f, label + $" (expected {expected}, got {actual})");
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Bot difficulty validation: " + label); }
    }
}
