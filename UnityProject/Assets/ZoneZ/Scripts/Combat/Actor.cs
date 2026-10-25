using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public sealed class Actor : MonoBehaviour
    {
        [Serializable] public struct DamageRecord
        {
            public Actor Attacker;
            public float Amount, Time;
            public DamageRecord(Actor attacker, float amount, float time) { Attacker = attacker; Amount = amount; Time = time; }
        }

        public int Id, Team, Kills, Deaths, Assists, Score;
        public string DisplayName;
        public bool Human, Alive, RemoteControlled;
        public float HP, MaxHP = 100, Armor = 100, SpeedMultiplier = 1, DamageMultiplier = 1, SpawnProtectedUntil;
        public Transform Aim;
        public WeaponController Weapon;
        public AbilityController Ability;
        public CharacterController Motor;
        public ActorVisual Visual;
        public MatchDirector Match;
        public Vector3 CurrentVelocity;
        public bool IsCrouching;
        public readonly List<DamageRecord> DamageHistory = new List<DamageRecord>();
        public float Shield => Ability ? Ability.Shield : 0;
        public bool IsSpawnProtected => Alive && Match != null && Match.MatchTime < SpawnProtectedUntil;
        public Vector3 EyePosition => Aim ? Aim.position : transform.position + Vector3.up * 1.62f;
        public Camera ViewCamera { get; private set; }
        public Transform Hitboxes { get; private set; }
        SphereCollider headCollider;
        public float LastDamagedAt { get; private set; } = -100;
        public Vector3 LastDamageDirection { get; private set; }
        public bool IsNetworkReplica => GameRoot.Instance?.Network?.IsClient == true;
        public bool IsBotControlled => !Human && !RemoteControlled && !IsNetworkReplica && GetComponent<BotBrain>() != null;

        public void Initialize(MatchDirector match, int id, string name, int team, bool human)
        {
            Match = match; Id = id; DisplayName = name; Team = team; Human = human;
            gameObject.name = (human ? "Player_" : "Bot_") + name;
            Motor = GetComponent<CharacterController>() ?? gameObject.AddComponent<CharacterController>();
            Motor.height = 1.8f; Motor.radius = .31f; Motor.center = Vector3.up * .9f;
            Motor.stepOffset = .32f; Motor.slopeLimit = 48; Motor.skinWidth = .035f; Motor.minMoveDistance = 0;
            Aim = new GameObject("Aim").transform; Aim.SetParent(transform, false); Aim.localPosition = Vector3.up * 1.62f;
            Hitboxes = new GameObject("Hitboxes").transform; Hitboxes.SetParent(transform, false);
            headCollider = HitZone.AddSphere(Hitboxes, this, "head", new Vector3(0, 1.64f, 0), .175f).GetComponent<SphereCollider>();
            HitZone.AddBox(Hitboxes, this, "chest", new Vector3(0, 1.28f, 0), new Vector3(.46f, .43f, .3f));
            HitZone.AddBox(Hitboxes, this, "stomach", new Vector3(0, .99f, 0), new Vector3(.43f, .22f, .28f));
            HitZone.AddBox(Hitboxes, this, "arms", new Vector3(-.32f, 1.25f, 0), new Vector3(.17f, .48f, .25f));
            HitZone.AddBox(Hitboxes, this, "arms", new Vector3(.32f, 1.25f, 0), new Vector3(.17f, .48f, .25f));
            HitZone.AddBox(Hitboxes, this, "legs", new Vector3(-.13f, .45f, 0), new Vector3(.2f, .86f, .27f));
            HitZone.AddBox(Hitboxes, this, "legs", new Vector3(.13f, .45f, 0), new Vector3(.2f, .86f, .27f));
            Visual = ActorVisual.Create(transform, team);
            if (Human)
            {
                Visual.gameObject.SetActive(false);
                var cameraObject = new GameObject("PlayerCamera"); cameraObject.transform.SetParent(Aim, false);
                ViewCamera = cameraObject.AddComponent<Camera>(); ViewCamera.tag = "MainCamera";
                ViewCamera.nearClipPlane = .035f; ViewCamera.farClipPlane = 500;
                ViewCamera.fieldOfView = GameRoot.Instance?.Profile?.fov ?? 60;
                WorldLook.ConfigureCamera(ViewCamera);
                cameraObject.AddComponent<AudioListener>();
                var controller = gameObject.AddComponent<PlayerMotor>(); controller.Owner = this;
            }
            else { var bot = gameObject.AddComponent<BotBrain>(); bot.Owner = this; }
            Weapon = gameObject.AddComponent<WeaponController>(); Weapon.Owner = this;
            var profile = GameRoot.Instance?.Profile;
            Weapon.EquipLoadout(profile?.primaryId ?? "weapon_ak47", profile?.pistolId ?? "weapon_glock");
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            if (Motor) Motor.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (Motor) { Motor.height = 1.8f; Motor.center = Vector3.up * .9f; Motor.enabled = true; }
            Alive = true; HP = MaxHP; Armor = 100; IsCrouching = false; CurrentVelocity = Vector3.zero;
            DamageHistory.Clear(); LastDamagedAt = -100;
            if (Hitboxes) { Hitboxes.gameObject.SetActive(true); Hitboxes.localScale = Vector3.one; }
            if (headCollider) headCollider.radius = .175f;
            if (Aim) { Aim.localPosition = Vector3.up * 1.62f; Aim.localRotation = Quaternion.identity; }
            SpawnProtectedUntil = Match && !Match.IsTeamMode ? CombatClock.AfterSeconds(Match.MatchTime, 2) : 0;
            if (Visual) Visual.SetDead(false);
            if (Weapon) Weapon.ResetForSpawn();
            GetComponent<PlayerMotor>()?.ResetLook();
            GetComponent<BotBrain>()?.ResetNavigation();
        }

        public void CancelSpawnProtection() => SpawnProtectedUntil = 0;

        // Human identifies the camera/input owner on this machine. A remote human
        // still uses the same movement simulation on the server, without a camera.
        public void SetNetworkRole(bool local, bool remote)
        {
            bool changed = Human != local || RemoteControlled != remote;
            Human = local; RemoteControlled = remote;
            var bot = GetComponent<BotBrain>();
            bool simulateBot = !local && !remote && !IsNetworkReplica;
            if (!bot && simulateBot) { bot = gameObject.AddComponent<BotBrain>(); bot.Owner = this; }
            if (bot)
            {
                bool enableBot = simulateBot && !bot.enabled;
                bot.enabled = simulateBot;
                if (!simulateBot) bot.StopNavigation();
                else if (Alive && (changed || enableBot)) bot.ResetNavigation();
            }
            var player = GetComponent<PlayerMotor>();
            if (!player && (local || remote)) { player = gameObject.AddComponent<PlayerMotor>(); player.Owner = this; }
            if (player) { player.enabled = local || remote; if (changed) player.ResetLook(); }
            if (local && !ViewCamera)
            {
                var cameraObject = new GameObject("PlayerCamera"); cameraObject.transform.SetParent(Aim, false);
                ViewCamera = cameraObject.AddComponent<Camera>(); ViewCamera.tag = "MainCamera";
                ViewCamera.nearClipPlane = .035f; ViewCamera.farClipPlane = 500;
                ViewCamera.fieldOfView = GameRoot.Instance?.Profile?.fov ?? 60;
                WorldLook.ConfigureCamera(ViewCamera);
                cameraObject.AddComponent<AudioListener>();
            }
            if (ViewCamera) ViewCamera.gameObject.SetActive(local);
            if (Visual) Visual.gameObject.SetActive(!local);
            if (changed) Weapon?.RefreshPresentation();
        }

        public void ApplyReplicaState(Vector3 position, float yaw, float pitch, bool alive, float hp, float armor,
            bool crouching, Vector3 velocity, float spawnProtectedUntil)
        {
            if (!IsNetworkReplica) return;
            bool wasAlive = Alive;
            float oldHP = HP;
            if (Motor) Motor.enabled = false;
            transform.position = position;
            if (!Human || alive && !wasAlive) transform.rotation = Quaternion.Euler(0, yaw, 0);
            Alive = alive; HP = hp; Armor = armor; CurrentVelocity = velocity; SpawnProtectedUntil = spawnProtectedUntil;
            SetStance(crouching, crouching ? 1.2f : 1.8f);
            if (!Human && Aim) Aim.localRotation = Quaternion.Euler(pitch, 0, 0);
            if (Hitboxes) Hitboxes.gameObject.SetActive(alive);
            if (Visual) { Visual.SetDead(!alive); Visual.SetAim(pitch); }
            if (!alive && wasAlive) Weapon?.OnOwnerDied();
            if (alive && !wasAlive)
            {
                GetComponent<PlayerMotor>()?.ResetLook();
                if (Weapon && Weapon.Presentation) Weapon.Presentation.gameObject.SetActive(true);
            }
            if (wasAlive && hp < oldHP)
            {
                LastDamagedAt = Time.unscaledTime;
                if (Human) AudioFX.Hurt();
            }
            // Replica colliders can be queried for presentation, but only the
            // server moves a CharacterController and resolves collisions.
        }

        public void SetStance(bool crouching, float height)
        {
            IsCrouching = crouching;
            if (Motor) { Motor.height = height; Motor.center = Vector3.up * height * .5f; }
            if (Aim) Aim.localPosition = Vector3.up * (height - .18f);
            if (Hitboxes) Hitboxes.localScale = new Vector3(1, height / 1.8f, 1);
            // SphereCollider uses the largest transform scale, so set radius explicitly
            // to keep its crouched top below the shortened movement capsule.
            if (headCollider) headCollider.radius = .175f * height / 1.8f;
            if (Visual) Visual.SetMotion(CurrentVelocity.magnitude, crouching);
        }

        public void ApplyDamage(float damage, Actor attacker, bool ultimate = false, bool ignoreArmor = false, string hitgroup = "chest", float armorRatio = 1.5f)
        {
            if (!CanBeDamagedBy(attacker) || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (Match.TryQueueDamage(() => ApplyDamage(damage, attacker, ultimate, ignoreArmor, hitgroup, armorRatio))) return;
            // Resolve AI difficulty once, after queued attacks reach their damage phase,
            // before shields and armor. Remote players may retain a disabled BotBrain.
            if (attacker && attacker.IsBotControlled)
                damage *= (GameRoot.Instance?.Catalog?.BotDifficulty ?? BotDifficultySettings.Defaults).DamageMultiplier;
            float incoming = Ability ? Ability.AbsorbDamage(damage) : damage;
            if (incoming <= 0)
            {
                if (attacker && attacker != this) attacker.Weapon?.ConfirmHit(false);
                return;
            }
            bool protectedZone = hitgroup != "legs";
            if (!ignoreArmor && protectedZone && Armor > 0)
            {
                float healthPart = incoming * Mathf.Clamp01(armorRatio * .5f);
                float armorCost = (incoming - healthPart) * .5f;
                if (armorCost <= Armor) { Armor -= armorCost; incoming = healthPart; }
                else { incoming -= Armor * 2; Armor = 0; }
            }
            float actual = Mathf.Min(HP, Mathf.Max(0, incoming));
            if (actual <= 0) return;
            HP = Mathf.Max(0, HP - actual); LastDamagedAt = Time.unscaledTime;
            LastDamageDirection = attacker ? (attacker.transform.position - transform.position).normalized : Vector3.zero;
            if (attacker && attacker != this)
            {
                DamageHistory.RemoveAll(x => !x.Attacker || Match.MatchTime - x.Time > 10);
                DamageHistory.Add(new DamageRecord(attacker, actual, Match.MatchTime));
                attacker.Ability?.OnDealtHealthDamage(actual, ultimate);
                attacker.Weapon?.ConfirmHit(HP <= 0);
            }
            if (Human) AudioFX.Hurt();
            if (HP <= 0) Die(attacker, ultimate);
        }

        public void Kill(Actor attacker, bool ultimate = true)
        {
            if (!CanBeDamagedBy(attacker)) return;
            if (Match.TryQueueDamage(() => Kill(attacker, ultimate))) return;
            HP = 0; LastDamagedAt = Time.unscaledTime;
            if (attacker && attacker != this) attacker.Weapon?.ConfirmHit(true);
            Die(attacker, ultimate);
        }

        bool CanBeDamagedBy(Actor attacker)
        {
            if (IsNetworkReplica || !Alive || IsSpawnProtected || !Match || Match.Phase != MatchPhase.Playing) return false;
            return !attacker || attacker == this || Match.IsEnemy(attacker, this);
        }

        void Die(Actor attacker, bool ultimate)
        {
            if (!Alive) return;
            Alive = false; HP = 0; CurrentVelocity = Vector3.zero;
            if (Hitboxes) Hitboxes.gameObject.SetActive(false);
            if (Motor) Motor.enabled = false;
            Weapon?.OnOwnerDied(); GetComponent<BotBrain>()?.StopNavigation();
            if (Visual) Visual.SetDead(true);
            Match.OnDeath(this, attacker, ultimate);
        }

        public void Heal(float amount) { if (!IsNetworkReplica && Alive && amount > 0 && !float.IsInfinity(amount)) HP = Mathf.Min(MaxHP, HP + amount); }

        void OnDestroy() => AudioFX.Forget(this);

        public float MovementSpeed
        {
            get
            {
                float baseSpeed = Weapon ? Weapon.MoveSpeed : 4.7f;
                float movementMultiplier = GameRoot.Instance?.Catalog?.MovementSpeedMultiplier ?? 1;
                return baseSpeed * movementMultiplier * SpeedMultiplier * (Ability ? Ability.SpeedFactor : 1);
            }
        }
    }
}
