using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZoneZ
{
    [Serializable]
    public sealed class SkinPreset
    {
        public string id = "", name = "Мой пресет", weaponId;
        public int formatVersion = 1;
        public string colorA = "4B5446", colorB = "20272C", colorC = "BFA470";
        public int pattern, baseStyle;
        public int[] stickers = new int[4];
        public float wear;
        public float patternScale = 1, patternRotation;
        public float[] stickerRotations = new float[4];
        public float[] stickerScales = new[] { 1f, 1f, 1f, 1f };
        public bool applied = true;
        public SkinPreset Copy() => JsonConvert.DeserializeObject<SkinPreset>(JsonConvert.SerializeObject(this));
    }

    [Serializable]
    public sealed class GameProfile
    {
        public int schemaVersion = 1, credits = 500, healthLevel, speedLevel, strengthLevel;
        public string primaryId = "weapon_ak47", pistolId = "weapon_glock", ultimateId = "frag_grenade";
        public List<string> ownedAbilities = new List<string> { "frag_grenade" };
        public List<string> ownedCosmetics = new List<string>();
        public List<string> rewardedMatches = new List<string>();
        public List<SkinPreset> skins = new List<SkinPreset>();
        public float sensitivity = 1, volume = .7f, fov = 60, uiScale = 1;
        public int qualityLevel = 1, frameLimit = 60;
        public bool fullscreen = true, vSync;
        public float weaponVolume = 1, footstepVolume = 1, effectsVolume = 1, ambientVolume = .5f;
        public int resolutionWidth, resolutionHeight;
        public Dictionary<string,int> keyBindings = GameInput.DefaultBindings();
        public bool invertY, reducedFlashes;
        public float scopeSensitivity = 1, viewMotion = 1, interfaceVolume = .7f, menuMusicVolume = .3f;
        public string crosshairColor = "DDE6CD";
        public float crosshairThickness = 2;
    }

    public sealed class ProfileStore
    {
        public GameProfile Current;
        public string LastError { get; private set; }
        public string FilePath { get; private set; }
        public bool WriteBlocked { get; private set; }
        public Func<bool> PurchasesAllowed = () => true;
        string committed;
        bool primaryHealthy;
        readonly object gate = new object();
        static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
        GameCatalog Catalog => GameCatalog.Load();

        public static ProfileStore Load() => Load(Application.persistentDataPath);

        // Directory overload permits tests to exercise real atomic saves away from a player's profile.
        public static ProfileStore Load(string directory)
        {
            var store = new ProfileStore { FilePath = Path.Combine(directory, "profile.json") };
            try {
                Directory.CreateDirectory(directory);
                string mainError = null;
                if (File.Exists(store.FilePath)) {
                    if (TryRead(store.FilePath, out var main, out mainError)) { store.Current = main; store.primaryHealthy = true; }
                    else {
                        if (mainError == "future_schema") {
                            store.WriteBlocked = true;
                            store.LastError = "Профиль создан более новой версией игры. Сохранение отключено, исходный файл сохранён.";
                        }
                        else {
                            // Preserve the malformed original before replacing it with a recovered save.
                            string archive = store.FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json";
                            File.Copy(store.FilePath, archive, false);
                            if (TryRead(store.FilePath + ".bak", out var backup, out string backupError)) {
                                store.Current = backup;
                                store.LastError = "Профиль восстановлен из резервной копии. Повреждённый файл сохранён отдельно.";
                            } else {
                                store.WriteBlocked = true;
                                store.LastError = backupError == "future_schema" ? "Резервный профиль создан более новой версией игры. Исходные файлы сохранены." : "Файлы профиля повреждены. Исходные файлы сохранены; запись отключена до восстановления.";
                            }
                        }
                    }
                } else if (File.Exists(store.FilePath + ".bak")) {
                    if (TryRead(store.FilePath + ".bak", out var backupOnly, out _)) {
                        store.Current = backupOnly;
                        store.LastError = "Основной файл отсутствует. Профиль восстановлен из резервной копии.";
                    } else { store.WriteBlocked = true; store.LastError = "Резервный профиль недоступен или несовместим. Запись отключена."; }
                }
            } catch (Exception ex) {
                store.WriteBlocked = true;
                store.LastError = "Хранилище профиля недоступно: " + ex.Message;
            }
            if (store.Current == null) store.Current = new GameProfile();
            Normalize(store.Current);
            store.committed = Serialize(store.Current);
            // The first successful purchase or settings change persists a newly created profile.
            return store;
        }

        static bool TryRead(string path, out GameProfile profile, out string error)
        {
            profile = null; error = null;
            try {
                if (!File.Exists(path)) return false;
                var data = JObject.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (data["schemaVersion"]?.Type != JTokenType.Integer) { error = "missing_schema"; return false; }
                if (data.Value<int>("schemaVersion") > 1) { error = "future_schema"; return false; }
                if (data["credits"]?.Type != JTokenType.Integer) { error = "missing_balance"; return false; }
                var value = data.ToObject<GameProfile>(JsonSerializer.Create(ReadSettings));
                if (value == null) { error = "empty"; return false; }
                if (value.schemaVersion > 1) { error = "future_schema"; return false; }
                if (value.schemaVersion != 1 || value.credits < 0) { error = "invalid"; return false; }
                profile = value; return true;
            } catch (Exception ex) { error = ex.Message; return false; }
        }

        static void Normalize(GameProfile profile)
        {
            profile.healthLevel = Mathf.Clamp(profile.healthLevel, 0, 5);
            profile.speedLevel = Mathf.Clamp(profile.speedLevel, 0, 5);
            profile.strengthLevel = Mathf.Clamp(profile.strengthLevel, 0, 5);
            profile.ownedAbilities = profile.ownedAbilities ?? new List<string>();
            if (!profile.ownedAbilities.Contains("frag_grenade")) profile.ownedAbilities.Add("frag_grenade");
            profile.ownedAbilities = profile.ownedAbilities.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            profile.ownedCosmetics = (profile.ownedCosmetics ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            profile.rewardedMatches = (profile.rewardedMatches ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            profile.skins = (profile.skins ?? new List<SkinPreset>()).Where(x => x != null).ToList();
            var skinIds = new HashSet<string>();
            foreach (var skin in profile.skins) {
                if (string.IsNullOrWhiteSpace(skin.id) || !skinIds.Add(skin.id)) { skin.id = Guid.NewGuid().ToString("N"); skinIds.Add(skin.id); }
            }
            foreach (var group in profile.skins.Where(x => x.applied).GroupBy(x => x.weaponId)) {
                var chosen = group.Last();
                foreach (var skin in group) skin.applied = ReferenceEquals(skin, chosen);
            }
            profile.sensitivity = FiniteClamp(profile.sensitivity, .1f, 4, 1);
            profile.volume = FiniteClamp(profile.volume, 0, 1, .7f);
            profile.weaponVolume = FiniteClamp(profile.weaponVolume, 0, 1, 1);
            profile.footstepVolume = FiniteClamp(profile.footstepVolume, 0, 1, 1);
            profile.effectsVolume = FiniteClamp(profile.effectsVolume, 0, 1, 1);
            profile.ambientVolume = FiniteClamp(profile.ambientVolume, 0, 1, .5f);
            profile.interfaceVolume = FiniteClamp(profile.interfaceVolume, 0, 1, .7f);
            profile.menuMusicVolume = FiniteClamp(profile.menuMusicVolume, 0, 1, .3f);
            profile.scopeSensitivity = FiniteClamp(profile.scopeSensitivity, .1f, 3, 1);
            profile.viewMotion = FiniteClamp(profile.viewMotion, 0, 1, 1);
            profile.crosshairThickness = FiniteClamp(profile.crosshairThickness, 1, 4, 2);
            if (profile.crosshairColor == null || profile.crosshairColor.Length != 6 || !profile.crosshairColor.All(Uri.IsHexDigit)) profile.crosshairColor = "DDE6CD";
            else profile.crosshairColor = profile.crosshairColor.ToUpperInvariant();
            if (profile.resolutionWidth < 640 || profile.resolutionWidth > 16384 || profile.resolutionHeight < 360 || profile.resolutionHeight > 16384) profile.resolutionWidth = profile.resolutionHeight = 0;
            profile.keyBindings = GameInput.NormalizeBindings(profile.keyBindings);
            profile.fov = FiniteClamp(profile.fov, 50, 90, 60);
            profile.uiScale = FiniteClamp(profile.uiScale, .8f, 1.5f, 1);
            profile.qualityLevel = Mathf.Clamp(profile.qualityLevel, 0, 2);
            if (profile.frameLimit != 60 && profile.frameLimit != 120 && profile.frameLimit != 144) profile.frameLimit = 60;
            var catalog = GameCatalog.Load();
            if (catalog.Weapon(profile.primaryId)?.Slot != "primary") profile.primaryId = "weapon_ak47";
            if (catalog.Weapon(profile.pistolId)?.Slot != "pistol") profile.pistolId = "weapon_glock";
            if (catalog.Ability(profile.ultimateId) == null || !profile.ownedAbilities.Contains(profile.ultimateId)) profile.ultimateId = "frag_grenade";
        }

        static float FiniteClamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        static string Serialize(GameProfile profile) => JsonConvert.SerializeObject(profile, Formatting.Indented);
        static GameProfile Copy(GameProfile profile) => JsonConvert.DeserializeObject<GameProfile>(Serialize(profile), ReadSettings);

        public bool Save()
        {
            lock (gate) {
                var candidate = Copy(Current);
                if (Write(candidate)) { Current = candidate; return true; }
                // Public Current supports callers setting fields; failed Save still restores committed state.
                Current = JsonConvert.DeserializeObject<GameProfile>(committed, ReadSettings);
                return false;
            }
        }

        bool Write(GameProfile profile)
        {
            if (WriteBlocked) { LastError = "Сохранение недоступно. Исходный профиль не изменён."; return false; }
            string temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                if (profile.schemaVersion != 1) throw new InvalidOperationException("Несовместимая схема профиля");
                Normalize(profile);
                if (profile.credits < 0) throw new InvalidOperationException("Отрицательный баланс недопустим");
                string json = Serialize(profile);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                // Recovery already archived the malformed main. Keep the known-good
                // backup until a subsequent save has a healthy main to replace it.
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, primaryHealthy ? FilePath + ".bak" : null, true);
                else File.Move(temporary, FilePath);
                committed = json; primaryHealthy = true; LastError = null; return true;
            } catch (Exception ex) {
                LastError = "Не удалось сохранить профиль: " + ex.Message;
                // A failed temporary file is retained for recovery; never remove existing player files.
                return false;
            }
        }

        bool Change(Func<GameProfile, string> operation, out string error)
        {
            lock (gate) {
                var candidate = Copy(Current);
                error = operation(candidate);
                if (error != null) { LastError = error; return false; }
                if (!WriteBlocked && File.Exists(FilePath) && Serialize(candidate) == committed) {
                    // A confirmed transaction remains confirmed even if the file is
                    // temporarily locked when its caller repeats the same request.
                    Current = candidate; LastError = null; return true;
                }
                if (!Write(candidate)) { error = LastError; return false; }
                Current = candidate; return true;
            }
        }

        public bool PurchaseAbility(string id, out string error)
        {
            var spec = Catalog.Ability(id);
            if (spec == null) { error = "Неизвестная способность"; return false; }
            return Change(p => {
                if (!PurchasesAllowed()) return "Покупки доступны только в главном меню";
                if (p.ownedAbilities.Contains(id)) return null;
                if (p.credits < spec.Cost) return "Не хватает " + (spec.Cost - p.credits) + " кредитов";
                p.credits -= spec.Cost; p.ownedAbilities.Add(id); return null;
            }, out error);
        }

        public bool Upgrade(string branch, out string error)
        {
            int level = branch == "health" ? Current.healthLevel : branch == "speed" ? Current.speedLevel : Current.strengthLevel;
            return Upgrade(branch, level, out error);
        }

        public bool Upgrade(string branch, int expectedCurrentLevel, out string error)
        {
            return Change(p => {
                if (!PurchasesAllowed()) return "Покупки доступны только в главном меню";
                int level;
                switch (branch) { case "health": level = p.healthLevel; break; case "speed": level = p.speedLevel; break; case "strength": level = p.strengthLevel; break; default: return "Неизвестная ветка"; }
                if (level != expectedCurrentLevel) return "Уровень уже изменён. Проверьте новую цену";
                int cost = Catalog.UpgradeCost(level);
                if (cost < 0) return "Максимальный уровень";
                if (p.credits < cost) return "Не хватает " + (cost - p.credits) + " кредитов";
                p.credits -= cost;
                if (branch == "health") p.healthLevel++; else if (branch == "speed") p.speedLevel++; else p.strengthLevel++;
                return null;
            }, out error);
        }

        public bool PurchaseCosmetic(string id, int cost, out string error)
        {
            int expected = Catalog.CosmeticCost(id);
            if (expected < 0 || cost != expected) { error = "Неверная цена или предмет"; return false; }
            return Change(p => {
                if (!PurchasesAllowed()) return "Покупки доступны только в главном меню";
                if (p.ownedCosmetics.Contains(id)) return null;
                if (p.credits < expected) return "Не хватает " + (expected - p.credits) + " кредитов";
                p.credits -= expected; p.ownedCosmetics.Add(id); return null;
            }, out error);
        }

        public bool GrantReward(string matchId, int amount)
        {
            if (string.IsNullOrWhiteSpace(matchId) || amount < 0) { LastError = "Некорректная награда"; return false; }
            return Change(p => {
                if (p.rewardedMatches.Contains(matchId)) return null;
                if ((long)p.credits + amount > int.MaxValue) return "Баланс превышает допустимый предел";
                p.credits += amount; p.rewardedMatches.Add(matchId); return null;
            }, out _);
        }

        public bool SelectWeapon(string id, out string error)
        {
            var spec = Catalog.Weapon(id);
            if (spec == null || (spec.Slot != "primary" && spec.Slot != "pistol")) { error = "Этот слот доступен всегда"; return false; }
            return Change(p => { if (spec.Slot == "primary") p.primaryId = id; else p.pistolId = id; return null; }, out error);
        }

        public bool SelectAbility(string id, out string error) => Change(p => {
            if (!PurchasesAllowed()) return "Ульта фиксируется при входе в матч";
            if (Catalog.Ability(id) == null || !p.ownedAbilities.Contains(id)) return "Сначала разблокируйте способность";
            p.ultimateId = id; return null;
        }, out error);

        public SkinPreset SkinFor(string weaponId)
        {
            SkinPreset skin = Current.skins.LastOrDefault(x => x.weaponId == weaponId && x.applied);
            if (skin == null || ValidateSkin(skin) != null) return new SkinPreset { weaponId = weaponId };
            return skin.Copy();
        }

        public bool SaveSkin(SkinPreset skin, out string error)
        {
            error = ValidateSkin(skin);
            if (error != null) return false;
            SkinPreset saved = skin.Copy();
            if (string.IsNullOrEmpty(saved.id)) saved.id = Guid.NewGuid().ToString("N");
            saved.name = saved.name.Trim(); saved.applied = true;
            return Change(p => {
                if (!PurchasesAllowed()) return "Скины доступны только в главном меню";
                if (p.skins.Any(x => x.id == saved.id && x.weaponId != saved.weaponId)) return "Пресет принадлежит другому оружию";
                foreach (SkinPreset item in p.skins.Where(x => x.weaponId == saved.weaponId)) item.applied = false;
                int index = p.skins.FindIndex(x => x.id == saved.id);
                if (index >= 0) p.skins[index] = saved; else p.skins.Add(saved);
                return null;
            }, out error);
        }

        public bool ApplySkin(string id, out string error) => Change(p => {
            if (!PurchasesAllowed()) return "Скины доступны только в главном меню";
            var skin = p.skins.FirstOrDefault(x => x.id == id);
            string problem = ValidateSkin(skin);
            if (problem != null) return problem;
            foreach (var other in p.skins.Where(x => x.weaponId == skin.weaponId)) other.applied = other.id == id;
            return null;
        }, out error);

        public bool DeleteSkin(string id, out string error) => Change(p => {
            if (!PurchasesAllowed()) return "Скины доступны только в главном меню";
            if (p.skins.RemoveAll(x => x.id == id) == 0) return "Пресет не найден";
            return null;
        }, out error);

        public string SkinProblem(SkinPreset skin) => ValidateSkin(skin);

        string ValidateSkin(SkinPreset skin)
        {
            if (skin == null || Catalog.Weapon(skin.weaponId) == null) return "Неизвестное оружие";
            if (skin.formatVersion != 1) return "Несовместимая версия пресета";
            if (string.IsNullOrWhiteSpace(skin.name) || skin.name.Trim().Length > 40) return "Имя пресета: от 1 до 40 символов";
            foreach (string value in new[] { skin.colorA, skin.colorB, skin.colorC })
                if (value == null || value.Length != 6 || !value.All(Uri.IsHexDigit)) return "Цвет должен содержать 6 символов HEX";
            if (skin.pattern < 0 || skin.pattern > 3 || skin.baseStyle < 0 || skin.baseStyle > 4) return "Неизвестный узор или скин";
            if (skin.pattern > 0 && !Current.ownedCosmetics.Contains("pattern_" + skin.pattern)) return "Сначала купите выбранный узор";
            if (skin.baseStyle > 0 && !Current.ownedCosmetics.Contains("skin_" + skin.baseStyle)) return "Сначала купите выбранный скин";
            if (skin.stickers == null || skin.stickers.Length != 4) return "Требуется четыре слота наклеек";
            foreach (int sticker in skin.stickers) {
                if (sticker < 0 || sticker > 3) return "Неизвестная наклейка";
                if (sticker > 0 && !Current.ownedCosmetics.Contains("sticker_" + sticker)) return "Сначала купите выбранную наклейку";
            }
            if (float.IsNaN(skin.wear) || skin.wear < 0 || skin.wear > 1) return "Некорректный износ";
            if (float.IsNaN(skin.patternScale) || skin.patternScale < .5f || skin.patternScale > 2) return "Масштаб узора: от 0,5 до 2";
            if (float.IsNaN(skin.patternRotation) || Mathf.Abs(skin.patternRotation) > 180) return "Поворот узора: от −180 до 180°";
            if (skin.stickerRotations == null || skin.stickerRotations.Length != 4 || skin.stickerScales == null || skin.stickerScales.Length != 4) return "Некорректные параметры наклеек";
            for (int i = 0; i < 4; i++)
                if (float.IsNaN(skin.stickerRotations[i]) || Mathf.Abs(skin.stickerRotations[i]) > 180 || float.IsNaN(skin.stickerScales[i]) || skin.stickerScales[i] < .5f || skin.stickerScales[i] > 1.5f) return "Некорректный масштаб или поворот наклейки";
            return null;
        }

        public bool SaveSettings(float sensitivity, float volume, float fov, float uiScale, out string error) => Change(p => {
            p.sensitivity = sensitivity; p.volume = volume; p.fov = fov; p.uiScale = uiScale; return null;
        }, out error);

        public bool SaveSettings(float sensitivity, float volume, float fov, float uiScale, int quality, int fps, bool fullscreen, bool vSync, out string error) => Change(p => {
            p.sensitivity = sensitivity; p.volume = volume; p.fov = fov; p.uiScale = uiScale;
            p.qualityLevel = quality; p.frameLimit = fps; p.fullscreen = fullscreen; p.vSync = vSync;
            return null;
        }, out error);

        public bool SaveSettings(GameProfile settings, out string error)
        {
            if (settings == null) { error = "Настройки отсутствуют"; return false; }
            return Change(p => {
                p.sensitivity = settings.sensitivity; p.volume = settings.volume; p.fov = settings.fov; p.uiScale = settings.uiScale;
                p.qualityLevel = settings.qualityLevel; p.frameLimit = settings.frameLimit; p.fullscreen = settings.fullscreen; p.vSync = settings.vSync;
                p.weaponVolume = settings.weaponVolume; p.footstepVolume = settings.footstepVolume; p.effectsVolume = settings.effectsVolume; p.ambientVolume = settings.ambientVolume;
                p.resolutionWidth = settings.resolutionWidth; p.resolutionHeight = settings.resolutionHeight;
                p.keyBindings = GameInput.NormalizeBindings(settings.keyBindings);
                p.invertY = settings.invertY; p.reducedFlashes = settings.reducedFlashes;
                p.scopeSensitivity = settings.scopeSensitivity; p.viewMotion = settings.viewMotion;
                p.interfaceVolume = settings.interfaceVolume; p.menuMusicVolume = settings.menuMusicVolume;
                p.crosshairColor = settings.crosshairColor; p.crosshairThickness = settings.crosshairThickness;
                return null;
            }, out error);
        }
    }
}
