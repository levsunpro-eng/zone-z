using UnityEngine;

namespace ZoneZ
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public Actor Owner;
        float yaw, pitch, verticalVelocity, recoilPitch, recoilYaw, distanceSinceStep, height = 1.8f;
        Vector2 movement;
        Vector3 horizontalVelocity;
        bool jumpRequested, crouch, walk;
        bool primaryPressed, primaryHeld, primaryReleased, alternatePressed, reloadRequested, abilityRequested, inspectRequested, useRequested;
        string slotRequested;
        Actor spectating;
        public Actor SpectatingActor => spectating;
        int inputSequence;
        float lastRemoteInput = -100;

        public void ResetLook()
        {
            yaw = transform.eulerAngles.y; pitch = 0; recoilPitch = recoilYaw = 0; verticalVelocity = -2;
            horizontalVelocity = Vector3.zero; movement = Vector2.zero; height = 1.8f; spectating = null;
            crouch = walk = jumpRequested = false; distanceSinceStep = 0;
            ClearCombatInput();
            lastRemoteInput = -100;
            if (Owner && Owner.ViewCamera)
            {
                Owner.ViewCamera.transform.SetParent(Owner.Aim, false); Owner.ViewCamera.transform.localPosition = Vector3.zero;
                Owner.ViewCamera.transform.localRotation = Quaternion.identity;
            }
        }

        void Start() { ResetLook(); }

        void Update()
        {
            if (!Owner || !Owner.Match || !GameRoot.Instance) return;
            if (!Owner.Human) return;
            if (Owner.IsNetworkReplica && GameRoot.Instance.Network.TestInputOverride) return;
            if (!Owner.Alive) { movement = Vector2.zero; jumpRequested = false; ClearCombatInput(); SubmitClientInput(); UpdateSpectator(); return; }
            if (GameRoot.Instance.Paused || Owner.Match.Phase != MatchPhase.Playing || Cursor.lockState != CursorLockMode.Locked)
            {
                movement = Vector2.zero; crouch = walk = jumpRequested = false; ClearCombatInput(); SubmitClientInput(); return;
            }
            var profile = GameRoot.Instance.Profile;
            float sensitivity = Mathf.Clamp(profile.sensitivity, .05f, 10) * 1.75f;
            if (Owner.Weapon && Owner.Weapon.Scoped)
                sensitivity *= Mathf.Clamp(Owner.Weapon.ScopeFov / Mathf.Max(30, profile.fov), .18f, 1) * Mathf.Clamp(profile.scopeSensitivity, .05f, 5);
            yaw += Input.GetAxisRaw("Mouse X") * sensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * sensitivity * (profile.invertY ? -1 : 1), -86, 86);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Owner.Aim.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilPitch, -88, 88), recoilYaw, 0);
            movement = Vector2.ClampMagnitude(new Vector2((GameInput.Held("right") ? 1 : 0) - (GameInput.Held("left") ? 1 : 0), (GameInput.Held("forward") ? 1 : 0) - (GameInput.Held("backward") ? 1 : 0)), 1);
            crouch = GameInput.Held("crouch");
            walk = GameInput.Held("walk");
            if (GameInput.Down("jump")) jumpRequested = true;
            if (GameInput.Down("slot1")) slotRequested = "primary";
            if (GameInput.Down("slot2")) slotRequested = "pistol";
            if (GameInput.Down("slot3")) slotRequested = "knife";
            if (GameInput.Down("slot4")) slotRequested = "zeus";
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > .01f)
            {
                string[] slots = { "primary", "pistol", "knife", "zeus" }; int at = System.Array.IndexOf(slots, slotRequested ?? Owner.Weapon.Slot);
                slotRequested = slots[(at + (scroll > 0 ? 3 : 1)) % 4];
            }
            reloadRequested |= GameInput.Down("reload");
            abilityRequested |= GameInput.Down("ultimate");
            inspectRequested |= GameInput.Down("inspect");
            useRequested |= GameInput.Down("use");
            primaryReleased |= GameInput.Up("fire");
            alternatePressed |= GameInput.Down("alternate");
            primaryPressed |= GameInput.Down("fire");
            primaryHeld = GameInput.Held("fire");
            SubmitClientInput();
        }

        void SubmitClientInput()
        {
            var network = GameRoot.Instance?.Network;
            if (network?.IsClient != true) return;
            string[] slots = { "primary", "pistol", "knife", "zeus" };
            network.SubmitInput(new NetworkCommand {
                sequence = ++inputSequence, moveX = movement.x, moveY = movement.y, yaw = yaw, pitch = pitch,
                jump = jumpRequested, crouch = crouch, walk = walk, fire = primaryHeld,
                firePressed = primaryPressed, fireReleased = primaryReleased, alt = alternatePressed,
                reload = reloadRequested, use = useRequested, ultimate = abilityRequested,
                slot = System.Array.IndexOf(slots, slotRequested)
            });
            if (inspectRequested) Owner.Weapon?.Inspect();
            jumpRequested = primaryPressed = primaryReleased = alternatePressed = reloadRequested = abilityRequested = inspectRequested = useRequested = false;
            slotRequested = null;
        }

        public void ApplyNetworkCommand(NetworkCommand command)
        {
            if (!Owner || !Owner.RemoteControlled || Owner.IsNetworkReplica || command == null) return;
            if (!Finite(command.moveX) || !Finite(command.moveY) || !Finite(command.yaw) || !Finite(command.pitch)) return;
            lastRemoteInput = Time.realtimeSinceStartup;
            if (!Owner.Alive || !Owner.Match || Owner.Match.Phase != MatchPhase.Playing)
            {
                movement = Vector2.zero; jumpRequested = false; ClearCombatInput(); return;
            }
            movement = Vector2.ClampMagnitude(new Vector2(command.moveX, command.moveY), 1);
            yaw = Mathf.Repeat(command.yaw, 360); pitch = Mathf.Clamp(command.pitch, -86, 86);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            crouch = command.crouch; walk = command.walk; jumpRequested |= command.jump;
            primaryPressed |= command.firePressed || command.fire && !primaryHeld;
            primaryReleased |= command.fireReleased || !command.fire && primaryHeld;
            primaryHeld = command.fire; alternatePressed |= command.alt;
            reloadRequested |= command.reload; abilityRequested |= command.ultimate; useRequested |= command.use;
            if (command.slot >= 0 && command.slot < 4) slotRequested = new[] { "primary", "pistol", "knife", "zeus" }[command.slot];
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        void FixedUpdate()
        {
            if (!Owner || !Owner.Alive || !Owner.Match || !GameRoot.Instance) return;
            if (Owner.IsNetworkReplica)
            {
                recoilPitch = Mathf.MoveTowards(recoilPitch, 0, Time.fixedDeltaTime * 6);
                recoilYaw = Mathf.MoveTowards(recoilYaw, 0, Time.fixedDeltaTime * 6);
                return;
            }
            if (!Owner.Motor || !Owner.Motor.enabled || GameRoot.Instance.SimulationPaused || Owner.Match.Phase != MatchPhase.Playing) return;
            if (Owner.RemoteControlled && Time.realtimeSinceStartup - lastRemoteInput > .3f)
            {
                movement = Vector2.zero; crouch = walk = jumpRequested = false; ClearCombatInput();
                Owner.Weapon?.CancelPrimaryWindup();
            }
            recoilPitch = Mathf.MoveTowards(recoilPitch, 0, Time.fixedDeltaTime * 6);
            recoilYaw = Mathf.MoveTowards(recoilYaw, 0, Time.fixedDeltaTime * 6);
            Owner.Aim.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilPitch, -88, 88), recoilYaw, 0);
            Owner.Visual?.SetAim(pitch - recoilPitch);
            bool grounded = Owner.Motor.isGrounded;
            bool blockedAbove = !crouch && height < 1.79f && CeilingBlocked();
            float targetHeight = crouch || blockedAbove ? 1.2f : 1.8f;
            height = Mathf.MoveTowards(height, targetHeight, Time.fixedDeltaTime * 5);
            Owner.SetStance(targetHeight < 1.7f, height);
            float speed = Owner.MovementSpeed * (Owner.IsCrouching ? .55f : walk ? .52f : 1);
            Vector3 desired = (transform.right * movement.x + transform.forward * movement.y) * speed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, Time.fixedDeltaTime * (grounded ? 32 : 9));
            if (grounded && verticalVelocity < 0) verticalVelocity = -2;
            if (jumpRequested && grounded && !Owner.IsCrouching) verticalVelocity = Mathf.Sqrt(2 * 23 * 1.05f);
            jumpRequested = false; verticalVelocity -= 23 * Time.fixedDeltaTime;
            Vector3 before = transform.position;
            CollisionFlags collision = Owner.Motor.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.fixedDeltaTime);
            if ((collision & CollisionFlags.Above) != 0 && verticalVelocity > 0) verticalVelocity = 0;
            Owner.CurrentVelocity = (transform.position - before) / Time.fixedDeltaTime;
            distanceSinceStep += new Vector2(Owner.CurrentVelocity.x, Owner.CurrentVelocity.z).magnitude * Time.fixedDeltaTime;
            if (grounded && distanceSinceStep > (Owner.IsCrouching ? 1.5f : .95f))
            {
                distanceSinceStep = 0; AudioFX.Footstep(Owner, walk || Owner.IsCrouching);
            }
            if (transform.position.y < -12) Owner.Kill(null, false);
            HandleCombatInput();
        }

        void HandleCombatInput()
        {
            if (!Owner.Weapon) { ClearCombatInput(); return; }
            if (slotRequested != null) Owner.Weapon.SelectSlot(slotRequested);
            if (reloadRequested) Owner.Weapon.Reload();
            if (abilityRequested) Owner.Ability?.Activate();
            if (inspectRequested) Owner.Weapon.Inspect();
            if (useRequested) LootPickup.TryNearest(Owner);
            if (primaryReleased) Owner.Weapon.CancelPrimaryWindup();
            if (alternatePressed) Owner.Weapon.Alternate();
            bool automatic = Owner.Weapon.Spec != null && Owner.Weapon.Spec.Automatic && !Owner.Weapon.BurstMode;
            if (Owner.Ability && Owner.Ability.HasTemporaryWeapon) automatic = true;
            if (primaryPressed || automatic && primaryHeld) Owner.Weapon.TryFire();
            // Releasing R8 before the next simulation tick must still cancel its new windup.
            if (primaryReleased && !primaryHeld) Owner.Weapon.CancelPrimaryWindup();
            primaryPressed = primaryReleased = alternatePressed = reloadRequested = abilityRequested = inspectRequested = useRequested = false;
            slotRequested = null;
        }

        void ClearCombatInput()
        {
            primaryPressed = primaryHeld = primaryReleased = alternatePressed = reloadRequested = abilityRequested = inspectRequested = useRequested = false;
            slotRequested = null;
        }

        void LateUpdate()
        {
            if (!Owner || !Owner.Alive || !Owner.Aim || !GameRoot.Instance || !Owner.Human && Owner.IsNetworkReplica) return;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Owner.Aim.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilPitch, -88, 88), recoilYaw, 0);
            if (Owner.ViewCamera)
            {
                float fov = Owner.Weapon && Owner.Weapon.Scoped ? Owner.Weapon.ScopeFov : GameRoot.Instance.Profile.fov;
                Owner.ViewCamera.fieldOfView = Mathf.Lerp(Owner.ViewCamera.fieldOfView, fov, 1 - Mathf.Exp(-14 * Time.unscaledDeltaTime));
            }
        }

        bool CeilingBlocked()
        {
            var hits = Physics.OverlapCapsule(transform.position + Vector3.up * .4f, transform.position + Vector3.up * 1.47f, .29f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits) if (hit.GetComponentInParent<Actor>() != Owner) return true;
            return false;
        }

        public void AddRecoil(float up, float side)
        {
            recoilPitch = Mathf.Clamp(recoilPitch + up, 0, 13); recoilYaw = Mathf.Clamp(recoilYaw + side, -7, 7);
            if (Owner && Owner.Aim) Owner.Aim.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilPitch, -88, 88), recoilYaw, 0);
        }

        void UpdateSpectator()
        {
            if (!Owner.ViewCamera || !Owner.Match || !Owner.Match.IsTeamMode || Owner.Match.Phase != MatchPhase.Playing || GameRoot.Instance.Paused) return;
            if (!spectating || !spectating.Alive || GameInput.Down("fire") || GameInput.Down("alternate"))
            {
                var all = Owner.Match.Actors;
                int step = GameInput.Down("alternate") ? -1 : 1;
                int start = spectating ? all.IndexOf(spectating) + step : step > 0 ? 0 : all.Count - 1;
                spectating = null;
                for (int i = 0; i < all.Count; i++)
                {
                    var candidate = all[(start + i * step + all.Count * 2) % all.Count];
                    if (candidate && candidate.Alive && candidate.Team == Owner.Team) { spectating = candidate; break; }
                }
            }
            if (spectating)
            {
                Vector3 focus = spectating.EyePosition;
                Vector3 offset = -spectating.transform.forward * 2.7f + Vector3.up * .7f;
                float distance = offset.magnitude;
                foreach (var hit in Physics.SphereCastAll(focus, .16f, offset.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<Actor>()) continue;
                    distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .05f));
                }
                Vector3 cameraPosition = focus + offset.normalized * distance;
                Owner.ViewCamera.transform.SetPositionAndRotation(cameraPosition,
                    Quaternion.LookRotation(focus + spectating.Aim.forward * 5 - cameraPosition));
                Owner.ViewCamera.fieldOfView = GameRoot.Instance.Profile.fov;
            }
        }
    }
}
