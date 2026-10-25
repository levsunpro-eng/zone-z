using System;
using UnityEngine;
using UnityEngine.AI;

namespace ZoneZ
{
    public sealed class BotBrain : MonoBehaviour
    {
        public Actor Owner;
        NavMeshAgent navigation;
        Actor target;
        LootPickup lootTarget;
        Vector3 lastSeen, destination, lastPosition;
        float observedAt = -100, perceiveAt, decideAt, gravity, stuckTime, footDistance, investigateUntil;
        float recoilPitch, recoilYaw, aimErrorAt;
        Vector2 aimError, aimErrorGoal;
        string firingWeapon;
        readonly BotCombatTiming combatTiming = new BotCombatTiming();
        int soundCursor;
        bool destinationValid, visibleLastTick;
        System.Random rng;
        float Now => Owner && Owner.Match ? Owner.Match.MatchTime : 0;
        BotDifficultySettings Difficulty => GameRoot.Instance && GameRoot.Instance.Catalog != null ? GameRoot.Instance.Catalog.BotDifficulty : BotDifficultySettings.Defaults;

        void Start()
        {
            if (!Owner || Owner.Human || Owner.RemoteControlled || Owner.IsNetworkReplica) { enabled = false; return; }
            rng = new System.Random(Owner.Id * 7919 + 73);
            navigation = gameObject.AddComponent<NavMeshAgent>();
            navigation.radius = .34f; navigation.height = 1.8f; navigation.baseOffset = 0;
            navigation.speed = 5; navigation.acceleration = 28; navigation.angularSpeed = 250;
            navigation.updatePosition = false; navigation.updateRotation = false;
            navigation.stoppingDistance = .65f;
            navigation.avoidancePriority = 20 + Owner.Id % 60;
            ResetNavigation();
        }

