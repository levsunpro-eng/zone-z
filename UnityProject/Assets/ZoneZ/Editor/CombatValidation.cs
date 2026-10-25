using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZoneZ.Editor
{
    public static class CombatValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Vector3 Origin = new Vector3(3000, 3000, 3000);
        static readonly List<GameObject> created = new List<GameObject>();
        static MatchDirector match;
        static GameRoot root;

        public static void Run()
        {
            var previous = GameRoot.Instance;
            var existingTrails = new HashSet<CombatTracer>(Object.FindObjectsByType<CombatTracer>(FindObjectsSortMode.None));
            var existingEffects = new HashSet<AbilityEffects>(Object.FindObjectsByType<AbilityEffects>(FindObjectsSortMode.None));
            float oldScale = Time.timeScale;
            try
            {
                var rootObject = New("Combat validation root"); rootObject.SetActive(false);
                root = rootObject.AddComponent<GameRoot>(); root.Catalog = GameCatalog.Load(); GameRoot.Instance = root;
                match = rootObject.AddComponent<MatchDirector>(); match.Phase = MatchPhase.Playing; match.ModeId = "team";
                root.Match = match;
                TimeBoundaries();
                DamageAndShieldRules();
                SimultaneousDamage();
                TraceGeometry();
                WeaponTransitions();
                ReconSnapshots();
                LootExpiry();
                ArsenalSweep();
                Debug.Log("ZONEZ_COMBAT_VALIDATION_OK: armor, shields, death idempotence, material tracing, body zones, burst, R8, reload interruption, Zeus recharge, recon snapshots, loot expiry");
            }
            finally
            {
                foreach (var trail in Object.FindObjectsByType<CombatTracer>(FindObjectsSortMode.None))
                    if (!existingTrails.Contains(trail)) Object.DestroyImmediate(trail.gameObject);
                foreach (var effect in Object.FindObjectsByType<AbilityEffects>(FindObjectsSortMode.None))
                    if (!existingEffects.Contains(effect)) Object.DestroyImmediate(effect.gameObject);
                foreach (var go in created) if (go) Object.DestroyImmediate(go);
                created.Clear(); GameRoot.Instance = previous; Time.timeScale = oldScale;
            }
        }

        static void TimeBoundaries()
        {
            foreach (int duration in new[] { 2, 3, 5, 6, 8, 10, 12, 15, 30 })
                for (int tick = 0; tick < 36000; tick++)
                {
                    float deadline = CombatClock.AfterSeconds(tick / 60f, duration);
                    float expected = (tick + duration * 60) / 60f;
                    if (deadline != expected) throw new InvalidOperationException($"Combat deadline: tick {tick}, duration {duration}, expected {expected}, got {deadline}");
                }
            Near(CombatClock.AfterSeconds(0, .466667f), 28 / 60f, "rounded source shell duration retains intended tick count");
            var actor = Actor("Fractional protection boundary", 0);
            match.ModeId = "deathmatch"; match.MatchTime = 22 / 60f;
            actor.Respawn(Origin, Quaternion.identity);
            match.MatchTime = 141 / 60f; Check(actor.IsSpawnProtected, "protection lasts through penultimate tick");
            match.MatchTime = 142 / 60f; Check(!actor.IsSpawnProtected, "fractional-start protection ends exactly at tick boundary");
            actor.gameObject.SetActive(false); match.ModeId = "team"; match.MatchTime = 0;
        }

        static void SimultaneousDamage()
        {
            // This fixture validates the damage packet independently of round rewards.
            match.ModeId = "deathmatch";
            typeof(MatchDirector).GetField("roundResolutionPending", Private).SetValue(match, false);
            Actor first = Actor("Mutual first", 0), second = Actor("Mutual second", 1);
            var queue = (Queue<Action>)typeof(MatchDirector).GetField("damageBatch", Private).GetValue(match);
            queue.Enqueue(() => first.ApplyDamage(500, second, ignoreArmor: true));
            queue.Enqueue(() => second.ApplyDamage(500, first, ignoreArmor: true));
            Check(first.Alive && second.Alive, "both attacks are admitted before tick damage resolves");
            match.FlushDamage();
            Check(!first.Alive && !second.Alive && first.Kills == 1 && second.Kills == 1, "queued reciprocal hits preserve mutual kills");
            Check(first.Deaths == 1 && second.Deaths == 1, "mutual packet reports one death each");
            Check(first.Ability.Streak == 0 && second.Ability.Streak == 0, "death clears incomplete charge in mutual packet");
            first.gameObject.SetActive(false); second.gameObject.SetActive(false);
            match.ModeId = "team";
        }

        static void DamageAndShieldRules()
        {
            Actor attacker = Actor("Attacker", 0), victim = Actor("Victim", 1);
            victim.ApplyDamage(36, attacker, armorRatio: 1.55f);
            Near(victim.HP, 72.1f, "AK chest health"); Near(victim.Armor, 95.95f, "AK chest armor");
            Reset(victim); victim.ApplyDamage(27, attacker, armorRatio: 1.55f);
            Near(victim.HP, 79.075f, "AK chest through one wood health");
            Reset(victim); victim.ApplyDamage(27, attacker, hitgroup: "legs", armorRatio: 1.55f);
            Near(victim.HP, 73, "AK leg health"); Near(victim.Armor, 100, "legs preserve armor");
            Reset(victim); victim.Armor = 1; victim.ApplyDamage(36, attacker, armorRatio: 1.55f);
            Near(victim.HP, 66, "armor exhaustion health"); Near(victim.Armor, 0, "armor exhaustion clamps");
            Reset(victim); victim.ApplyDamage(500, attacker, ignoreArmor: true);
            Check(victim.Deaths == 1 && attacker.Kills == 1, "one lethal damage event");
            victim.ApplyDamage(500, attacker); victim.Kill(attacker);
            Check(victim.Deaths == 1 && attacker.Kills == 1, "pellets cannot duplicate death");
            Near(victim.DamageHistory[0].Amount, 100, "overkill records only remaining HP");
            Reset(victim); victim.Team = 0; victim.ApplyDamage(36, attacker);
            Near(victim.HP, 100, "friendly fire disabled"); victim.Team = 1;

            victim.Ability.Initialize(victim, "armor_shield");
            int needed = root.Catalog.Ability("armor_shield").KillsRequired;
            for (int i = 0; i < needed; i++) victim.Ability.RegisterKill(false);
            Check(victim.Ability.Activate(), "shield charge activates");
            victim.Ability.ApplyLoot("protective_field");
            victim.SpawnProtectedUntil = match.MatchTime + 2;
            victim.ApplyDamage(50, attacker); victim.Kill(attacker, true);
            Near(victim.Shield, 75, "spawn protection preserves shield"); Near(victim.HP, 100, "spawn protection blocks absolute kill");
            victim.CancelSpawnProtection(); victim.ApplyDamage(50, attacker);
            Near(victim.Shield, 25, "75 and 40 shields do not stack"); Near(victim.HP, 100, "shield absorbs before armor");
            victim.ApplyDamage(30, attacker, armorRatio: 1.55f);
            Near(victim.Shield, 0, "remaining shield consumed"); Near(victim.HP, 96.125f, "shield spill uses armor");
            match.MatchTime += 11;
            Near(victim.Shield, 0, "weaker shield does not return after stronger expires");
            victim.Kill(attacker, true); Near(victim.HP, 0, "absolute kill bypasses armor");
            Reset(victim); victim.HP = 12; victim.Armor = 0;
            attacker.HP = 50; attacker.Ability.ApplyLoot("vampirism");
            victim.ApplyDamage(120, attacker, ultimate: true, ignoreArmor: true);
            Near(attacker.HP, 53, "lifesteal uses actual HP even for non-absolute ultimate damage");
            attacker.ApplyDamage(10, attacker, ignoreArmor: true);
            Near(attacker.HP, 43, "self damage cannot generate lifesteal");
            attacker.gameObject.SetActive(false); victim.gameObject.SetActive(false);
        }

        static void TraceGeometry()
        {
            match.MatchTime = 0;
            Actor owner = Actor("Trace owner", 0), victim = Actor("Trace target", 1);
            owner.transform.position = Origin;
            victim.transform.position = Origin + Vector3.forward * 10;
            victim.Motor = victim.gameObject.AddComponent<CharacterController>();
            victim.Motor.height = 1.8f; victim.Motor.center = Vector3.up * .9f; victim.Motor.radius = .31f;
            var chest = HitZone.AddBox(victim.transform, victim, "chest", Vector3.up * 1.28f, new Vector3(.46f, .43f, .3f));
            HitZone.AddSphere(victim.transform, victim, "head", Vector3.up * 1.64f, .175f);
            HitZone.AddBox(victim.transform, victim, "legs", new Vector3(.13f, .45f, 0), new Vector3(.2f, .86f, .27f));
            Physics.SyncTransforms();
            Vector3 start = Origin + Vector3.up * 1.28f;
            var hit = CombatTrace.Cast(start, Vector3.forward, 15, owner, true);
            Check(hit.Target == victim && hit.Zone == chest, "movement capsule does not mask chest");
            Near(hit.Distance, 9.85f, "body box contact distance", .002f);
            Check(CombatTrace.Cast(Origin + Vector3.up * 1.64f, Vector3.forward, 15, owner, true).Zone.Group == "head", "head region");
            Check(CombatTrace.Cast(Origin + new Vector3(.13f, .45f, 0), Vector3.forward, 15, owner, true).Zone.Group == "legs", "leg region");
            Check(!CombatTrace.Cast(start, Vector3.forward, 9.84f, owner, true).Hit, "range excludes distant target");
            Check(CombatTrace.Cast(start, Vector3.forward, hit.Distance, owner, true).Target == victim, "inclusive maximum range");

            var woodGroup = New("One compound wooden obstacle");
            GameObject woodA = Solid("Wood front", 2, "wood", woodGroup);
            GameObject woodB = Solid("Wood back", 3, "wood", woodGroup);
            GameObject secondWood = Solid("Second wooden obstacle", 5, "wood");
            Physics.SyncTransforms();
            hit = CombatTrace.Cast(start, Vector3.forward, 15, owner, true);
            Check(hit.Target == victim, "bullet traverses wood"); Near(hit.Retention, .5625f, "one penalty per logical wood obstacle");
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, false).Target == null, "wood blocks Zeus and sight");
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, false, .16f).Target == null, "wood blocks knife sweep");
            GameObject metal = Solid("Metal layer", 6, "metal", woodGroup);
            Physics.SyncTransforms();
            hit = CombatTrace.Cast(start, Vector3.forward, 15, owner, true);
            Check(hit.Collider == metal.GetComponent<Collider>() && hit.Target == null, "metal stops bullet despite shared logical group");
            metal.GetComponent<SurfaceTag>().MaterialId = "glass";
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, true).Target == null, "glass stops bullets");
            Object.DestroyImmediate(metal.GetComponent<SurfaceTag>());
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, true).Target == null, "unmarked solid stops bullets");
            metal.transform.position = start;
            Physics.SyncTransforms();
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, true).Collider == metal.GetComponent<Collider>(), "cast originating inside solid cannot escape through wall");

            woodA.SetActive(false); woodB.SetActive(false); secondWood.SetActive(false); metal.SetActive(false);
            Actor friend = Actor("Friendly blocker", 0); friend.transform.position = Origin + Vector3.forward * 5;
            HitZone.AddBox(friend.transform, friend, "chest", Vector3.up * 1.28f, new Vector3(.46f, .43f, .3f));
            Physics.SyncTransforms();
            Check(CombatTrace.Cast(start, Vector3.forward, 15, owner, true).Target == friend, "first friendly stops bullet before enemy");
            owner.gameObject.SetActive(false); victim.gameObject.SetActive(false); friend.gameObject.SetActive(false);
        }

        static void WeaponTransitions()
        {
            var owner = Actor("Weapon state owner", 0);
            owner.transform.position = Origin + Vector3.right * 100;
            owner.Aim = New("Weapon aim").transform; owner.Aim.SetParent(owner.transform, false); owner.Aim.localPosition = Vector3.up * 1.62f;
            var weapon = owner.gameObject.AddComponent<WeaponController>(); owner.Weapon = weapon; weapon.Owner = owner;
            UseWeapon(weapon, "weapon_mp5sd");
            Check(!weapon.IsAlt, "integral silencer is not an alternate fire mode");
            Near(weapon.MoveSpeed, 235 * .01905f, "MP5 uses normal source speed despite permanent silencer");
            UseWeapon(weapon, "weapon_m4a1_silencer");
            Check(weapon.IsAlt, "detachable suppressor starts in alternate parameter state");
            UseWeapon(weapon, "weapon_glock"); match.MatchTime = 0;
            weapon.Alternate(); match.MatchTime = 1;
            int initial = weapon.Ammo;
            Check(weapon.TryFire(), "burst begins");
            float interval = weapon.Spec.Value("m_flTimeBetweenBurstShots");
            match.MatchTime += interval; Tick(weapon); match.MatchTime += interval; Tick(weapon);
            Check(weapon.Ammo == initial - 3, "Glock burst consumes three rounds");
            match.MatchTime = 3; weapon.Ammo = 2; Check(weapon.TryFire(), "short burst begins");
            match.MatchTime += interval; Tick(weapon); match.MatchTime += interval; Tick(weapon);
            Check(weapon.Ammo == 0, "short burst never invents third round");

            UseWeapon(weapon, "weapon_revolver"); match.MatchTime = 10; initial = weapon.Ammo;
            Check(weapon.TryFire(), "R8 windup starts"); weapon.CancelPrimaryWindup(); match.MatchTime += .3f; Tick(weapon);
            Check(weapon.Ammo == initial, "cancelled windup consumes no round");
            Check(weapon.TryFire(), "R8 second windup starts"); match.MatchTime += .21f; Tick(weapon);
            Check(weapon.Ammo == initial - 1, "R8 fires one round after windup");

            UseWeapon(weapon, "weapon_nova"); match.MatchTime = 20; weapon.Ammo = 2; weapon.Reserve = 3;
            weapon.Reload(); match.MatchTime += .51f; Tick(weapon);
            Check(!weapon.TryFire(), "fire waits for current shell insertion");
            match.MatchTime += .48f; Tick(weapon);
            Check(!weapon.Reloading && weapon.Ammo == 2 && weapon.Reserve == 2, "single click fires inserted shell and preserves ammo accounting");

            UseWeapon(weapon, "weapon_ak47"); match.MatchTime = 30; weapon.Ammo = 5; weapon.Reserve = 90;
            weapon.Reload(); match.MatchTime += 1; Tick(weapon);
            Call(weapon, "Add", "pistol", root.Catalog.Weapon("weapon_glock"));
            Transform savedAim = owner.Aim; owner.Aim = null;
            weapon.SelectSlot("pistol"); weapon.SelectSlot("primary"); owner.Aim = savedAim;
            Check(!weapon.Reloading && weapon.Ammo == 5 && weapon.Reserve == 90, "switch cancels magazine reload without transfer");
            match.MatchTime = 34; weapon.Reload(); match.MatchTime += weapon.Spec.ReloadSeconds + .01f; Tick(weapon);
            Check(weapon.Ammo == 30 && weapon.Reserve == 65, "magazine reload conserves ammunition");

            UseWeapon(weapon, "weapon_taser"); match.MatchTime = 40;
            Check(weapon.TryFire() && weapon.Ammo == 0, "Zeus spends one charge");
            match.MatchTime = 69.99f; Tick(weapon); Check(weapon.Ammo == 0 && weapon.ZeusRechargeRemaining > 0, "Zeus unavailable before thirty seconds");
            match.MatchTime = 70; Tick(weapon); Check(weapon.Ammo == 1 && weapon.ZeusRechargeRemaining == 0, "Zeus recharges exactly at thirty seconds");

            UseWeapon(weapon, "weapon_taser"); match.MatchTime = 247 / 60f;
            Check(weapon.TryFire(), "fractional-start Zeus discharge");
            match.MatchTime = 2046 / 60f; Tick(weapon); Check(weapon.Ammo == 0, "fractional-start Zeus remains empty before boundary");
            match.MatchTime = 2047 / 60f;
            Check(weapon.ZeusRechargeRemaining == 0 && weapon.TryFire(), "fractional-start Zeus can fire at exact boundary before controller update");

            UseWeapon(weapon, "weapon_nova"); match.MatchTime = 100; weapon.Ammo = 0; weapon.Reserve = 2;
            owner.Ability.ApplyLoot("fast_reload"); weapon.Reload(); owner.Ability.ApplyLoot("haste");
            match.MatchTime = 100.351f; Tick(weapon); match.MatchTime = 100.69f; Tick(weapon);
            Check(weapon.Ammo == 1 && weapon.Reserve == 1, "shell reload retains speed snapshot after loot replacement");
            Call(weapon, "CancelReload"); Call(weapon, "Add", "knife", root.Catalog.Weapon("weapon_knife"));
            owner.Ability.Initialize(owner, "laser_burst");
            for (int i = 0; i < root.Catalog.Ability("laser_burst").KillsRequired; i++) owner.Ability.RegisterKill(false);
            Check(owner.Ability.Activate(), "temporary weapon activates");
            weapon.SelectSlot("knife"); Check(weapon.Slot == "primary", "temporary weapon locks underlying normal slot");
            match.MatchTime += 10.1f; Transform aim = owner.Aim; owner.Aim = null; weapon.SelectSlot("knife"); owner.Aim = aim;
            Check(weapon.Slot == "knife", "normal slots unlock after temporary weapon expires");

            UseWeapon(weapon, "weapon_glock"); owner.Ability.ResetForSpawn(false); match.MatchTime = 200;
            weapon.Alternate(); match.MatchTime = 201; Check(weapon.TryFire(), "burst starts before temporary weapon activation");
            int burstAmmo = weapon.Ammo;
            for (int i = 0; i < root.Catalog.Ability("laser_burst").KillsRequired; i++) owner.Ability.RegisterKill(false);
            Check(owner.Ability.Activate(), "temporary weapon activates during pending burst");
            typeof(AbilityController).GetField("nextFire", Private).SetValue(owner.Ability, 202f);
            Check(!weapon.TryFire(), "temporary cadence can reject shot without normal weapon fallback");
            Check((int)typeof(WeaponController).GetField("burstRemaining", Private).GetValue(weapon) == 0 && weapon.Ammo == burstAmmo, "entering temporary weapon cancels pending normal burst before first attempt");
            owner.gameObject.SetActive(false);
        }

        static void ReconSnapshots()
        {
            match.ModeId = "team"; match.MatchTime = 44 / 60f;
            var owner = Actor("Recon owner", 0);
            var target = Actor("Recon target", 1);
            owner.transform.position = Origin + Vector3.right * 200;
            target.transform.position = owner.transform.position + Vector3.forward * 8;
            owner.Ability.Initialize(owner, "recon_pulse");
            for (int i = 0; i < root.Catalog.Ability("recon_pulse").KillsRequired; i++) owner.Ability.RegisterKill(false);
            Check(owner.Ability.Activate(), "recon activates");
            Check(owner.Ability.Revealed.TryGetValue(target, out var snapshot) && snapshot == target.transform.position, "recon publishes t=0 snapshot immediately without Update");
            for (int second = 1; second <= 4; second++)
            {
                target.transform.position += Vector3.right;
                match.MatchTime = (44 + second * 60 - 1) / 60f;
                Call(owner.Ability, "FixedUpdate");
                Check(owner.Ability.Revealed[target] == snapshot, "recon retains snapshot before second " + second);
                match.MatchTime = (44 + second * 60) / 60f;
                Call(owner.Ability, "FixedUpdate");
                snapshot = target.transform.position;
                Check(owner.Ability.Revealed[target] == snapshot, "recon refreshes on fixed boundary " + second + " without render Update");
            }
            match.MatchTime = 344 / 60f; target.transform.position += Vector3.right;
            Call(owner.Ability, "FixedUpdate");
            Check(owner.Ability.ActiveRemaining == 0 && (!owner.Ability.Revealed.TryGetValue(target, out snapshot) || snapshot != target.transform.position), "recon ends at five seconds without a fifth refresh");
            Call(owner.Ability, "Update");
            Check(owner.Ability.Revealed.Count == 0, "expired recon markers are cleared before rendering");
            owner.gameObject.SetActive(false); target.gameObject.SetActive(false);
        }

        static void LootExpiry()
        {
            match.ModeId = "deathmatch";
            var owner = Actor("Loot expiry owner", 0); owner.HP = 50;
            owner.transform.position = Origin + Vector3.right * 240;
            owner.Aim = New("Loot expiry aim").transform; owner.Aim.SetParent(owner.transform, false); owner.Aim.localPosition = Vector3.up * 1.62f;
            var pickup = New("Deadline medkit").AddComponent<LootPickup>(); pickup.Id = "medkit";
            pickup.transform.position = owner.EyePosition + Vector3.forward;
            typeof(LootPickup).GetField("match", Private).SetValue(pickup, match);
            float deadline = CombatClock.AfterSeconds(22 / 60f, 30);
            typeof(LootPickup).GetField("expires", Private).SetValue(pickup, deadline);
            Physics.SyncTransforms();
            match.MatchTime = 1821 / 60f;
            Check(LootPickup.FindNearby(owner, 1.5f, true) == pickup, "visible aimed medkit is available one tick before expiry");
            match.MatchTime = deadline;
            Check(pickup.gameObject.activeInHierarchy && LootPickup.FindNearby(owner, 1.5f, true) == null, "loot becomes unavailable exactly at deadline before its Update runs");
            LootPickup.TryNearest(owner);
            Near(owner.HP, 50, "deadline pickup cannot apply expired medkit");
            Check(pickup.gameObject.activeInHierarchy, "rejected expired pickup does not depend on delayed destruction");
            owner.gameObject.SetActive(false); pickup.gameObject.SetActive(false); match.ModeId = "team";
        }

        static void ArsenalSweep()
        {
            match.ModeId = "team"; match.Phase = MatchPhase.Playing;
            var owner = Actor("Arsenal owner", 0);
            owner.transform.position = Origin + Vector3.right * 400;
            owner.Aim = New("Arsenal aim").transform; owner.Aim.SetParent(owner.transform, false); owner.Aim.localPosition = Vector3.up * 1.62f;
            owner.Weapon = owner.gameObject.AddComponent<WeaponController>(); owner.Weapon.Owner = owner;
            var target = Actor("Arsenal target", 1); target.transform.position = owner.transform.position + Vector3.forward * 2;
            var zone = HitZone.AddBox(target.transform, target, "chest", Vector3.up * 1.62f, new Vector3(20, 20, .1f));
            target.MaxHP = 100000; target.HP = target.MaxHP; target.Armor = 0;
            Physics.SyncTransforms();
            var existingTrails = new HashSet<CombatTracer>(Object.FindObjectsByType<CombatTracer>(FindObjectsSortMode.None));
            int count = 0;
            foreach (var spec in root.Catalog.Weapons)
            {
                if (spec.Slot != "primary" && spec.Slot != "pistol") continue;
                var weapon = owner.Weapon;
                CatalogLoadout(owner, spec);
                Check(weapon.Spec == spec && weapon.Ammo == spec.Magazine && weapon.Reserve == spec.Reserve, spec.Id + " catalog loadout initializes ammunition");
                CheckArsenalAccuracy(owner, spec);
                target.HP = target.MaxHP; target.DamageHistory.Clear();
                float started = match.MatchTime;
                float interval = spec.Value("m_flCycleTime", weapon.IsAlt, spec.FireInterval);
                Check(weapon.TryFire(), spec.Id + " accepts primary attack");
                CompleteWindup(weapon, started);
                Check(weapon.Ammo == spec.Magazine - 1, spec.Id + " primary spends exactly one cartridge");
                CheckArsenalHits(target, spec, spec.Pellets);
                match.MatchTime = started + interval * .9f;
                Check(!weapon.TryFire() && weapon.Ammo == spec.Magazine - 1, spec.Id + " cannot fire before source cycle");
                match.MatchTime = started + interval;
                Check(weapon.TryFire(), spec.Id + " fires again at source cycle");
                CompleteWindup(weapon, match.MatchTime);
                Check(weapon.Ammo == spec.Magazine - 2, spec.Id + " second attack spends one cartridge");
                CheckArsenalHits(target, spec, spec.Pellets * 2);
                Vector3 firstEndpoint = Vector3.zero; bool haveEndpoint = false, differentEndpoints = false;
                foreach (var trail in Object.FindObjectsByType<CombatTracer>(FindObjectsSortMode.None))
                {
                    if (existingTrails.Contains(trail)) continue;
                    Vector3 endpoint = trail.GetComponent<LineRenderer>().GetPosition(1);
                    if (!haveEndpoint) { firstEndpoint = endpoint; haveEndpoint = true; }
                    else if ((endpoint - firstEndpoint).sqrMagnitude > .000001f) differentEndpoints = true;
                }
                Check(differentEndpoints, spec.Id + " spread produces distinct physical impact positions");
                CheckArsenalReload(weapon, spec);
                CheckArsenalAlternate(owner, target, spec);

                // Full-magazine cadence uses actual 60 Hz requests, including noninteger tick intervals.
                zone.gameObject.SetActive(false); CatalogLoadout(owner, spec);
                int firstShotTick = -1, lastShotTick = -1, fired = 0;
                int maxTicks = Mathf.CeilToInt((spec.Magazine * spec.FireInterval + 4) * 60);
                for (int tick = 0; tick <= maxTicks && weapon.Ammo > 0; tick++)
                {
                    match.MatchTime = (600 + tick) / 60f;
                    int before = weapon.Ammo; weapon.TryFire(); Tick(weapon);
                    int consumed = before - weapon.Ammo;
                    Check(consumed >= 0 && consumed <= 1, spec.Id + " consumes at most one cartridge per primary tick");
                    if (consumed == 0) continue;
                    if (firstShotTick < 0) firstShotTick = tick;
                    lastShotTick = tick; fired += consumed;
                }
                Check(fired == spec.Magazine && weapon.Ammo == 0, spec.Id + " full magazine completes at 60 Hz");
                float expected = (spec.Magazine - 1) * spec.Value("m_flCycleTime", weapon.IsAlt, spec.FireInterval);
                Near((lastShotTick - firstShotTick) / 60f, expected, spec.Id + " full magazine cadence has no accumulated tick rounding", 1 / 60f + .002f);
                zone.gameObject.SetActive(true); Physics.SyncTransforms();
                // Keep the editor fixture bounded even for 150-round machine guns.
                foreach (var trail in Object.FindObjectsByType<CombatTracer>(FindObjectsSortMode.None))
                    if (!existingTrails.Contains(trail)) Object.DestroyImmediate(trail.gameObject);
                Debug.Log("ZONEZ_ARSENAL_PASS " + spec.Id + " damage/pellets, stance spread, cycle, full magazine, reload, supported alternate");
                count++;
            }
            Check(count == 34, "all 34 firearms receive functional acceptance checks");
            owner.gameObject.SetActive(false); target.gameObject.SetActive(false);
        }

        static void CatalogLoadout(Actor owner, WeaponSpec spec)
        {
            match.MatchTime = 0; owner.Ability.ResetForSpawn(false); owner.CurrentVelocity = Vector3.zero; owner.IsCrouching = false;
            // Presentation construction is covered separately; use the public loadout path for combat state.
            Transform aim = owner.Aim; owner.Aim = null;
            owner.Weapon.EquipLoadout(spec.Slot == "primary" ? spec.Id : "weapon_ak47", spec.Slot == "pistol" ? spec.Id : "weapon_glock");
            owner.Weapon.ResetForSpawn();
            owner.Weapon.SelectSlot(spec.Slot); owner.Aim = aim;
            match.MatchTime = 10;
        }

        static void CheckArsenalAccuracy(Actor owner, WeaponSpec spec)
        {
            var weapon = owner.Weapon; Tick(weapon); float standing = weapon.CurrentInaccuracy;
            Check(!float.IsNaN(standing) && !float.IsInfinity(standing) && standing > 0, spec.Id + " standing spread is finite and nonzero");
            owner.IsCrouching = true; Tick(weapon); Check(weapon.CurrentInaccuracy <= standing, spec.Id + " crouching does not worsen accuracy");
            owner.IsCrouching = false; owner.CurrentVelocity = Vector3.right * owner.MovementSpeed; Tick(weapon);
            Check(weapon.CurrentInaccuracy > standing, spec.Id + " movement worsens accuracy");
            owner.CurrentVelocity = Vector3.up * 5; owner.Motor = owner.gameObject.AddComponent<CharacterController>(); Tick(weapon);
            Check(weapon.CurrentInaccuracy > standing, spec.Id + " airborne movement worsens accuracy");
            Object.DestroyImmediate(owner.Motor); owner.Motor = null; owner.CurrentVelocity = Vector3.zero; Tick(weapon);
        }

        static void CompleteWindup(WeaponController weapon, float started)
        {
            if (weapon.Spec.Id != "weapon_revolver") return;
            int before = weapon.Ammo;
            float windup = weapon.Spec.Overrides.Value<float?>("primaryWindupSeconds") ?? .2f;
            match.MatchTime = CombatClock.AfterSeconds(started, windup) - 1 / 60f; Tick(weapon);
            Check(weapon.Ammo == before, "R8 holds cartridge before windup deadline");
            match.MatchTime = CombatClock.AfterSeconds(started, windup); Tick(weapon);
        }

        static void CheckArsenalHits(Actor target, WeaponSpec spec, int expectedHits)
        {
            Check(target.DamageHistory.Count == expectedHits, spec.Id + " raycast hit count matches bullets/pellets");
            foreach (var hit in target.DamageHistory)
                Check(hit.Amount >= spec.Damage * .8f && hit.Amount <= spec.Damage + .01f, spec.Id + " close unarmored chest damage remains near canonical per-projectile damage");
            Check(target.HP < target.MaxHP && target.Alive, spec.Id + " shots apply positive HP damage without fixture overkill");
        }

        static void CheckArsenalReload(WeaponController weapon, WeaponSpec spec)
        {
            match.MatchTime = 20; int before = weapon.Ammo, total = weapon.Ammo + weapon.Reserve;
            weapon.Reload(); Check(weapon.Reloading, spec.Id + " reload starts with depleted magazine");
            int startTick = 1200;
            if (!spec.SingleShellReload)
            {
                int finishTick = startTick + Mathf.CeilToInt(spec.ReloadSeconds * 60 - .0001f);
                match.MatchTime = (finishTick - 1) / 60f; Tick(weapon);
                Check(weapon.Reloading && weapon.Ammo == before, spec.Id + " magazine does not refill before reload deadline");
                match.MatchTime = finishTick / 60f; Tick(weapon);
            }
            else
            {
                int previousAmmo = weapon.Ammo;
                for (int tick = startTick + 1; tick <= startTick + 600 && weapon.Reloading; tick++)
                {
                    match.MatchTime = tick / 60f; Tick(weapon);
                    Check(weapon.Ammo >= previousAmmo && weapon.Ammo <= previousAmmo + 1, spec.Id + " shell reload inserts one cartridge at a time");
                    Check(weapon.Ammo + weapon.Reserve == total, spec.Id + " shell reload conserves ammunition"); previousAmmo = weapon.Ammo;
                }
            }
            Check(!weapon.Reloading && weapon.Ammo == spec.Magazine && weapon.Ammo + weapon.Reserve == total, spec.Id + " reload completes with conserved ammunition");
        }

        static void CheckArsenalAlternate(Actor owner, Actor target, WeaponSpec spec)
        {
            var weapon = owner.Weapon; match.MatchTime = 40; target.DamageHistory.Clear(); target.HP = target.MaxHP;
            if (spec.HasScope)
            {
                int levels = Mathf.RoundToInt(spec.Value("m_nZoomLevels")); float previousFov = 180;
                for (int level = 0; level < levels; level++)
                {
                    weapon.Alternate(); Check(weapon.Scoped && weapon.ScopeFov < previousFov, spec.Id + " cycles into narrower scope level"); previousFov = weapon.ScopeFov;
                }
                weapon.Alternate(); Check(!weapon.Scoped, spec.Id + " scope cycle returns to unscoped");
                weapon.Alternate(); Check(weapon.TryFire(), spec.Id + " scoped weapon fires"); CheckArsenalHits(target, spec, spec.Pellets);
            }
            else if (spec.HasBurst)
            {
                weapon.Alternate(); Check(weapon.BurstMode, spec.Id + " burst mode enables");
                match.MatchTime = 41; int before = weapon.Ammo; Check(weapon.TryFire(), spec.Id + " burst starts");
                float interval = spec.Value("m_flTimeBetweenBurstShots");
                match.MatchTime = 41 + interval * .5f; Tick(weapon); Check(weapon.Ammo == before - 1, spec.Id + " burst waits between scheduled shots");
                match.MatchTime = 41 + interval; Tick(weapon); match.MatchTime = 41 + interval * 2; Tick(weapon);
                Check(weapon.Ammo == before - 3, spec.Id + " burst consumes exactly three cartridges"); CheckArsenalHits(target, spec, spec.Pellets * 3);
                match.MatchTime = 43; weapon.Alternate(); Check(!weapon.BurstMode, spec.Id + " returns to normal fire mode");
            }
            else if (spec.Source.Value<string>("m_eSilencerType") == "WEAPONSILENCER_DETACHABLE")
            {
                bool previous = weapon.IsAlt; int before = weapon.Ammo;
                weapon.Alternate(); Check(weapon.IsAlt != previous && weapon.Ammo == before, spec.Id + " suppressor changes mode without spending ammo");
                Check(!weapon.TryFire(), spec.Id + " suppressor transition blocks firing");
                match.MatchTime = CombatClock.AfterSeconds(40, spec.Overrides.Value<float?>("silencerToggleSeconds") ?? 2);
                Check(weapon.TryFire(), spec.Id + " suppressor transition unlocks at deadline"); CheckArsenalHits(target, spec, spec.Pellets);
            }
            else if (spec.Id == "weapon_revolver")
            {
                int before = weapon.Ammo; weapon.Alternate();
                Check(weapon.Ammo == before - 1, "R8 alternate fires immediately without primary windup"); CheckArsenalHits(target, spec, 1);
                weapon.Alternate(); Check(weapon.Ammo == before - 1, "R8 alternate obeys its cycle");
            }
        }

        static void UseWeapon(WeaponController weapon, string id)
        {
            Call(weapon, "CancelReload");
            typeof(WeaponController).GetField("burstRemaining", Private).SetValue(weapon, 0);
            typeof(WeaponController).GetField("blockUntil", Private).SetValue(weapon, 0f);
            var spec = root.Catalog.Weapon(id);
            Call(weapon, "Add", spec.Slot, spec);
            var states = (IDictionary)typeof(WeaponController).GetField("states", Private).GetValue(weapon);
            typeof(WeaponController).GetField("active", Private).SetValue(weapon, states[spec.Slot]);
            typeof(WeaponController).GetProperty("Slot").SetValue(weapon, spec.Slot);
        }

        static Actor Actor(string name, int team)
        {
            var actor = New(name).AddComponent<Actor>(); actor.Match = match; actor.Team = team; actor.DisplayName = name;
            actor.MaxHP = 100; actor.Id = match.Actors.Count;
            actor.transform.position = Origin + Vector3.left * (20 + actor.Id * 2);
            actor.Ability = actor.gameObject.AddComponent<AbilityController>(); actor.Ability.Initialize(actor, "frag_grenade");
            match.Actors.Add(actor); Reset(actor); return actor;
        }
        static void Reset(Actor actor) { actor.Alive = true; actor.HP = 100; actor.Armor = 100; actor.SpawnProtectedUntil = 0; actor.DamageHistory.Clear(); }
        static GameObject Solid(string name, float forward, string material, GameObject group = null)
        {
            var go = New(name); go.transform.position = Origin + Vector3.forward * forward + Vector3.up * 1.28f;
            var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(2, 2, .3f);
            var tag = go.AddComponent<SurfaceTag>(); tag.MaterialId = material; tag.logicalGroup = group; return go;
        }
        static GameObject New(string name) { var go = new GameObject("Validation " + name); created.Add(go); return go; }
        static void Tick(WeaponController weapon) => Call(weapon, "FixedUpdate");
        static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        static void Near(float actual, float expected, string label, float tolerance = .001f) => Check(Mathf.Abs(actual - expected) <= tolerance, label + $" (expected {expected}, got {actual})");
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Combat validation: " + label); }
    }
}
