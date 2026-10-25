using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZoneZ
{
    public sealed class WeaponSpec
    {
        public string Id, Name, Category, Slot;
        public float Damage, FireInterval, Range, RangeModifier, ArmorRatio, HeadMultiplier;
        public float MoveSpeed, ReloadSeconds, Recoil, Spread, StandInaccuracy, MoveInaccuracy;
        public int Pellets, Magazine, Reserve, RecoilSeed;
        public bool Automatic, HasScope, HasBurst, HasSilencer, SingleShellReload;
        public JObject Source, Overrides;

        // Preserve raw source arrays: index 0 is normal mode, 1 is alternate mode.
        public float Value(string field, bool alt = false, float fallback = 0)
        {
            JToken token = Source?[field];
            if (token is JArray values) token = values.Count == 0 ? null : values[Math.Min(alt ? 1 : 0, values.Count - 1)];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? 1 : 0;
            return token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? token.Value<float>() : fallback;
        }

        internal static WeaponSpec Parse(JObject entry)
        {
            var spec = new WeaponSpec {
                Id = entry.Value<string>("id"), Name = entry.Value<string>("displayName"),
                Category = entry.Value<string>("category"), Source = (JObject)entry["sourceFields"],
                Overrides = (JObject)entry["projectOverrides"]
            };
            spec.Source = spec.Source ?? new JObject();
            spec.Overrides = spec.Overrides ?? new JObject();
            spec.Slot = spec.Overrides.Value<string>("loadoutSlot") ?? "primary";
            spec.Damage = spec.Value("m_nDamage", fallback: 40);
            spec.FireInterval = Mathf.Max(.03f, spec.Value("m_flCycleTime", fallback: .1f));
            spec.Range = spec.Value("m_flRange", fallback: 4096) * .01905f;
            spec.RangeModifier = spec.Value("m_flRangeModifier", fallback: 1);
            spec.ArmorRatio = spec.Value("m_flArmorRatio", fallback: 2);
            spec.HeadMultiplier = spec.Value("m_flHeadshotMultiplier", fallback: 4);
            spec.MoveSpeed = spec.Value("m_flMaxSpeed", fallback: 230) * .01905f;
            spec.ReloadSeconds = spec.Overrides.Value<float?>("reloadDurationSeconds") ?? 2.5f;
            spec.Recoil = spec.Value("m_flRecoilMagnitude");
            spec.Spread = spec.Value("m_flSpread");
            spec.StandInaccuracy = spec.Value("m_flInaccuracyStand");
            spec.MoveInaccuracy = spec.Value("m_flInaccuracyMove");
            spec.Pellets = Math.Max(1, (int)spec.Value("m_nNumBullets", fallback: 1));
            spec.Magazine = spec.Overrides.Value<int?>("initialMagazineAmmo") ?? Math.Max(1, (int)spec.Value("m_iMaxClip1", fallback: 1));
            spec.Reserve = spec.Overrides.Value<int?>("initialReserveAmmo") ?? 0;
            spec.RecoilSeed = (int)spec.Value("m_nRecoilSeed");
            spec.Automatic = spec.Value("m_bIsFullAuto") != 0;
            spec.HasScope = spec.Value("m_nZoomLevels") > 0;
            spec.HasBurst = spec.Value("m_bHasBurstMode") != 0;
            string silencer = spec.Source.Value<string>("m_eSilencerType");
            spec.HasSilencer = !string.IsNullOrEmpty(silencer) && silencer != "WEAPONSILENCER_NONE";
            spec.SingleShellReload = spec.Overrides.Value<string>("reloadStyle") == "individual_shells" ||
                spec.Overrides.Value<string>("reloadStyle") == "single_shell" ||
                spec.Overrides.Value<string>("reloadStyle") == "per_shell";
            if (spec.Slot == "knife") {
                spec.Damage = spec.Overrides["primaryAttack"].Value<float>("baseDamage");
                spec.FireInterval = spec.Overrides["primaryAttack"].Value<float>("cycleSeconds");
                spec.Range = spec.Overrides["primaryAttack"].Value<float>("rangeMeters");
                spec.MoveSpeed = spec.Overrides.Value<float>("movementSpeedMetersPerSecond");
                spec.HeadMultiplier = 1; spec.Magazine = 1;
            }
            if (string.IsNullOrEmpty(spec.Id)) throw new FormatException("Оружие без ID в таблице баланса");
            return spec;
        }
    }

    public sealed class AbilitySpec
    {
        public string Id, Name;
        public int Cost, KillsRequired, Shots;
        public float Duration, Damage, Radius;
        public JObject Effect;
        internal static AbilitySpec Parse(JObject entry)
        {
            JObject effect = (JObject)entry["effect"];
            return new AbilitySpec {
                Id = entry.Value<string>("id"), Name = entry.Value<string>("nameRu"),
                Cost = entry.Value<int>("priceCredits"), KillsRequired = entry.Value<int>("killsRequired"),
                Duration = effect.Value<float?>("durationSeconds") ?? 0,
                Damage = effect.Value<float?>("maximumDamage") ?? effect.Value<float?>("damage") ?? 0,
                Radius = effect.Value<float?>("radiusMeters") ?? 0,
                Shots = effect.Value<int?>("shots") ?? effect.Value<int?>("charges") ?? 1,
                Effect = effect
            };
        }
    }

    public sealed class GameCatalog
    {
        static GameCatalog cached;
        public List<WeaponSpec> Weapons = new List<WeaponSpec>();
        public List<AbilitySpec> Abilities = new List<AbilitySpec>();
        public JObject Economy, Modes, Loot;
        public BotDifficultySettings BotDifficulty = BotDifficultySettings.Defaults;
        public float MovementSpeedMultiplier = 1;
        readonly Dictionary<string, WeaponSpec> weaponsById = new Dictionary<string, WeaponSpec>();
        readonly Dictionary<string, AbilitySpec> abilitiesById = new Dictionary<string, AbilitySpec>();
        public WeaponSpec Weapon(string id) => id != null && weaponsById.TryGetValue(id, out var item) ? item : null;
        public AbilitySpec Ability(string id) => id != null && abilitiesById.TryGetValue(id, out var item) ? item : null;

        public static GameCatalog Load()
        {
            if (cached != null) return cached;
            var result = new GameCatalog();
            foreach (JObject item in Read("weapons")["weapons"]) {
                WeaponSpec weapon = WeaponSpec.Parse(item);
                result.Weapons.Add(weapon); result.weaponsById.Add(weapon.Id, weapon);
            }
            foreach (JObject item in Read("abilities")["abilities"]) {
                AbilitySpec ability = AbilitySpec.Parse(item);
                result.Abilities.Add(ability); result.abilitiesById.Add(ability.Id, ability);
            }
            result.Economy = Read("economy"); result.Modes = Read("modes"); result.Loot = Read("loot");
            result.BotDifficulty = BotDifficultySettings.FromJson(result.Modes["botDifficulty"]);
            result.MovementSpeedMultiplier = result.Modes["sharedRules"]?.Value<float?>("movementSpeedMultiplier") ?? 1;
            if (float.IsNaN(result.MovementSpeedMultiplier) || float.IsInfinity(result.MovementSpeedMultiplier) || result.MovementSpeedMultiplier <= 0)
                throw new FormatException("Множитель скорости движения должен быть положительным конечным числом.");
            if (result.Weapons.Count != 36 || result.Abilities.Count != 10)
                throw new FormatException("Неполный каталог: требуется 36 видов оружия и 10 ультимейтов.");
            cached = result;
            return result;
        }

        static JObject Read(string file)
        {
            TextAsset asset = Resources.Load<TextAsset>("Balance/" + file);
            if (!asset) throw new InvalidOperationException("Не найдена таблица Balance/" + file + ".json");
            JObject data = JObject.Parse(asset.text);
            if (data.Value<int>("schemaVersion") != 1 || data.Value<string>("balanceVersion") != "1.0.2")
                throw new FormatException("Несовместимая версия таблицы " + file);
            return data;
        }

        public int UpgradeCost(int currentLevel)
        {
            JArray prices = (JArray)Economy["progression"]["levelCostsCredits"];
            return currentLevel >= 0 && currentLevel < prices.Count ? prices[currentLevel].Value<int>() : -1;
        }

        public int CosmeticCost(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            string[] parts = id.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int index)) return -1;
            if (id != parts[0] + "_" + index) return -1;
            JObject cosmetic = (JObject)Economy["cosmetics"];
            if (parts[0] == "pattern" && index >= 1 && index <= 3) return cosmetic.Value<int>("patternPriceCredits");
            if (parts[0] == "sticker" && index >= 1 && index <= 3) return cosmetic.Value<int>("stickerPriceCredits");
            if (parts[0] == "skin" && index >= 1 && index <= 4) return cosmetic["skinPricesCredits"][index - 1].Value<int>();
            return -1;
        }
    }
}
