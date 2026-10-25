using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZoneZ
{
    // Keep wire vectors scalar-only: Json.NET also walks Unity's computed
    // Vector3.normalized property, which recursively produces more vectors.
    [Serializable]
    public struct NetworkVector3
    {
        public float x, y, z;
        public static implicit operator NetworkVector3(Vector3 value) => new NetworkVector3 { x = value.x, y = value.y, z = value.z };
        public static implicit operator Vector3(NetworkVector3 value) => new Vector3(value.x, value.y, value.z);
    }

    [Serializable]
    public sealed class WeaponNetworkState
    {
        public string primary, pistol, slot;
        public int ammo, reserve, zoom, shots, hits, kills;
        public bool alternate, reloading;
        public float reloadProgress, inaccuracy, zeusReadyAt, recoilUp, recoilSide;
        public NetworkVector3 tracerEnd;
        public bool tracer;
    }

    public sealed class WeaponController : MonoBehaviour
    {
        sealed class WeaponState
        {
            public WeaponSpec Spec;
            public int Ammo, Reserve, ShotNumber, SequenceNumber, Zoom;
            public bool Alt;
            public float NextFire, ZeusReadyAt, FirePenalty;
        }
        public Actor Owner;
        public WeaponSpec Spec => active?.Spec;
        public string Slot { get; private set; } = "primary";
        public int Ammo { get => active?.Ammo ?? 0; set { if (active != null) active.Ammo = value; } }
        public int Reserve { get => active?.Reserve ?? 0; set { if (active != null) active.Reserve = value; } }
        public bool Reloading { get; private set; }
        public float ReloadProgress { get; private set; }
        public float ShotAt { get; private set; } = -100;
        public float HitAt { get; private set; } = -100;
        public float KillAt { get; private set; } = -100;
        public bool Scoped => active != null && active.Zoom > 0;
        public bool BurstMode => Spec != null && Spec.HasBurst && active.Alt;
        public float ScopeFov => !Scoped ? 60 : Spec.Value(active.Zoom == 1 ? "m_nZoomFOV1" : "m_nZoomFOV2", false, 40);
        public float ZeusRechargeRemaining => active != null && Spec.Category == "taser" ? Mathf.Max(0, active.ZeusReadyAt - Now) : 0;
        public WeaponPresentation Presentation { get; private set; }
        public float CurrentInaccuracy { get; private set; }
        public float MoveSpeed => Spec == null ? 4.7f : (Spec.Category == "melee" ? Override("movementSpeedMetersPerSecond", 4.7625f) : Spec.Value("m_flMaxSpeed", IsAlt, Spec.MoveSpeed / .01905f) * .01905f) * (Now < active.NextFire ? Spec.Value("m_flAttackMovespeedFactor", IsAlt, 1) : 1);
        public bool IsAlt => active != null && (active.Alt || active.Zoom > 0);
        readonly Dictionary<string, WeaponState> states = new Dictionary<string, WeaponState>();
        WeaponState active;
        Transform mount;
        int burstRemaining, shellPhase;
        float burstAt, reloadAt, reloadStarted, reloadDuration, reloadFactor = 1, blockUntil, landingPenalty, takeoffPenalty;
        bool groundedLast, revolverCharging, fireAfterShell;
        string presentedAbility;
        int shotSerial, hitSerial, killSerial;
        bool receivedNetworkState, lastShotTracer;
        float lastRecoilUp, lastRecoilSide;
        Vector3 lastTracerEnd;
        float Now => Owner && Owner.Match ? Owner.Match.MatchTime : 0;

        public void EquipLoadout(string primary, string pistol)
        {
            var catalog = GameRoot.Instance?.Catalog;
            if (catalog == null) return;
            states.Clear(); active = null;
            Add("primary", catalog.Weapon(primary) ?? catalog.Weapon("weapon_ak47"));
            Add("pistol", catalog.Weapon(pistol) ?? catalog.Weapon("weapon_glock"));
            Add("knife", catalog.Weapon("weapon_knife"));
            Add("zeus", catalog.Weapon("weapon_taser"));
            SelectSlot("primary");
        }

        void Add(string slot, WeaponSpec spec)
        {
            if (spec == null) return;
            states[slot] = new WeaponState { Spec = spec, Ammo = spec.Magazine, Reserve = spec.Reserve,
                Alt = StartsInAlternateMode(spec) };
        }

        static bool StartsInAlternateMode(WeaponSpec spec) => spec.Source?.Value<string>("m_eSilencerType") == "WEAPONSILENCER_DETACHABLE" && (spec.Overrides?.Value<bool?>("startSilenced") ?? false);

        public void ResetForSpawn()
        {
            foreach (var pair in states)
            {
                var state = pair.Value; state.Ammo = state.Spec.Magazine; state.Reserve = state.Spec.Reserve;
                state.NextFire = 0; state.ZeusReadyAt = 0; state.FirePenalty = 0; state.ShotNumber = 0; state.SequenceNumber = 0; state.Zoom = 0;
                state.Alt = StartsInAlternateMode(state.Spec);
            }
            blockUntil = 0; burstRemaining = 0; revolverCharging = false; landingPenalty = takeoffPenalty = 0;
            groundedLast = Owner.Motor && Owner.Motor.isGrounded;
            ShotAt = HitAt = KillAt = -100;
            CancelReload(); active = null; SelectSlot("primary");
            if (Presentation) Presentation.gameObject.SetActive(true);
        }

        public void SelectSlot(string slot)
        {
            if (active != null && Owner && Owner.Ability && Owner.Ability.HasTemporaryWeapon) return;
            if (!states.TryGetValue(slot, out var target)) return;
            if (active == target) return;
            CancelReload(); burstRemaining = 0; revolverCharging = false;
            if (active != null) active.Zoom = 0;
            active = target; Slot = slot; blockUntil = Mathf.Max(blockUntil, CombatClock.AfterSeconds(Now, Mathf.Max(0, Spec.Value("m_flDeployDuration", false, .18f))));
            BuildPresentation();
        }

        public bool HasAmmunition(string slot)
        {
            if (!states.TryGetValue(slot, out var state)) return false;
            return state.Spec.Category == "melee" || state.Ammo > 0 || state.Reserve > 0 || state.Spec.Category == "taser" && Now >= state.ZeusReadyAt;
        }

        void BuildPresentation()
        {
            if (Presentation) { Presentation.gameObject.SetActive(false); Destroy(Presentation.gameObject); }
            if (!Owner || !Owner.Aim || Spec == null) return;
            if (!mount)
            {
                mount = new GameObject("WeaponMount").transform; mount.SetParent(Owner.Aim, false);
            }
            mount.localPosition = Owner.Human ? new Vector3(.24f, -.27f, .32f) : new Vector3(.20f, -.28f, .18f);
            mount.localRotation = Quaternion.identity;
            presentedAbility = Owner.Ability && Owner.Ability.HasTemporaryWeapon ? Owner.Ability.EquippedId : null;
            if (presentedAbility != null) Presentation = WeaponModelFactory.CreateAbility(mount, presentedAbility, Owner.Human);
            else
            {
                var skin = GameRoot.Instance?.Store?.SkinFor(Spec.Id);
                Presentation = WeaponModelFactory.Create(mount, Spec, skin, Owner.Human);
                Presentation.SetAlternate(IsAlt);
            }
        }

        public void RefreshPresentation() => BuildPresentation();

        public WeaponNetworkState CaptureNetworkState()
        {
            return new WeaponNetworkState {
                primary = states.TryGetValue("primary", out var primary) ? primary.Spec.Id : "weapon_ak47",
                pistol = states.TryGetValue("pistol", out var pistol) ? pistol.Spec.Id : "weapon_glock",
                slot = Slot, ammo = Ammo, reserve = Reserve, zoom = active?.Zoom ?? 0, alternate = active?.Alt ?? false,
                reloading = Reloading, reloadProgress = ReloadProgress, inaccuracy = CurrentInaccuracy,
                zeusReadyAt = active?.ZeusReadyAt ?? 0, shots = shotSerial, hits = hitSerial, kills = killSerial,
                recoilUp = lastRecoilUp, recoilSide = lastRecoilSide, tracer = lastShotTracer, tracerEnd = lastTracerEnd
            };
        }

        public void ApplyNetworkState(WeaponNetworkState state)
        {
            if (state == null || !Owner || !Owner.IsNetworkReplica) return;
            bool loadoutChanged = !states.TryGetValue("primary", out var primary) || primary.Spec.Id != state.primary ||
                !states.TryGetValue("pistol", out var pistol) || pistol.Spec.Id != state.pistol;
            if (loadoutChanged) EquipLoadout(state.primary, state.pistol);
            if (state.slot != Slot && states.TryGetValue(state.slot ?? "", out var target))
            {
                active = target; Slot = state.slot; BuildPresentation();
            }
            if (active == null) return;
            Ammo = state.ammo; Reserve = state.reserve; active.Zoom = state.zoom; active.Alt = state.alternate;
            active.ZeusReadyAt = state.zeusReadyAt; Reloading = state.reloading;
            ReloadProgress = state.reloadProgress; CurrentInaccuracy = state.inaccuracy;
            string temporary = Owner.Ability && Owner.Ability.HasTemporaryWeapon ? Owner.Ability.EquippedId : null;
            if (presentedAbility != temporary) BuildPresentation();
            if (Presentation) { Presentation.SetAlternate(IsAlt); Presentation.SetReload(Reloading ? ReloadProgress : 0, Ammo == 0); }
            if (receivedNetworkState)
            {
                if (state.shots != shotSerial)
                {
                    ShotAt = Time.unscaledTime; Presentation?.Fire(); AudioFX.Shot(Owner, Spec, IsAlt);
                    if (Owner.Human) Owner.GetComponent<PlayerMotor>()?.AddRecoil(state.recoilUp, state.recoilSide);
                    if (state.tracer) CombatTracer.Draw(Presentation && Presentation.Muzzle ? Presentation.Muzzle.position : Owner.EyePosition,
                        state.tracerEnd, new Color(1, .77f, .3f), .012f, .06f);
                }
                if (state.hits != hitSerial)
                {
                    HitAt = Time.unscaledTime;
                    if (Owner.Human) AudioFX.Hit(state.kills != killSerial);
                }
                if (state.kills != killSerial) KillAt = Time.unscaledTime;
            }
            shotSerial = state.shots; hitSerial = state.hits; killSerial = state.kills; receivedNetworkState = true;
        }

        void FixedUpdate()
        {
            if (!Owner || Owner.IsNetworkReplica || !Owner.Match || !Owner.Alive || Owner.Match.Phase != MatchPhase.Playing || (GameRoot.Instance && GameRoot.Instance.SimulationPaused)) return;
            SyncTemporaryWeapon();
            foreach (var state in states.Values)
            {
                bool crouch = Owner.IsCrouching;
                bool alt = state.Alt || state.Zoom > 0;
                float recovery = state.Spec.Value(crouch ? "m_flRecoveryTimeCrouch" : "m_flRecoveryTimeStand", alt, .35f);
                string finalKey = crouch ? "m_flRecoveryTimeCrouchFinal" : "m_flRecoveryTimeStandFinal";
                float final = state.Spec.Value(finalKey, alt, recovery);
                int first = Mathf.RoundToInt(state.Spec.Value("m_nRecoveryTransitionStartBullet", false, 0));
                int last = Mathf.RoundToInt(state.Spec.Value("m_nRecoveryTransitionEndBullet", false, 5));
                if (final > 0) recovery = Mathf.Lerp(recovery, final, Mathf.InverseLerp(first, Mathf.Max(first + 1, last), state.ShotNumber));
                state.FirePenalty = Mathf.MoveTowards(state.FirePenalty, 0, Time.deltaTime * .12f / Mathf.Max(.1f, recovery));
                if (state.FirePenalty <= .00001f) state.ShotNumber = 0;
                if (state.Spec.Category == "taser" && state.Ammo == 0 && Now >= state.ZeusReadyAt) state.Ammo = 1;
            }
            bool grounded = Owner.Motor && Owner.Motor.isGrounded;
            if (!groundedLast && grounded && Spec != null) landingPenalty = Spec.Value("m_flInaccuracyLand", IsAlt, .002f) * 50;
            if (groundedLast && !grounded && Spec != null && Owner.CurrentVelocity.y > 0) takeoffPenalty = Spec.Value("m_flInaccuracyJumpInitial", IsAlt, .05f);
            groundedLast = grounded; landingPenalty = Mathf.MoveTowards(landingPenalty, 0, Time.deltaTime * .08f);
            takeoffPenalty = Mathf.MoveTowards(takeoffPenalty, 0, Time.deltaTime * .35f);
            CurrentInaccuracy = CalculateInaccuracy();
            if (Reloading) TickReload();
            if (fireAfterShell && !Reloading && Now >= active.NextFire)
            {
                TryFire();
            }
            while (burstRemaining > 0 && Now + .0001f >= burstAt)
            {
                if (Ammo > 0 && !Reloading)
                {
                    FireBullet(); burstRemaining--; revolverCharging = false;
                    burstAt += Spec.Value("m_flTimeBetweenBurstShots", false, .05f);
                }
                else burstRemaining = 0;
            }
            if (Presentation) Presentation.SetReload(Reloading ? ReloadProgress : 0, Ammo == 0);
        }

        void SyncTemporaryWeapon()
        {
            string temporary = Owner.Ability && Owner.Ability.HasTemporaryWeapon ? Owner.Ability.EquippedId : null;
            if (presentedAbility != temporary)
            {
                CancelReload(); burstRemaining = 0; revolverCharging = false;
                if (active != null) active.Zoom = 0;
                BuildPresentation();
            }
        }

        void LateUpdate()
        {
            if (!Owner || !Owner.Alive || !Owner.Human || !mount || !Presentation) return;
            bool hidden = Scoped && (Spec.Source?["m_bHideViewModelWhenZoomed"]?.Value<bool>() ?? false);
            Presentation.gameObject.SetActive(!hidden);
            float moving = Mathf.Clamp01(new Vector2(Owner.CurrentVelocity.x, Owner.CurrentVelocity.z).magnitude / 4.5f);
            float sway = (Scoped ? .2f : 1) * Mathf.Clamp01(GameRoot.Instance.Profile.viewMotion);
            Vector3 rest = Scoped ? new Vector3(.09f, -.21f, .32f) : new Vector3(.24f, -.27f, .32f);
            mount.localPosition = rest + new Vector3(Mathf.Sin(Now * 9) * .01f, Mathf.Cos(Now * 18) * .008f, 0) * moving * sway;
        }

        public void CancelPrimaryWindup()
        {
            if (!revolverCharging) return;
            revolverCharging = false; burstRemaining = 0; if (active != null) active.NextFire = Now;
        }

        public bool TryFire()
        {
            if (!CanAct() || Now < blockUntil || active == null) return false;
            SyncTemporaryWeapon();
            if (Owner.Ability && Owner.Ability.HasTemporaryWeapon)
            {
                if (Owner.Ability.FireTemporary(Owner.EyePosition, Owner.Aim.forward))
                {
                    Owner.CancelSpawnProtection(); ShotAt = Time.unscaledTime; shotSerial++; lastShotTracer = false; lastRecoilUp = lastRecoilSide = 0;
                    Presentation?.Fire(); AudioFX.ReportNoise(Owner, 62); return true;
                }
                return false;
            }
            if (Reloading && Now >= reloadAt) TickReload();
            if (Reloading)
            {
                if (!Spec.SingleShellReload) return false;
                if (shellPhase == 1 || shellPhase == 3) { shellPhase = 3; fireAfterShell = true; return false; }
                if (Ammo <= 0) return false;
                if (shellPhase == 0 || shellPhase == 2) CancelReload();
            }
            if (burstRemaining > 0 || Now + .0001f < active.NextFire) return false;
            fireAfterShell = false;
            if (Spec.Category == "melee") return Melee(false);
            if (Spec.Category == "taser" && Ammo == 0 && Now >= active.ZeusReadyAt) Ammo = 1;
            if (Ammo <= 0) { if (Reserve > 0) Reload(); return false; }
            if (Spec.Category == "taser")
            {
                Ammo = 0; active.ZeusReadyAt = CombatClock.AfterSeconds(Now, Override("rechargeSeconds", 30)); Owner.CancelSpawnProtection();
                ShootRay(Owner.Aim.forward, true); FireFeedback(); return true;
            }
            // Revolver wind-up is handled as a scheduled single shot, cancellable by switching.
            if (Spec.Id == "weapon_revolver")
            {
                burstRemaining = 1; burstAt = CombatClock.AfterSeconds(Now, Override("primaryWindupSeconds", .2f));
                revolverCharging = true;
                active.NextFire = Now + Spec.Value("m_flCycleTime", false, .5f); return true;
            }
            if (BurstMode)
            {
                active.NextFire = Now + Spec.Value("m_flCycleTimeWhenInBurstMode", false, .5f);
                FireBullet(); burstRemaining = Mathf.Min(2, Ammo); burstAt = Now + Spec.Value("m_flTimeBetweenBurstShots", false, .05f); return true;
            }
            float interval = Spec.Value("m_flCycleTime", IsAlt, Spec.FireInterval);
            active.NextFire = Now - active.NextFire > interval ? Now + interval : active.NextFire + interval;
            FireBullet(); return true;
        }

        bool CanAct() => Owner && !Owner.IsNetworkReplica && Owner.Alive && Owner.Match && Owner.Match.Phase == MatchPhase.Playing && (!GameRoot.Instance || !GameRoot.Instance.SimulationPaused);

        void FireBullet(bool forceAlt = false)
        {
            if (Ammo <= 0) return;
            Ammo--; Owner.CancelSpawnProtection();
            bool alt = forceAlt || IsAlt;
            float spread = CalculateInaccuracy(alt);
            // Simulation seeds depend on the shot sequence, never on frame timing.
            uint rng = unchecked((uint)((int)Spec.Value("m_nSpreadSeed", false, Spec.RecoilSeed) + Owner.Id * 1013 + active.SequenceNumber * 7919 + Owner.Match.Seed));
            int pellets = Mathf.Max(1, Spec.Pellets);
            for (int i = 0; i < pellets; i++)
            {
                float radius = Mathf.Sqrt(Random01(ref rng)) * spread;
                float angle = Random01(ref rng) * Mathf.PI * 2;
                Vector3 direction = (Owner.Aim.forward + Owner.Aim.right * (Mathf.Cos(angle) * radius) + Owner.Aim.up * (Mathf.Sin(angle) * radius)).normalized;
                ShootRay(direction, false);
            }
            active.ShotNumber++; active.SequenceNumber++;
            active.FirePenalty = Mathf.Min(.2f, active.FirePenalty + Spec.Value("m_flInaccuracyFire", alt, .005f));
            float stability = Owner.Ability ? Owner.Ability.StabilityFactor : 1;
            uint recoilRng = unchecked((uint)(Spec.RecoilSeed + active.ShotNumber * 7919));
            float magnitude = Mathf.Max(0, Spec.Value("m_flRecoilMagnitude", alt, Spec.Recoil) + (Random01(ref recoilRng) * 2 - 1) * Spec.Value("m_flRecoilMagnitudeVariance", alt)) * .035f * stability;
            float variance = Spec.Value("m_flRecoilAngleVariance", alt, 20);
            float recoilAngle = (Spec.Value("m_flRecoilAngle", alt, 0) + (Random01(ref recoilRng) * 2 - 1) * variance) * Mathf.Deg2Rad;
            float up = magnitude * Mathf.Cos(recoilAngle), side = magnitude * Mathf.Sin(recoilAngle);
            lastRecoilUp = up; lastRecoilSide = side;
            Owner.GetComponent<PlayerMotor>()?.AddRecoil(up, side);
            Owner.GetComponent<BotBrain>()?.AddRecoil(up, side);
            FireFeedback();
            if (Spec.Source?["m_bUnzoomsAfterShot"]?.Value<bool>() ?? false) active.Zoom = 0;
        }

        float CalculateInaccuracy(bool? alternative = null)
        {
            if (Spec == null || Spec.Category == "melee") return 0;
            bool alt = alternative ?? IsAlt;
            float baseAccuracy = Spec.Value(Owner.IsCrouching ? "m_flInaccuracyCrouch" : "m_flInaccuracyStand", alt, Spec.StandInaccuracy);
            float speedRatio = Mathf.Clamp01(new Vector2(Owner.CurrentVelocity.x, Owner.CurrentVelocity.z).magnitude / Mathf.Max(.1f, Owner.MovementSpeed));
            float accuracy = Spec.Value("m_flSpread", alt, Spec.Spread) + baseAccuracy + Spec.Value("m_flInaccuracyMove", alt, Spec.MoveInaccuracy) * speedRatio * speedRatio;
            if (Owner.Motor && !Owner.Motor.isGrounded)
                accuracy += Mathf.Lerp(Spec.Value("m_flInaccuracyJumpApex", alt), Spec.Value("m_flInaccuracyJump", alt, .09f), Mathf.Clamp01(Mathf.Abs(Owner.CurrentVelocity.y) / 7)) + takeoffPenalty;
            if (Reloading) accuracy += Spec.Value("m_flInaccuracyReload", alt);
            accuracy += active.FirePenalty + landingPenalty;
            return accuracy * (Owner.Ability ? Owner.Ability.StabilityFactor : 1);
        }

        void ShootRay(Vector3 direction, bool electric)
        {
            var hit = CombatTrace.Cast(Owner.EyePosition, direction, Spec.Range, Owner, !electric);
            lastShotTracer = true; lastTracerEnd = hit.Point;
            var actor = hit.Target;
            if (actor && Owner.Match.IsEnemy(Owner, actor))
            {
                if (!electric && Owner.Ability && Owner.Ability.AbsoluteActive) actor.Kill(Owner, true);
                else
                {
                    float body = electric ? 1 : hit.Zone.Group == "head" ? Spec.HeadMultiplier : hit.Zone.Group == "stomach" ? 1.25f : hit.Zone.Group == "legs" ? .75f : 1;
                    float falloff = Mathf.Pow(Spec.RangeModifier, hit.Distance / .01905f / 500);
                    float damage = Mathf.Max(1, Spec.Damage * Owner.DamageMultiplier * body * falloff * hit.Retention);
                    actor.ApplyDamage(damage, Owner, false, electric, hit.Zone.Group, Spec.ArmorRatio);
                }
                if (!actor.IsSpawnProtected) AudioFX.Impact(hit.Point, false, true);
            }
            else if (hit.Hit && !actor)
            {
                var surface = hit.Collider.GetComponentInParent<SurfaceTag>();
                AudioFX.Impact(hit.Point, surface && surface.Penetrable, false);
            }
            CombatTracer.Draw(Presentation && Presentation.Muzzle ? Presentation.Muzzle.position : Owner.EyePosition, hit.Point, electric ? new Color(.3f, .8f, 1) : new Color(1, .77f, .3f), electric ? .035f : .012f, electric ? .1f : .035f);
        }

        bool Melee(bool heavy)
        {
            if (Now < active.NextFire || !CanAct()) return false;
            lastShotTracer = false; lastRecoilUp = lastRecoilSide = 0;
            string attack = heavy ? "secondaryAttack" : "primaryAttack";
            float damage = Spec.Overrides?[attack]?.Value<float?>("baseDamage") ?? (heavy ? 65 : 40);
            float range = Spec.Overrides?[attack]?.Value<float?>("rangeMeters") ?? (heavy ? 1.25f : 1.5f);
            float cycle = Spec.Overrides?[attack]?.Value<float?>("cycleSeconds") ?? (heavy ? 1 : .4f);
            active.NextFire = Now + cycle; Owner.CancelSpawnProtection();
            var hit = CombatTrace.Cast(Owner.EyePosition, Owner.Aim.forward, range, Owner, false, .16f);
            var victim = hit.Target;
            if (victim && Owner.Match.IsEnemy(Owner, victim))
            {
                Vector3 relative = (Owner.transform.position - victim.transform.position).normalized;
                if (Vector3.Dot(Owner.Aim.forward, victim.transform.forward) > .5f && Vector3.Dot(relative, victim.transform.forward) < -.5f) damage *= Override("backstabDamageMultiplier", 2);
                victim.ApplyDamage(damage * Owner.DamageMultiplier, Owner, false, false, hit.Zone.Group == "legs" ? "legs" : "chest", Override("armorDamageFraction", .85f) * 2);
                if (!victim.IsSpawnProtected) AudioFX.Impact(hit.Point, false, true);
            }
            else if (hit.Hit && !victim) AudioFX.Impact(hit.Point, false, false);
            FireFeedback(); return true;
        }

        void FireFeedback()
        {
            ShotAt = Time.unscaledTime; shotSerial++;
            Presentation?.Fire(); AudioFX.Shot(Owner, Spec, IsAlt);
        }

        public void ConfirmHit(bool killed)
        {
            HitAt = Time.unscaledTime; hitSerial++; if (killed) { KillAt = Time.unscaledTime; killSerial++; }
            if (Owner.Human) AudioFX.Hit(killed);
        }

        public void Reload()
        {
            if (!CanAct() || Reloading || active == null || (Owner.Ability && Owner.Ability.HasTemporaryWeapon) || Spec.Category == "melee" || Spec.Category == "taser" || Ammo >= Spec.Magazine || Reserve <= 0 || burstRemaining > 0) return;
            Reloading = true; ReloadProgress = 0; active.Zoom = 0; reloadStarted = Now; fireAfterShell = false;
            reloadFactor = Owner.Ability ? Owner.Ability.ReloadFactor : 1;
            float factor = reloadFactor;
            if (Spec.SingleShellReload)
            {
                shellPhase = 0; reloadAt = CombatClock.AfterSeconds(Now, Override("reloadStartSeconds", .5f) * factor);
                reloadDuration = (Override("reloadStartSeconds", .5f) + Mathf.Min(Reserve, Spec.Magazine - Ammo) * Override("reloadShellSeconds", .5f) + Override("reloadFinishSeconds", .3f)) * factor;
            }
            else { reloadDuration = Mathf.Max(.1f, Spec.ReloadSeconds * factor); reloadAt = CombatClock.AfterSeconds(Now, reloadDuration); }
            AudioFX.Reload(Owner);
        }

        void TickReload()
        {
            ReloadProgress = Mathf.Clamp01((Now - reloadStarted) / Mathf.Max(.1f, reloadDuration));
            if (Now < reloadAt) return;
            if (!Spec.SingleShellReload)
            {
                int transfer = Mathf.Min(Spec.Magazine - Ammo, Reserve); Ammo += transfer; Reserve -= transfer; CancelReload(); return;
            }
            float factor = reloadFactor;
            if (shellPhase == 2) { bool pending = fireAfterShell; CancelReload(); fireAfterShell = pending; return; }
            if (shellPhase == 1 || shellPhase == 3)
            {
                if (Reserve > 0 && Ammo < Spec.Magazine) { Ammo++; Reserve--; AudioFX.Reload(Owner); }
                if (shellPhase == 3)
                {
                    // Preserve a single click until the shell is inserted, then fire it.
                    // Switching/death cancels the queued input through CancelReload.
                    CancelReload(); fireAfterShell = true; return;
                }
                if (Reserve == 0 || Ammo >= Spec.Magazine)
                {
                    shellPhase = 2; reloadAt = CombatClock.AfterSeconds(Now, Override("reloadFinishSeconds", .3f) * factor); return;
                }
            }
            shellPhase = 1; reloadAt = CombatClock.AfterSeconds(Now, Override("reloadShellSeconds", .5f) * factor);
        }

        void CancelReload() { Reloading = false; ReloadProgress = 0; shellPhase = 0; fireAfterShell = false; }

        public void Alternate()
        {
            if (!CanAct() || active == null || (Owner.Ability && Owner.Ability.HasTemporaryWeapon) || Reloading || burstRemaining > 0 || Now < blockUntil) return;
            if (Spec.Category == "melee") { Melee(true); return; }
            if (Spec.Id == "weapon_revolver")
            {
                if (Ammo <= 0 || Now < active.NextFire) return;
                active.NextFire = Now + Spec.Value("m_flCycleTime", true, .4f); FireBullet(true); return;
            }
            if (Spec.HasScope) { active.Zoom = (active.Zoom + 1) % (Mathf.RoundToInt(Spec.Value("m_nZoomLevels", false, 1)) + 1); Presentation?.SetAlternate(IsAlt); return; }
            if (Spec.HasBurst) { active.Alt = !active.Alt; blockUntil = CombatClock.AfterSeconds(Now, .15f); Presentation?.SetAlternate(IsAlt); return; }
            if (Spec.Source?["m_eSilencerType"]?.Value<string>() == "WEAPONSILENCER_DETACHABLE")
            {
                active.Alt = !active.Alt; blockUntil = CombatClock.AfterSeconds(Now, Override("silencerToggleSeconds", 2)); Presentation?.SetAlternate(IsAlt); AudioFX.Reload(Owner);
            }
        }

        public void OnOwnerDied()
        {
            CancelReload(); burstRemaining = 0; revolverCharging = false; if (active != null) active.Zoom = 0;
            if (Presentation) Presentation.gameObject.SetActive(false);
        }

        public void Inspect() { if (Presentation && !Reloading) Presentation.Inspect(); }
        float Override(string key, float fallback) => Spec?.Overrides?.Value<float?>(key) ?? fallback;
        static float Random01(ref uint value)
        {
            if (value == 0) value = 2463534242;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            return (value & 0x00FFFFFF) / 16777216f;
        }
    }

    public sealed class CombatTracer : MonoBehaviour
    {
        static Material material;
        float remaining;
        public static void Draw(Vector3 start, Vector3 end, Color color, float width, float duration)
        {
            if (!material) material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            var go = new GameObject("ShotTrail"); var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.positionCount = 2; line.SetPosition(0, start); line.SetPosition(1, end);
            line.startWidth = width; line.endWidth = width * .3f; line.startColor = color; line.endColor = new Color(color.r, color.g, color.b, .4f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            go.AddComponent<CombatTracer>().remaining = duration;
        }
        void Update() { if (GameRoot.Instance && GameRoot.Instance.SimulationPaused) return; remaining -= Time.deltaTime; if (remaining <= 0) Destroy(gameObject); }
    }
}
