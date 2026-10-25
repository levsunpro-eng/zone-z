using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace ZoneZ
{
    // Opt-in smoke fixture. No frames are yielded while existing actors are borrowed.
    public static class RuntimeAbilityAcceptance
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Vector3 Origin = new Vector3(5000, 5000, 5000);

        public static List<string> Run(GameRoot root)
        {
            if (!Application.isPlaying || !root || !root.Match || root.Match.IsTeamMode || root.Match.Phase != MatchPhase.Playing || root.Paused)
                throw new InvalidOperationException("Ability acceptance requires an unpaused playing Deathmatch.");
            Actor player = root.Match.Local, enemy = root.Match.Actors.FirstOrDefault(a => a && a != player && a.Alive);
            if (!player || !player.Alive || !enemy) throw new InvalidOperationException("Ability acceptance requires two live actors.");
            var originalObjects = new HashSet<GameObject>(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            var playerState = new ActorState(player); var enemyState = new ActorState(enemy);
            var added = new List<Component>();
            var noises = AudioFX.Noises.ToArray();
            try
            {
                var matchObject = new GameObject("Ability acceptance isolated clock"); matchObject.SetActive(false);
                var match = matchObject.AddComponent<MatchDirector>(); match.ModeId = "deathmatch"; match.Phase = MatchPhase.Playing;
                match.Actors.Add(player); match.Actors.Add(enemy);
                foreach (var actor in new[] { player, enemy })
                {
                    actor.Match = match; actor.Human = false; actor.Motor.enabled = false;
                    actor.Ability = actor.gameObject.AddComponent<AbilityController>(); added.Add(actor.Ability);
                    actor.Ability.Initialize(actor, "frag_grenade"); actor.Ability.enabled = false;
                    actor.Weapon = null;
                }
                player.Weapon = player.gameObject.AddComponent<WeaponController>(); player.Weapon.Owner = player; player.Weapon.enabled = false; added.Add(player.Weapon);
                var fixture = new Fixture(root, match, player, enemy);
                fixture.Execute();
                return fixture.Checks;
            }
            finally
            {
                // Remove new components before reinstating their original public references.
                foreach (var component in added) if (component) Object.DestroyImmediate(component);
                playerState.Restore(); enemyState.Restore();
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (go && !originalObjects.Contains(go)) { go.SetActive(false); Object.DestroyImmediate(go); }
                AudioFX.Noises.Clear(); AudioFX.Noises.AddRange(noises);
                Physics.SyncTransforms();
            }
        }

        sealed class Fixture
        {
            readonly GameRoot root;
            readonly MatchDirector match;
            readonly Actor player, enemy;
            readonly HashSet<string> abilities = new HashSet<string>(), loots = new HashSet<string>();
            public readonly List<string> Checks = new List<string>();
            public Fixture(GameRoot root, MatchDirector match, Actor player, Actor enemy) { this.root = root; this.match = match; this.player = player; this.enemy = enemy; }
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Ability acceptance: " + label); Checks.Add(label); Debug.Log("ABILITY PASS " + label); }
            void Near(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < .005f, label + " (" + actual + ")");
            void At(int tick) { match.Tick = tick; match.MatchTime = tick / 60f; }
            void Step(int ticks = 1) => At(match.Tick + ticks);
            void Reset()
            {
                At(600);
                foreach (var actor in new[] { player, enemy })
                {
                    actor.Alive = true; actor.HP = actor.MaxHP = 100; actor.Armor = 0; actor.SpawnProtectedUntil = 0;
                    actor.CurrentVelocity = Vector3.zero; actor.IsCrouching = false; actor.DamageHistory.Clear(); actor.Ability.ResetForSpawn(false);
                    actor.Motor.enabled = false; actor.Hitboxes.gameObject.SetActive(true); actor.Hitboxes.localScale = Vector3.one;
                    actor.transform.SetPositionAndRotation(Origin + (actor == enemy ? Vector3.forward * 10 : Vector3.zero), Quaternion.identity);
                    actor.Aim.localPosition = Vector3.up * 1.62f; actor.Aim.localRotation = Quaternion.identity;
                }
                Physics.SyncTransforms();
            }
            void Ready(Actor actor, string id)
            {
                actor.Ability.ResetForSpawn(false); actor.Ability.Initialize(actor, id);
                int required = root.Catalog.Ability(id).KillsRequired;
                for (int i = 0; i < required; i++) actor.Ability.RegisterKill(false);
                Check(actor.Ability.Ready, id + " charges at its catalog kill requirement");
            }
            void Activate(Actor actor, string id)
            {
                Ready(actor, id); Check(actor.Ability.Activate() && !actor.Ability.Ready, id + " activates and consumes one charge"); abilities.Add(id);
            }
            AbilityProjectile NewProjectile(Action fire)
            {
                var before = new HashSet<AbilityProjectile>(Object.FindObjectsByType<AbilityProjectile>(FindObjectsSortMode.None));
                fire();
                var created = Object.FindObjectsByType<AbilityProjectile>(FindObjectsSortMode.None).Where(p => !before.Contains(p)).ToArray();
                Check(created.Length == 1, "one accepted projectile attack creates one projectile"); return created[0];
            }
            GameObject Wall(Vector3 position, string material = "wood")
            {
                var wall = new GameObject("Ability acceptance " + material); wall.transform.position = position;
                wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, .12f);
                wall.AddComponent<SurfaceTag>().MaterialId = material; Physics.SyncTransforms(); return wall;
            }
            void Equip()
            {
                Transform aim = player.Aim; player.Aim = null;
                player.Weapon.EquipLoadout("weapon_ak47", "weapon_glock"); player.Weapon.ResetForSpawn(); player.Aim = aim;
                var motor = player.GetComponent<PlayerMotor>();
                if (motor) { Field(motor, "recoilPitch").SetValue(motor, 0f); Field(motor, "recoilYaw").SetValue(motor, 0f); }
                Step(120);
            }
            LootPickup Pickup(string id, bool consume = true)
            {
                var before = new HashSet<LootPickup>(Object.FindObjectsByType<LootPickup>(FindObjectsSortMode.None));
                Vector3 desired = player.EyePosition + player.Aim.forward * .9f;
                LootPickup.Create(id, desired - Vector3.up * .6f, match, CombatClock.AfterSeconds(match.MatchTime, 45));
                var pickup = Object.FindObjectsByType<LootPickup>(FindObjectsSortMode.None).Single(p => !before.Contains(p));
                if (consume)
                {
                    Check(LootPickup.FindNearby(player, 1.5f, true) == pickup, id + " can be targeted through the real pickup query");
                    LootPickup.TryNearest(player); Check(!pickup.gameObject.activeSelf, id + " is consumed once"); loots.Add(id);
                }
                return pickup;
            }
            public void Execute()
            {
                Grenade(); SpeedHealingAndShield(); Recon(); EMP(); TemporaryWeapons(); Absolute(); LootEffects();
                Check(abilities.Count == 10 && loots.Count == 6, "all 10 abilities and all 6 loot types exercised");
                Debug.Log("ZONEZ_ABILITIES_ACCEPTANCE_OK: " + string.Join(",", abilities.OrderBy(x => x)) + "; loot=" + string.Join(",", loots.OrderBy(x => x)));
            }

            void Grenade()
            {
                Reset(); Ready(player, "frag_grenade"); player.SpawnProtectedUntil = CombatClock.AfterSeconds(match.MatchTime, 2);
                int start = match.Tick;
                var grenade = NewProjectile(() => { Check(player.Ability.Activate(), "frag activation succeeds"); abilities.Add("frag_grenade"); });
                Check(!player.IsSpawnProtected && !player.Ability.Ready, "grenade activation cancels spawn protection and spends charge");
                At(start + 119); grenade.transform.position = enemy.transform.position + Vector3.up + Vector3.right;
                Call(grenade, "FixedUpdate"); Near(enemy.HP, 100, "grenade cannot explode one tick before fuse");
                player.Kill(enemy, false); Check(!player.Alive, "grenade owner can die before detonation");
                At(start + 120); grenade.transform.position = enemy.transform.position + Vector3.up + Vector3.right;
                Call(grenade, "FixedUpdate"); Near(enemy.HP, 4, "launched grenade survives owner death and detonates at 120 ticks");

                Reset(); var wall = Wall(enemy.transform.position + Vector3.up + Vector3.back);
                var blocked = NewProjectile(() => Activate(player, "frag_grenade"));
                blocked.transform.position = enemy.transform.position + Vector3.up + Vector3.back * 2;
                Call(blocked, "Explode"); Near(enemy.HP, 100, "wood blocks grenade blast despite bullet penetration");
                wall.SetActive(false);
            }

            void SpeedHealingAndShield()
            {
                Reset(); Activate(player, "adrenaline"); int start = match.Tick;
                player.Ability.ApplyLoot("haste"); Near(player.Ability.SpeedFactor, 1.35f, "adrenaline and haste use strongest speed only");
                At(start + 479); Near(player.Ability.SpeedFactor, 1.35f, "adrenaline lasts through penultimate tick");
                At(start + 480); Near(player.Ability.SpeedFactor, 1.25f, "haste remains after adrenaline expires exactly at 8 seconds");
                At(start + 720); Near(player.Ability.SpeedFactor, 1, "haste ends exactly at 12 seconds");

                Reset(); Ready(player, "medical_pulse"); Check(!player.Ability.Activate() && player.Ability.Ready, "full-health medical pulse preserves ready charge");
                player.HP = 20; Check(player.Ability.Activate(), "medical pulse activates while hurt"); abilities.Add("medical_pulse"); Near(player.HP, 80, "medical pulse restores 60 HP");
                Ready(player, "medical_pulse"); Check(player.Ability.Activate(), "medical pulse can heal partial missing health"); Near(player.HP, 100, "medical pulse caps at current maximum health");

                Reset(); Activate(player, "armor_shield"); player.Ability.ApplyLoot("protective_field");
                player.ApplyDamage(50, enemy, ignoreArmor: true); Near(player.HP, 100, "stacked shields absorb before HP"); Near(player.Shield, 45, "75 and 40 shields do not stack; incoming bot damage is reduced");
                player.ApplyDamage(80, enemy, ignoreArmor: true); Near(player.HP, 97, "shield exhaustion spills remaining reduced bot damage to HP");
                Check(player.Ability.ActiveRemaining == 0, "depleted ultimate shield finishes its effect");
                Activate(player, "armor_shield"); start = match.Tick;
                At(start + 599); Near(player.Shield, 75, "ultimate shield active before 10-second deadline");
                At(start + 600); Near(player.Shield, 0, "ultimate shield expires exactly at 10 seconds");
            }

            void Recon()
            {
                Reset(); var wall = Wall(Origin + Vector3.forward * 5 + Vector3.up);
                Activate(player, "recon_pulse"); int start = match.Tick;
                Check(player.Ability.Revealed.TryGetValue(enemy, out var snapshot) && snapshot == enemy.transform.position, "recon reveals enemy through wood immediately");
                for (int second = 1; second <= 4; second++)
                {
                    enemy.transform.position += Vector3.right;
                    At(start + second * 60 - 1); Call(player.Ability, "FixedUpdate"); Check(player.Ability.Revealed[enemy] == snapshot, "recon retains snapshot before second " + second);
                    At(start + second * 60); Call(player.Ability, "FixedUpdate"); snapshot = enemy.transform.position;
                    Check(player.Ability.Revealed[enemy] == snapshot, "recon refreshes on second " + second);
                }
                At(start + 300); Call(player.Ability, "Update"); Check(player.Ability.Revealed.Count == 0, "recon clears at five-second deadline"); wall.SetActive(false);
            }

            void EMP()
            {
                Reset(); Activate(enemy, "adrenaline"); enemy.Ability.ApplyLoot("protective_field");
                int start = match.Tick; var emp = NewProjectile(() => Activate(player, "emp_grenade"));
                At(start + 119); emp.transform.position = enemy.transform.position + Vector3.up + Vector3.right; Call(emp, "FixedUpdate");
                Check(!enemy.Ability.Blocked && enemy.Shield == 40, "EMP waits for two-second fuse");
                At(start + 120); emp.transform.position = enemy.transform.position + Vector3.up + Vector3.right; Call(emp, "FixedUpdate");
                Near(enemy.HP, 100, "EMP causes no HP damage"); Check(enemy.Ability.Blocked && enemy.Ability.LootId == null && enemy.Shield == 0 && enemy.Ability.SpeedFactor == 1, "EMP removes active ultimate and loot");
                enemy.Ability.Initialize(enemy, "medical_pulse"); enemy.HP = 20;
                for (int i = 0; i < root.Catalog.Ability("medical_pulse").KillsRequired; i++) enemy.Ability.RegisterKill(false);
                Check(!enemy.Ability.Activate() && enemy.Ability.Ready, "EMP blocks activation without consuming ready charge");
                enemy.Ability.ApplyLoot("haste"); Near(enemy.Ability.SpeedFactor, 1.25f, "new loot works during EMP lockout");
                Step(120); enemy.Ability.ApplyEMP(); int refreshed = match.Tick;
                At(refreshed + 299); Check(enemy.Ability.Blocked && !enemy.Ability.Activate(), "repeated EMP retains five-second lockout until final tick");
                At(refreshed + 300); Check(!enemy.Ability.Blocked && enemy.Ability.Activate(), "EMP refresh replaces deadline and unlocks at exactly five seconds");
                Near(enemy.HP, 80, "ready medical pulse remains usable after EMP");

                Reset(); enemy.Ability.ApplyLoot("protective_field"); var wall = Wall(enemy.transform.position + Vector3.up + Vector3.back);
                emp = NewProjectile(() => Activate(player, "emp_grenade")); emp.transform.position = enemy.transform.position + Vector3.up + Vector3.back * 2; Call(emp, "Explode");
                Check(!enemy.Ability.Blocked && enemy.Shield == 40, "wood blocks EMP blast"); wall.SetActive(false);
                emp = NewProjectile(() => Activate(player, "emp_grenade")); enemy.SpawnProtectedUntil = CombatClock.AfterSeconds(match.MatchTime, 2);
                emp.transform.position = enemy.transform.position + Vector3.up + Vector3.right; Call(emp, "Explode");
                Check(!enemy.Ability.Blocked && enemy.Shield == 40, "spawn protection blocks EMP stripping");

                foreach (string id in new[] { "adrenaline", "armor_shield", "recon_pulse", "absolute_shot", "underbarrel", "laser_burst", "rocket_launcher" })
                {
                    Reset(); Activate(player, id); player.Ability.ApplyLoot("haste"); player.Ability.ApplyEMP();
                    Check(player.Ability.ActiveRemaining == 0 && !player.Ability.HasTemporaryWeapon && player.Ability.Revealed.Count == 0 && player.Ability.LootId == null, "EMP cancels " + id);
                }
            }

            void TemporaryWeapons()
            {
                foreach (string id in new[] { "underbarrel", "rocket_launcher" })
                {
                    Reset(); player.HP = player.MaxHP = enemy.HP = enemy.MaxHP = 500;
                    var wall = Wall(player.EyePosition + Vector3.forward * .8f); int shots = root.Catalog.Ability(id).Shots;
                    Activate(player, id); var projectile = NewProjectile(() => Check(player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " fires its projectile"));
                    Check(player.Ability.RemainingShots == shots - 1, id + " consumes one temporary round");
                    for (int i = 0; i < 6 && !(bool)Field(projectile, "done").GetValue(projectile); i++) { Step(); Call(projectile, "FixedUpdate"); }
                    Check((bool)Field(projectile, "done").GetValue(projectile), id + " explodes on first wooden obstacle");
                    Near(enemy.HP, 500, id + " collision does not penetrate wood"); wall.SetActive(false);
                    if (shots > 1)
                    {
                        Check(!player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " cannot fire before temporary cadence"); Step(40);
                        projectile = NewProjectile(() => Check(player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " second shot succeeds"));
                    }
                    else
                    {
                        Activate(player, id); projectile = NewProjectile(() => Check(player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " accepts a new earned charge"));
                        Vector3 before = projectile.transform.position; Step(1200); Call(projectile, "FixedUpdate");
                        Check(projectile.transform.position != before, "launched rocket keeps flying beyond held-weapon lifetime and 15 seconds");
                    }
                    projectile.transform.position = enemy.transform.position + Vector3.up + Vector3.right; float hp = enemy.HP; Call(projectile, "Explode");
                    Check(enemy.HP < hp && enemy.HP > 0, id + " explosion applies positive radial damage");
                    Check(!player.Ability.HasTemporaryWeapon && !player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " disappears after its last round");
                }
                Reset(); enemy.HP = enemy.MaxHP = 500; Vector3 aim = (enemy.transform.position + Vector3.up * 1.28f - player.EyePosition).normalized;
                var wood = Wall(Origin + Vector3.forward * 5 + Vector3.up); Activate(player, "laser_burst");
                Check(player.Ability.FireTemporary(player.EyePosition, aim), "laser first ray fires"); Near(enemy.HP, 410, "laser deals 120 times wood retention without body multiplier");
                Check(!player.Ability.FireTemporary(player.EyePosition, aim), "laser cadence rejects repeated immediate input");
                wood.GetComponent<SurfaceTag>().MaterialId = "metal"; Step(39);
                Check(player.Ability.FireTemporary(player.EyePosition, aim), "laser second ray fires at cadence boundary"); Near(enemy.HP, 410, "metal blocks laser ray");
                Check(!player.Ability.HasTemporaryWeapon && player.Ability.RemainingShots == 0, "laser ends after exactly two rays"); wood.SetActive(false);

                foreach (string id in new[] { "underbarrel", "laser_burst", "rocket_launcher" })
                {
                    Reset(); Activate(player, id); int start = match.Tick;
                    At(start + 599); Check(player.Ability.HasTemporaryWeapon, id + " remains held before ten-second boundary");
                    At(start + 600); Check(!player.Ability.HasTemporaryWeapon && !player.Ability.FireTemporary(player.EyePosition, Vector3.forward), id + " expires at exactly ten seconds");
                }
            }

            void Absolute()
            {
                Reset(); Equip(); enemy.transform.position = player.transform.position + Vector3.forward * 2;
                player.Aim.rotation = Quaternion.LookRotation(enemy.transform.position + new Vector3(.13f, .45f, 0) - player.EyePosition);
                var wood = Wall(Origin + Vector3.forward + Vector3.up); Physics.SyncTransforms();
                Check(CombatTrace.Cast(player.EyePosition, player.Aim.forward, 5, player, true).Zone?.Group == "legs", "absolute fixture physically targets an enemy leg through wood");
                Activate(player, "absolute_shot"); int start = match.Tick; player.HP = 50; player.Ability.ApplyLoot("vampirism");
                enemy.Armor = 100; enemy.Ability.ApplyLoot("protective_field"); enemy.SpawnProtectedUntil = CombatClock.AfterSeconds(match.MatchTime, 2);
                Check(player.Weapon.TryFire(), "absolute protected-target bullet is emitted"); Check(enemy.Alive && enemy.HP == 100 && enemy.Shield == 40, "spawn protection defeats physical absolute bullet");
                Step(7); enemy.CancelSpawnProtection(); wood.GetComponent<SurfaceTag>().MaterialId = "metal";
                player.Aim.rotation = Quaternion.LookRotation(enemy.transform.position + new Vector3(.13f, .45f, 0) - player.EyePosition);
                Check(player.Weapon.TryFire(), "absolute metal-target bullet is emitted"); Check(enemy.Alive && enemy.HP == 100, "metal defeats physical absolute bullet");
                Step(7); wood.GetComponent<SurfaceTag>().MaterialId = "wood";
                player.Aim.rotation = Quaternion.LookRotation(enemy.transform.position + new Vector3(.13f, .45f, 0) - player.EyePosition);
                Check(CombatTrace.Cast(player.EyePosition, player.Aim.forward, 5, player, true).Zone?.Group == "legs", "absolute lethal shot remains aimed at the leg after previous recoil");
                Check(player.Weapon.TryFire(), "absolute leg bullet is emitted through wood"); Check(!enemy.Alive && enemy.Armor == 100, "absolute leg hit kills through wood regardless of armor and shield");
                Near(player.HP, 50, "absolute kill cannot trigger vampirism"); Check(player.Ability.Streak == 0, "absolute kill cannot recharge an ultimate");
                At(start + 359); Check(player.Ability.AbsoluteActive, "absolute effect active before six-second deadline"); At(start + 360); Check(!player.Ability.AbsoluteActive, "absolute effect expires at exactly six seconds"); wood.SetActive(false);
            }

            void LootEffects()
            {
                Reset(); Equip(); player.HP = 50; player.Ability.ApplyLoot("haste"); Pickup("medkit");
                Near(player.HP, 85, "medkit restores 35 HP"); Check(player.Ability.LootId == "haste", "medkit preserves occupied temporary loot slot");
                player.HP = 100; var unused = Pickup("medkit", false); LootPickup.TryNearest(player);
                Check(unused.gameObject.activeSelf && LootPickup.FindNearby(player, 1.5f, true) == null, "full-health medkit remains unconsumed"); unused.gameObject.SetActive(false);

                Reset(); Pickup("protective_field"); player.ApplyDamage(100, enemy, ignoreArmor: true);
                Near(player.HP, 80, "loot field absorbs 40 from 60 reduced bot damage"); Near(player.Shield, 0, "loot field does not restore consumed capacity");
                Reset(); Equip(); float speed = player.MovementSpeed; Pickup("haste"); Near(player.MovementSpeed, speed * 1.25f, "haste affects actual movement speed");
                Step(300); Pickup("haste"); Near(player.Ability.LootRemaining, 12, "repeated haste refreshes instead of adding durations"); Near(player.MovementSpeed, speed * 1.25f, "repeated haste does not multiply speed again");

                Reset(); Equip(); player.Weapon.Ammo = 5; Pickup("fast_reload"); player.Weapon.Reload(); int start = match.Tick;
                player.Ability.ApplyLoot("haste"); int reloadTicks = Mathf.CeilToInt(player.Weapon.Spec.ReloadSeconds * .7f * 60 - .0001f);
                At(start + reloadTicks - 1); Call(player.Weapon, "FixedUpdate"); Check(player.Weapon.Reloading && player.Weapon.Ammo == 5, "fast reload retains original snapshot before boundary");
                At(start + reloadTicks); Call(player.Weapon, "FixedUpdate"); Check(!player.Weapon.Reloading && player.Weapon.Ammo == 30, "fast reload keeps 0.70 duration after loot replacement");

                Reset(); Equip(); player.Aim.rotation = Quaternion.LookRotation(Vector3.right); Call(player.Weapon, "FixedUpdate"); float spread = player.Weapon.CurrentInaccuracy;
                Check(player.Weapon.TryFire(), "baseline recoil shot fires"); var motor = player.GetComponent<PlayerMotor>(); float recoil = (float)Field(motor, "recoilPitch").GetValue(motor);
                Equip(); Pickup("stabilizer"); Call(player.Weapon, "FixedUpdate"); Near(player.Weapon.CurrentInaccuracy, spread * .75f, "stabilizer scales actual weapon spread by 0.75");
                Check(player.Weapon.TryFire(), "stabilized recoil shot fires"); Near((float)Field(motor, "recoilPitch").GetValue(motor), recoil * .75f, "stabilizer scales actual camera recoil by 0.75");

                Reset(); player.HP = 50; Pickup("vampirism"); enemy.HP = 12; enemy.ApplyDamage(120, player, ignoreArmor: true);
                Near(player.HP, 53, "vampirism heals only 25 percent of remaining 12 HP");
                enemy.Alive = true; enemy.HP = 100; enemy.Hitboxes.gameObject.SetActive(true); enemy.Ability.ApplyLoot("protective_field"); player.HP = 50;
                enemy.ApplyDamage(20, player, ignoreArmor: true); Near(player.HP, 50, "shield-only damage cannot generate vampirism healing");
                player.ApplyDamage(10, player, ignoreArmor: true); Near(player.HP, 40, "self damage cannot generate vampirism healing");

                foreach (var pair in new[] { ("protective_field", 15), ("haste", 12), ("fast_reload", 12), ("stabilizer", 12), ("vampirism", 10) })
                {
                    Reset(); At(622); player.Ability.ApplyLoot(pair.Item1); int started = match.Tick;
                    At(started + pair.Item2 * 60 - 1); Check(player.Ability.LootId == pair.Item1, pair.Item1 + " remains before deadline");
                    At(started + pair.Item2 * 60); Check(player.Ability.LootId == null, pair.Item1 + " expires at exact fractional-start deadline");
                }
                Reset(); Activate(player, "adrenaline"); player.Ability.ApplyLoot("protective_field"); player.Kill(enemy, false);
                Check(player.Ability.ActiveRemaining == 0 && player.Ability.LootId == null && player.Shield == 0, "death clears active ultimate and temporary loot");
            }
        }

        // Public actor references are restored directly; original ability/weapon components were never edited.
        sealed class ActorState
        {
            readonly Actor actor;
            readonly Dictionary<FieldInfo, object> fields, movementFields;
            readonly Actor.DamageRecord[] history;
            readonly Component[] components;
            readonly Vector3 position, aimPosition, hitboxScale;
            readonly Quaternion rotation, aimRotation;
            readonly bool motorEnabled, hitboxesActive;
            readonly NavMeshAgent agent;
            readonly bool agentEnabled;
            readonly NavMeshPath agentPath;
            readonly Dictionary<FieldInfo, object> visualFields;
            public ActorState(Actor actor)
            {
                this.actor = actor; fields = Capture(actor); history = actor.DamageHistory.ToArray(); components = actor.GetComponents<Component>();
                position = actor.transform.position; rotation = actor.transform.rotation; aimPosition = actor.Aim.localPosition; aimRotation = actor.Aim.localRotation;
                hitboxScale = actor.Hitboxes.localScale; hitboxesActive = actor.Hitboxes.gameObject.activeSelf; motorEnabled = actor.Motor.enabled;
                var motor = actor.GetComponent<PlayerMotor>(); movementFields = motor ? Capture(motor) : null;
                visualFields = actor.Visual ? Capture(actor.Visual) : null;
                agent = actor.GetComponent<NavMeshAgent>(); agentEnabled = agent && agent.enabled;
                if (agentEnabled && agent.isOnNavMesh && agent.hasPath) agentPath = agent.path;
            }
            public void Restore()
            {
                if (!actor) return;
                RestoreFields(actor, fields); actor.Motor.enabled = false; actor.transform.SetPositionAndRotation(position, rotation);
                actor.Aim.localPosition = aimPosition; actor.Aim.localRotation = aimRotation;
                actor.Hitboxes.localScale = hitboxScale; actor.Hitboxes.gameObject.SetActive(hitboxesActive); actor.Motor.enabled = motorEnabled;
                actor.DamageHistory.Clear(); actor.DamageHistory.AddRange(history);
                if (movementFields != null) RestoreFields(actor.GetComponent<PlayerMotor>(), movementFields);
                if (visualFields != null) RestoreFields(actor.Visual, visualFields);
                if (agent) { agent.enabled = agentEnabled; if (agentEnabled && agent.isOnNavMesh && agentPath != null) agent.SetPath(agentPath); }
                foreach (var component in actor.GetComponents<Component>()) if (component && !components.Contains(component)) Object.DestroyImmediate(component);
            }
        }
        static Dictionary<FieldInfo, object> Capture(object value) => value.GetType().GetFields(Fields).Where(f => !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(value));
        static void RestoreFields(object target, Dictionary<FieldInfo, object> fields) { foreach (var pair in fields) pair.Key.SetValue(target, pair.Value); }
        static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Fields) ?? throw new MissingFieldException(target.GetType().Name, name);
        static void Call(object target, string method) => target.GetType().GetMethod(method, Fields).Invoke(target, null);
    }
}