        public void ResetNavigation()
        {
            if (!Owner || !enabled || Owner.Human || Owner.RemoteControlled || Owner.IsNetworkReplica) { StopNavigation(); return; }
            target = null; lootTarget = null; observedAt = -100; perceiveAt = Now + (Owner.Id % 9) * .024f;
            decideAt = 0; gravity = -2; stuckTime = footDistance = investigateUntil = 0; lastPosition = transform.position; destinationValid = visibleLastTick = false;
            recoilPitch = recoilYaw = 0;
            ResetCombat();
            soundCursor = AudioFX.LastNoiseId;
            if (navigation)
            {
                navigation.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out var hit, 4, NavMesh.AllAreas)) navigation.Warp(hit.position);
            }
        }

        public void StopNavigation()
        {
            ResetCombat();
            if (navigation) { if (navigation.isOnNavMesh) navigation.ResetPath(); navigation.enabled = false; }
        }

        void FixedUpdate()
        {
            if (!Owner || Owner.Human || Owner.RemoteControlled || Owner.IsNetworkReplica || !Owner.Alive || !Owner.Match || !Owner.Motor || !Owner.Motor.enabled || !Owner.Weapon || Owner.Weapon.Spec == null || !navigation || !navigation.enabled || (GameRoot.Instance && GameRoot.Instance.SimulationPaused)) return;
            if (Owner.Match.Phase != MatchPhase.Playing) { Owner.CurrentVelocity = Vector3.zero; return; }
            recoilPitch = Mathf.MoveTowards(recoilPitch, 0, Time.fixedDeltaTime * 6);
            recoilYaw = Mathf.MoveTowards(recoilYaw, 0, Time.fixedDeltaTime * 6);
            if (!navigation.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out var nearest, 5, NavMesh.AllAreas)) navigation.Warp(nearest.position);
                else return;
            }
            if (Now >= perceiveAt) { Perceive(); perceiveAt = Now + .17f + (Owner.Id % 5) * .01f; }
            if (target && !target.Alive) { target = null; ResetCombat(); }
            // Recheck the actual sight line before firing, even between perception samples.
            Vector3 sightPoint = target ? target.transform.position + Vector3.up * (target.IsCrouching ? .88f : 1.25f) : lastSeen;
            bool visible = target && Now - observedAt < .24f && CanSee(target, sightPoint);
            if (visible || lootTarget && !lootTarget.gameObject.activeInHierarchy) lootTarget = null;
            if (visible && !visibleLastTick)
            {
                combatTiming.Acquire(Now, Difficulty, RandomSource);
                PickAimError(); aimError = aimErrorGoal;
            }
            if (visible && combatTiming.TrackingDue(Now))
            {
                // Aim at observations, not the enemy's current transform on every physics tick.
                lastSeen = sightPoint;
                combatTiming.RecordTrackingSample(Now, Difficulty, RandomSource);
            }
            visibleLastTick = visible;
            float distanceToTarget = visible ? Vector3.Distance(Owner.EyePosition, lastSeen) : float.PositiveInfinity;
            SelectUsableWeapon(distanceToTarget);
            if (Now >= decideAt) { Decide(visible); decideAt = Now + .6f + NextFloat() * .7f; }
            navigation.speed = Owner.MovementSpeed;
            navigation.nextPosition = transform.position;
            Vector3 desired = navigation.desiredVelocity;
            desired.y = 0;
            bool shortRange = Owner.Weapon.Slot == "knife" || Owner.Weapon.Slot == "zeus";
            if (visible && !shortRange && Now >= combatTiming.ReactionReadyAt && ((int)(Now * 1.7f + Owner.Id) % 3) != 0) desired = Vector3.zero;
            Vector3 aimAt = visible ? lastSeen : lootTarget ? lootTarget.transform.position : transform.position + (desired.sqrMagnitude > .1f ? desired.normalized : transform.forward) * 8 + Vector3.up * 1.2f;
            Vector3 aimDirection = aimAt - Owner.EyePosition;
            if (aimDirection.sqrMagnitude > .001f)
            {
                Vector3 flat = new Vector3(aimDirection.x, 0, aimDirection.z);
                if (flat.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), Time.fixedDeltaTime * Difficulty.TurnDegreesPerSecond);
                float pitch = -Mathf.Atan2(aimDirection.y, new Vector2(aimDirection.x, aimDirection.z).magnitude) * Mathf.Rad2Deg;
                if (visible && Now >= aimErrorAt) PickAimError();
                aimError = Vector2.Lerp(aimError, aimErrorGoal, 1 - Mathf.Exp(-Time.fixedDeltaTime * 3));
                float errorScale = visible && !shortRange ? Mathf.Lerp(.55f, 1, Mathf.InverseLerp(2, 25, distanceToTarget)) : 0;
                Owner.Aim.localRotation = Quaternion.Slerp(Owner.Aim.localRotation, Quaternion.Euler(pitch - recoilPitch + aimError.y * errorScale, recoilYaw + aimError.x * errorScale, 0), Time.fixedDeltaTime * 11);
                Owner.Visual?.SetAim(pitch);
            }
            if (visible && Now >= combatTiming.ReactionReadyAt && Vector3.Angle(Owner.Aim.forward, aimDirection) < 9)
            {
                UseAbility(true, distanceToTarget);
                ResetBurstForWeapon();
                if (Owner.Ability && Owner.Ability.HasTemporaryWeapon) TryCombatTrigger(false);
                else if (Owner.Weapon.Ammo == 0 && Owner.Weapon.Reserve > 0) Owner.Weapon.Reload();
                else if (!shortRange || distanceToTarget <= Owner.Weapon.Spec.Range + .2f) TryCombatTrigger(shortRange);
            }
            else
            {
                Owner.Weapon.CancelPrimaryWindup();
                UseAbility(false, distanceToTarget);
                if (Owner.Weapon.Ammo < Mathf.Max(1, Owner.Weapon.Spec.Magazine / 3) && Owner.Weapon.Reserve > 0) Owner.Weapon.Reload();
            }
            if (Owner.Motor.isGrounded && gravity < 0) gravity = -2;
            gravity -= 23 * Time.fixedDeltaTime;
            Vector3 before = transform.position;
            Owner.Motor.Move((desired + Vector3.up * gravity) * Time.fixedDeltaTime);
            Owner.CurrentVelocity = (transform.position - before) / Time.fixedDeltaTime;
            if (lootTarget && Vector3.Distance(lootTarget.transform.position, transform.position + Vector3.up) <= 1.5f)
            {
                // Same aim cone, sight line and availability checks used by the player's E action.
                LootPickup.TryNearest(Owner);
                if (!lootTarget || !lootTarget.gameObject.activeInHierarchy) { lootTarget = null; decideAt = 0; }
            }
            navigation.nextPosition = transform.position;
            Owner.Visual?.SetMotion(new Vector2(Owner.CurrentVelocity.x, Owner.CurrentVelocity.z).magnitude, false);
            footDistance += new Vector2(Owner.CurrentVelocity.x, Owner.CurrentVelocity.z).magnitude * Time.fixedDeltaTime;
            if (footDistance > 1.1f && Owner.Motor.isGrounded) { footDistance = 0; AudioFX.Footstep(Owner, false); }
            if (desired.sqrMagnitude > 1 && Vector3.Distance(lastPosition, transform.position) < .012f) stuckTime += Time.fixedDeltaTime;
            else stuckTime = 0;
            lastPosition = transform.position;
            if (stuckTime > 1.7f) { destinationValid = false; PickRoamPoint(); stuckTime = 0; }
            if (transform.position.y < -12) Owner.Kill(null, false);
        }

        public void AddRecoil(float up, float side)
        {
            recoilPitch = Mathf.Clamp(recoilPitch + up, 0, 13);
            recoilYaw = Mathf.Clamp(recoilYaw + side, -7, 7);
        }

        void Perceive()
        {
            Actor chosen = null;
            float bestDistance = float.MaxValue;
            foreach (var candidate in Owner.Match.Actors)
            {
                if (!candidate || !candidate.Alive || !Owner.Match.IsEnemy(Owner, candidate)) continue;
                Vector3 point = candidate.transform.position + Vector3.up * (candidate.IsCrouching ? .87f : 1.28f);
                Vector3 direction = point - Owner.EyePosition;
                float distance = direction.magnitude;
                if (distance > 60 || distance > bestDistance) continue;
                if (Vector3.Angle(Owner.Aim.forward, direction) > (candidate == target ? 95 : 65)) continue;
                if (!CanSee(candidate, point)) continue;
                chosen = candidate; bestDistance = distance;
            }
            if (chosen)
            {
                if (target != chosen)
                {
                    Owner.Weapon.CancelPrimaryWindup();
                    ResetCombat();
                }
                target = chosen; observedAt = Now;
                investigateUntil = Now + 5;
            }
            else if (Now - observedAt > 5) target = null;
            foreach (var noise in AudioFX.Noises)
            {
                if (noise.Id <= soundCursor || !noise.Source || noise.Source == Owner || !Owner.Match.IsEnemy(Owner, noise.Source)) continue;
                if (Now - noise.Time > 2 || Vector3.Distance(Owner.EyePosition, noise.Position) > noise.Radius) continue;
                if (Now - observedAt > 1)
                {
                    lastSeen = noise.Position;
                    investigateUntil = Now + 3;
                    // Sound conveys its event position, never the source's continuously updated transform.
                    SetDestination(lastSeen); decideAt = Now + 1.5f;
                    Vector3 delta = lastSeen - transform.position; delta.y = 0;
                    if (delta.sqrMagnitude > .1f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), 30);
                }
            }
            soundCursor = AudioFX.LastNoiseId;
            if (!chosen && Owner.Ability && Owner.Ability.Revealed.Count > 0)
            {
                float nearest = float.MaxValue;
                foreach (var reveal in Owner.Ability.Revealed)
                {
                    if (!reveal.Key || !reveal.Key.Alive || !Owner.Match.IsEnemy(Owner, reveal.Key)) continue;
                    float distance = (reveal.Value - transform.position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; lastSeen = reveal.Value; investigateUntil = Now + 2;
                }
            }
        }

        bool CanSee(Actor candidate, Vector3 point)
        {
            Vector3 direction = point - Owner.EyePosition;
            return CombatTrace.Cast(Owner.EyePosition, direction, direction.magnitude + .15f, Owner, false).Target == candidate;
        }

        void Decide(bool visible)
        {
            if (visible)
            {
                float distance = Vector3.Distance(transform.position, lastSeen);
                if (Owner.Weapon.Spec.HasScope)
                {
                    bool shouldScope = distance > 20;
                    if (shouldScope != Owner.Weapon.Scoped) Owner.Weapon.Alternate();
                }
                Vector3 away = transform.position - lastSeen; away.y = 0; away.Normalize();
                if (Owner.Weapon.Slot == "knife" || Owner.Weapon.Slot == "zeus")
                {
                    navigation.stoppingDistance = .25f;
                    SetDestination(lastSeen + away * .7f); return;
                }
                navigation.stoppingDistance = .65f;
                Vector3 side = Vector3.Cross(Vector3.up, away) * (NextFloat() > .5f ? 1 : -1);
                Vector3 desired = distance > 24 ? lastSeen + away * 15 : transform.position + side * (1.5f + NextFloat() * 3);
                if (distance < 5) desired += away * 3;
                SetDestination(desired);
            }
            else
            {
                lootTarget = !Owner.Match.IsTeamMode && Now - observedAt > .6f ? LootPickup.FindNearby(Owner, 12, false) : null;
                if (lootTarget)
                {
                    navigation.stoppingDistance = .65f;
                    SetDestination(lootTarget.transform.position); return;
                }
                if (Now < investigateUntil) SetDestination(lastSeen);
                else if (!destinationValid || !navigation.hasPath || Vector3.Distance(transform.position, destination) < 2) PickRoamPoint();
            }
        }

        void SelectUsableWeapon(float distance)
        {
            if (Owner.Ability && Owner.Ability.HasTemporaryWeapon) return;
            string slot = Owner.Weapon.HasAmmunition("primary") ? "primary" : Owner.Weapon.HasAmmunition("pistol") ? "pistol" :
                distance < 3 && Owner.Weapon.HasAmmunition("zeus") ? "zeus" : "knife";
            if (slot != Owner.Weapon.Slot) { Owner.Weapon.SelectSlot(slot); decideAt = 0; }
            ResetBurstForWeapon();
        }

        void ResetCombat()
        {
            combatTiming.Clear();
            firingWeapon = null; visibleLastTick = false;
            aimError = aimErrorGoal = Vector2.zero; aimErrorAt = 0;
        }

        void PickAimError()
        {
            // Independent, slowly changing errors keep bots from sharing the same predictable aim wave.
            aimErrorGoal = new Vector2((NextFloat() * 2 - 1) * Difficulty.AimYawDegrees, (NextFloat() * 2 - 1) * Difficulty.AimPitchDegrees);
            aimErrorAt = Now + .6f + NextFloat() * .5f;
        }

        void ResetBurstForWeapon()
        {
            string weapon = Owner.Ability && Owner.Ability.HasTemporaryWeapon ? Owner.Ability.EquippedId : Owner.Weapon.Spec.Id;
            if (firingWeapon == weapon) return;
            firingWeapon = weapon;
            combatTiming.ResetBurst(Now, Difficulty, RandomSource);
        }

        void TryCombatTrigger(bool shortRange)
        {
            if (!shortRange && !combatTiming.ReadyToTrigger(Now)) return;
            // A pause only releases the trigger. Do not cancel R8 wind-up or the weapon's internal burst.
            if (Owner.Weapon.TryFire() && !shortRange) combatTiming.AcceptedTrigger(Now, Difficulty, RandomSource);
        }

        void UseAbility(bool aimedAtEnemy, float distance)
        {
            var ability = Owner.Ability;
            if (!ability || !ability.Ready || ability.Blocked) return;
            switch (ability.EquippedId)
            {
                case "medical_pulse": if (Owner.HP <= Owner.MaxHP - 45) ability.Activate(); break;
                case "armor_shield": if (aimedAtEnemy) ability.Activate(); break;
                case "adrenaline": if (aimedAtEnemy || Now < investigateUntil) ability.Activate(); break;
                case "recon_pulse": if (aimedAtEnemy && distance < 25 || Now < investigateUntil && !aimedAtEnemy) ability.Activate(); break;
                case "frag_grenade": case "emp_grenade": if (aimedAtEnemy && distance > 6 && distance < 24) ability.Activate(); break;
                case "rocket_launcher": if (aimedAtEnemy && distance > 8 && distance < 50) ability.Activate(); break;
                case "underbarrel": if (aimedAtEnemy && distance > 5 && distance < 32) ability.Activate(); break;
                case "absolute_shot": if (aimedAtEnemy && (Owner.Weapon.Slot == "primary" || Owner.Weapon.Slot == "pistol")) ability.Activate(); break;
                default: if (aimedAtEnemy) ability.Activate(); break;
            }
        }

        void PickRoamPoint()
        {
            var world = GameRoot.Instance.World;
            var points = world ? world.DeathmatchSpawns : null;
            if (points != null && points.Length > 0)
            {
                // Spawn points cover the walkable routes; mixing far and nearby goals prevents stationary spawn camping.
                SetDestination(points[rng.Next(points.Length)] + new Vector3((NextFloat() - .5f) * 6, 0, (NextFloat() - .5f) * 6));
            }
            else SetDestination(transform.position + new Vector3((NextFloat() - .5f) * 30, 0, (NextFloat() - .5f) * 30));
        }

        void SetDestination(Vector3 position)
        {
            if (!navigation || !navigation.enabled || !navigation.isOnNavMesh) return;
            if (!NavMesh.SamplePosition(position, out var hit, 7, NavMesh.AllAreas)) return;
            destination = hit.position; destinationValid = navigation.SetDestination(destination);
        }

        System.Random RandomSource => rng ?? (rng = new System.Random(Owner.Id * 7919 + 73));
        float NextFloat() => (float)RandomSource.NextDouble();
    }

    // Match-clock timers are separate from movement and weapon simulation so their bounds can be checked directly.
    public sealed class BotCombatTiming
    {
        public float ReactionReadyAt { get; private set; } = float.PositiveInfinity;
        public float NextTrackingAt { get; private set; }
        public float BurstResumeAt { get; private set; }
        public int BurstShotsRemaining { get; private set; }

        public void Clear()
        {
            ReactionReadyAt = float.PositiveInfinity;
            NextTrackingAt = BurstResumeAt = 0;
            BurstShotsRemaining = 0;
        }

        public void Acquire(float now, BotDifficultySettings settings, System.Random random)
        {
            ReactionReadyAt = now + Between(settings.ReactionMinSeconds, settings.ReactionMaxSeconds, random);
            NextTrackingAt = now;
            ResetBurst(now, settings, random);
        }

        public bool TrackingDue(float now) => now >= NextTrackingAt;
        public bool ReadyToTrigger(float now) => now >= ReactionReadyAt && now >= BurstResumeAt;

        public void RecordTrackingSample(float now, BotDifficultySettings settings, System.Random random)
        {
            NextTrackingAt = now + Between(settings.TrackingMinSeconds, settings.TrackingMaxSeconds, random);
        }

        public void ResetBurst(float now, BotDifficultySettings settings, System.Random random)
        {
            BurstResumeAt = now;
            BurstShotsRemaining = random.Next(settings.BurstMinShots, settings.BurstMaxShots + 1);
        }

        public void AcceptedTrigger(float now, BotDifficultySettings settings, System.Random random)
        {
            if (--BurstShotsRemaining > 0) return;
            BurstShotsRemaining = random.Next(settings.BurstMinShots, settings.BurstMaxShots + 1);
            BurstResumeAt = now + Between(settings.BurstPauseMinSeconds, settings.BurstPauseMaxSeconds, random);
        }

        static float Between(float min, float max, System.Random random) => min + (max - min) * (float)random.NextDouble();
    }
}
