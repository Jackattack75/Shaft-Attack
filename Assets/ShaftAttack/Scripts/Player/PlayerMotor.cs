using System.Collections.Generic;
using UnityEngine;

namespace ShaftAttack
{
    public enum MoveState { Running, Airborne, Sliding, Dashing, Slamming }

    /// <summary>
    /// ULTRAKILL-style movement, modelled on how that game actually works:
    ///
    ///  - Ground movement is TIGHT. You hit full speed almost instantly, change direction
    ///    almost instantly, and stop almost instantly. No ice.
    ///  - You never gain speed just by turning. Steering redirects speed you already have.
    ///  - Speed above running pace only comes from tricks, and only lasts while you keep
    ///    doing tricks: slide (1.45x run), dash (3x run, resets your momentum), dash-jump,
    ///    slide-jump, slam-jump, wall jump - and bomb jumps, via AddImpulse.
    ///  - Landing while holding slide keeps your speed. Landing without it drops you back to
    ///    running pace. That's the whole momentum loop: slide, jump, land sliding, repeat.
    ///
    /// All accelerations here are plain metres per second squared.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        /// <summary>Every active player. Explosions use this to find who to launch.</summary>
        public static readonly List<PlayerMotor> All = new List<PlayerMotor>();

        [Header("Run")]
        [Tooltip("Always-on pace. There is no sprint key.")]
        public float runSpeed = 10f;
        [Tooltip("m/s². How fast you reach speed, turn and stop on the ground. High = crisp, no ice.")]
        public float groundAcceleration = 100f;

        [Header("Air")]
        [Tooltip("m/s². How hard you can steer mid-air. Redirects speed; never adds speed past what you had.")]
        public float airAcceleration = 30f;
        public float gravity = 34f;
        public float jumpHeight = 1.6f;
        public float maxFallSpeed = 55f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;

        [Header("Stamina + Dash (Shift)")]
        public int maxDashCharges = 3;
        [Tooltip("Seconds to refill one stamina bar.")]
        public float dashRechargeTime = 1.0f;
        [Tooltip("ULTRAKILL rule: stamina does not refill while sliding. Forces a choice.")]
        public bool noRechargeWhileSliding = true;
        [Tooltip("3x run speed, like ULTRAKILL.")]
        public float dashSpeed = 30f;
        public float dashDuration = 0.18f;

        [Header("Dash jump (Space during a ground dash)")]
        [Tooltip("Extra stamina on top of the dash itself. ULTRAKILL total is 2.")]
        public int dashJumpExtraCost = 1;
        [Range(0.3f, 1f)]
        [Tooltip("Fraction of dash speed carried into the jump. This is the big long-jump.")]
        public float dashJumpSpeedKeep = 0.8f;

        [Header("Slide (Ctrl on the ground)")]
        [Tooltip("Instant, constant slide speed. About 1.45x run, like ULTRAKILL.")]
        public float slideSpeed = 14.5f;
        [Tooltip("Degrees per second a slide can bend toward your input. Low = committed.")]
        public float slideTurnRate = 110f;
        [Tooltip("m/s². When you land in a slide faster than slideSpeed, how quickly the extra drains.")]
        public float slideExcessDecay = 6f;
        [Tooltip("Extra speed gained sliding downhill.")]
        public float slopeAcceleration = 18f;
        public float slideHeight = 0.9f;

        [Header("Wall jump (Space at a wall, in the air)")]
        public int wallJumpsPerAirtime = 3;
        public float wallJumpUpSpeed = 10.5f;
        public float wallJumpPushSpeed = 9f;
        [Tooltip("Grace window after touching a wall where Space still counts as a wall jump.")]
        public float wallMemoryTime = 0.15f;
        [Range(0f, 1f)]
        [Tooltip("How much of your speed ALONG the wall survives the jump.")]
        public float wallJumpAlongKeep = 0.9f;

        [Header("Ground slam (Ctrl in the air)")]
        public float slamSpeed = 50f;
        [Tooltip("Jump right after a slam lands to launch this fast upward.")]
        public float slamJumpSpeed = 17f;
        public float slamJumpWindow = 0.2f;

        [Header("Launches (bomb jumps)")]
        [Tooltip("After a blast launches you, ground friction is ignored this long so it can't eat the launch.")]
        public float launchGraceTime = 0.15f;

        [Header("Stance")]
        public float standHeight = 1.8f;
        public float stanceChangeSpeed = 14f;
        public Transform cameraPivot;
        public float eyeOffsetFromTop = 0.18f;

        [Header("Collision")]
        [Tooltip("The player's own layer is stripped out automatically.")]
        public LayerMask obstructionMask = ~0;

        // --- read from anywhere: HUD, camera feel, animation, audio ---
        public MoveState State { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsSliding { get; private set; }
        public float Speed { get; private set; }
        public Vector3 Velocity { get { return velocity; } }
        public int DashCharges { get { return dashCharges; } }
        public float DashRecharge { get { return dashCharges >= maxDashCharges ? 1f : rechargeTimer / dashRechargeTime; } }
        public int WallJumpsLeft { get { return wallJumpsLeft; } }
        public bool TouchingWall { get { return Time.time - lastWallTouchTime <= wallMemoryTime; } }
        public Vector3 LastWallNormal { get { return lastWallNormal; } }
        /// <summary>Roughly where the body's centre is. Explosions aim knockback from here.</summary>
        public Vector3 BodyCenter { get { return transform.position + Vector3.up * (controller != null ? controller.height * 0.5f : 0.9f); } }

        // --- events: camera feel, sound, and later slam-breaks-blocks ---
        public System.Action OnJumped;
        public System.Action OnDashed;
        public System.Action OnWallJumped;
        public System.Action OnSlideStarted;
        public System.Action<Vector3> OnSlamImpact;
        public System.Action<Vector3> OnLaunched;

        private CharacterController controller;
        private Vector3 velocity;

        private float lastGroundedTime = -99f;
        private float lastJumpPressedTime = -99f;
        private float launchTimer;

        private int dashCharges;
        private float rechargeTimer;
        private float dashTimeLeft;
        private Vector3 dashDirection;
        private bool dashStartedGrounded;

        private bool isSlamming;
        private float slamLandedTime = -99f;

        private Vector3 slideDirection;
        private float slideCurrentSpeed;

        private int wallJumpsLeft;
        private Vector3 lastWallNormal;
        private float lastWallTouchTime = -99f;

        private Vector3 groundNormal = Vector3.up;
        private float targetHeight;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            obstructionMask &= ~(1 << gameObject.layer);

            dashCharges = maxDashCharges;
            wallJumpsLeft = wallJumpsPerAirtime;
            targetHeight = standHeight;
            ApplyHeight(standHeight);
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (launchTimer > 0f) launchTimer -= dt;
            RefreshGround();
            RechargeStamina(dt);

            if (GameInput.JumpPressedThisFrame) lastJumpPressedTime = Time.time;

            if (dashTimeLeft > 0f) TickDash(dt);
            else if (isSlamming) TickSlam();
            else TickNormal(dt);

            UpdateHeight(dt);

            controller.Move(velocity * dt);

            if ((controller.collisionFlags & CollisionFlags.Above) != 0 && velocity.y > 0f)
                velocity.y = 0f;

            Speed = new Vector2(velocity.x, velocity.z).magnitude;
            UpdateState();
        }

        // ------------------------------------------------------------------ external forces

        /// <summary>
        /// Shove the player - bombs, and later other players' explosions. Cancels dash, slam and
        /// slide, and briefly ignores the ground so friction can't swallow the launch.
        /// </summary>
        public void AddImpulse(Vector3 impulse)
        {
            dashTimeLeft = 0f;
            isSlamming = false;
            if (IsSliding) StopSliding();

            // Falling into a blast should still launch you: cancel the fall first.
            if (impulse.y > 0f && velocity.y < 0f) velocity.y = 0f;
            velocity += impulse;

            launchTimer = launchGraceTime;
            lastGroundedTime = -99f; // no coyote jump overwriting the launch
            if (OnLaunched != null) OnLaunched(impulse);
        }

        // ------------------------------------------------------------------ main tick

        private void TickNormal(float dt)
        {
            if (TryStartDash()) return;
            if (TryStartSlam()) return;

            UpdateSlide(dt);
            TryJump();

            Vector3 wishDir = GetWishDir();
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

            if (IsSliding)
            {
                flat = slideDirection * slideCurrentSpeed;
            }
            else if (IsGrounded)
            {
                // Tight: straight toward what you're pressing. Let go and you stop.
                flat = Vector3.MoveTowards(flat, wishDir * runSpeed, groundAcceleration * dt);
            }
            else if (wishDir.sqrMagnitude > 0.001f)
            {
                // Air: steer toward your input, but the target is never faster than what you
                // already have (or running pace, if you're slower). Turning can't add speed.
                float cap = Mathf.Max(runSpeed, flat.magnitude);
                flat = Vector3.MoveTowards(flat, wishDir * cap, airAcceleration * dt);
            }
            // No input in the air: keep your momentum exactly as it was.

            velocity.x = flat.x;
            velocity.z = flat.z;

            if (IsGrounded && velocity.y < 0f) velocity.y = -3f;
            else velocity.y = Mathf.Max(velocity.y - gravity * dt, -maxFallSpeed);
        }

        private void RefreshGround()
        {
            // During a launch we pretend the floor isn't there, so it can't eat the launch.
            IsGrounded = controller.isGrounded && launchTimer <= 0f;
            if (!IsGrounded) return;

            lastGroundedTime = Time.time;
            wallJumpsLeft = wallJumpsPerAirtime;
        }

        private void UpdateState()
        {
            if (dashTimeLeft > 0f) State = MoveState.Dashing;
            else if (isSlamming) State = MoveState.Slamming;
            else if (IsSliding) State = MoveState.Sliding;
            else if (!IsGrounded) State = MoveState.Airborne;
            else State = MoveState.Running;
        }

        // ------------------------------------------------------------------ jump / wall jump

        private void TryJump()
        {
            if (Time.time - lastJumpPressedTime > jumpBufferTime) return;

            bool canGroundJump = Time.time - lastGroundedTime <= coyoteTime;

            if (canGroundJump)
            {
                // Slide jump: you leave the slide but keep all its speed in the air.
                if (IsSliding) StopSliding();

                bool slamJump = Time.time - slamLandedTime <= slamJumpWindow;
                velocity.y = slamJump ? slamJumpSpeed : JumpVelocity();

                ConsumeJump();
                if (OnJumped != null) OnJumped();
                return;
            }

            if (wallJumpsLeft > 0 && TouchingWall) DoWallJump();
        }

        private float JumpVelocity()
        {
            return Mathf.Sqrt(2f * gravity * jumpHeight);
        }

        private void ConsumeJump()
        {
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;
        }

        private void DoWallJump()
        {
            wallJumpsLeft--;

            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 n = new Vector3(lastWallNormal.x, 0f, lastWallNormal.z).normalized;

            // Keep the speed you had running ALONG the wall, throw away anything going
            // into it, then push off. No reflection tricks, so no free speed.
            Vector3 along = flat - Vector3.Dot(flat, n) * n;
            Vector3 result = along * wallJumpAlongKeep + n * wallJumpPushSpeed;

            // Lean toward where you're steering so chaining walls feels deliberate.
            Vector3 wish = GetWishDir();
            if (wish.sqrMagnitude > 0.001f)
                result = Vector3.Lerp(result, wish * result.magnitude, 0.3f);

            velocity.x = result.x;
            velocity.z = result.z;
            velocity.y = wallJumpUpSpeed;

            lastJumpPressedTime = -99f;
            lastWallTouchTime = -99f;
            if (OnWallJumped != null) OnWallJumped();
        }

        // ------------------------------------------------------------------ stamina + dash

        private void RechargeStamina(float dt)
        {
            if (dashCharges >= maxDashCharges)
            {
                rechargeTimer = 0f;
                return;
            }

            if (noRechargeWhileSliding && IsSliding) return;

            rechargeTimer += dt;
            if (rechargeTimer >= dashRechargeTime)
            {
                rechargeTimer = 0f;
                dashCharges++;
            }
        }

        private bool TryStartDash()
        {
            if (!GameInput.DashPressedThisFrame) return false;
            if (dashCharges <= 0) return false;

            Vector3 dir = GetWishDir();
            if (dir.sqrMagnitude < 0.001f) dir = FlatForward();

            dashCharges--;
            dashTimeLeft = dashDuration;
            dashDirection = dir;
            dashStartedGrounded = IsGrounded || Time.time - lastGroundedTime <= coyoteTime;
            if (IsSliding) StopSliding();

            // A dash RESETS momentum - you go exactly where you pressed, at exactly dash speed.
            velocity = dashDirection * dashSpeed;
            velocity.y = 0f;

            if (OnDashed != null) OnDashed();
            return true;
        }

        private void TickDash(float dt)
        {
            dashTimeLeft -= dt;

            // Dash jump: jump during a ground dash to launch with most of the dash speed.
            // Costs extra stamina. This is the long-jump.
            bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
            if (jumpBuffered && dashStartedGrounded && dashCharges >= dashJumpExtraCost)
            {
                dashCharges -= dashJumpExtraCost;
                dashTimeLeft = 0f;

                Vector3 carry = dashDirection * (dashSpeed * dashJumpSpeedKeep);
                velocity = new Vector3(carry.x, JumpVelocity(), carry.z);

                ConsumeJump();
                if (OnJumped != null) OnJumped();
                return;
            }

            velocity = dashDirection * dashSpeed;
            velocity.y = 0f;

            if (dashTimeLeft <= 0f)
            {
                // Clean exit at running pace. Want to keep the speed? Dash-jump or slide out of it.
                dashTimeLeft = 0f;
                velocity = dashDirection * runSpeed;
            }
        }

        // ------------------------------------------------------------------ ground slam

        private bool TryStartSlam()
        {
            if (IsGrounded) return false;
            if (!GameInput.SlidePressedThisFrame) return false;

            isSlamming = true;
            if (IsSliding) StopSliding();
            return true;
        }

        private void TickSlam()
        {
            velocity.x = 0f;
            velocity.z = 0f;
            velocity.y = -slamSpeed;

            if (IsGrounded)
            {
                isSlamming = false;
                slamLandedTime = Time.time;
                velocity.y = -3f;
                if (OnSlamImpact != null) OnSlamImpact(transform.position);
            }
        }

        // ------------------------------------------------------------------ slide

        private void UpdateSlide(float dt)
        {
            bool held = GameInput.SlideHeld;

            // Start (or resume, on landing) a slide.
            if (held && IsGrounded && !IsSliding)
            {
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                Vector3 wish = GetWishDir();

                slideDirection = wish.sqrMagnitude > 0.001f ? wish
                               : flat.sqrMagnitude > 0.01f ? flat.normalized
                               : FlatForward();

                // Instant to full slide speed - and if you landed faster than that, you keep it.
                slideCurrentSpeed = Mathf.Max(slideSpeed, flat.magnitude);
                IsSliding = true;
                targetHeight = slideHeight;
                if (OnSlideStarted != null) OnSlideStarted();
                return;
            }

            if (!IsSliding) return;

            // Airborne mid-slide: hand over to air movement. Landing with Ctrl held resumes it.
            if (!IsGrounded || !held)
            {
                StopSliding();
                return;
            }

            // Gentle steering. Pure backwards input is ignored so S doesn't spin you around.
            Vector3 input = GetWishDir();
            if (input.sqrMagnitude > 0.001f && Vector3.Dot(input, slideDirection) > -0.3f)
            {
                slideDirection = Vector3.RotateTowards(
                    slideDirection, input, slideTurnRate * Mathf.Deg2Rad * dt, 0f);
                slideDirection.y = 0f;
                slideDirection.Normalize();
            }

            // Downhill speeds you up, uphill slows you - but never below base slide speed.
            float slopeGain = 0f;
            if (OnSlope())
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                downhill.y = 0f;
                float along = Vector3.Dot(downhill.normalized, slideDirection);
                slopeGain = along * slopeAcceleration * (1f - groundNormal.y) * 4f;
            }

            if (slopeGain > 0f)
                slideCurrentSpeed += slopeGain * dt;
            else
                slideCurrentSpeed = Mathf.MoveTowards(slideCurrentSpeed, slideSpeed,
                    (slideExcessDecay - slopeGain) * dt);

            slideCurrentSpeed = Mathf.Max(slideSpeed, slideCurrentSpeed);
        }

        private void StopSliding()
        {
            IsSliding = false;
            targetHeight = standHeight;
        }

        private bool OnSlope()
        {
            return IsGrounded && groundNormal.y < 0.995f && groundNormal.y > 0.1f;
        }

        // ------------------------------------------------------------------ stance

        private void UpdateHeight(float dt)
        {
            // Never stand up into a ceiling - stay low until there's room.
            float want = targetHeight;
            if (want > controller.height && !HasHeadroom()) want = controller.height;

            float h = Mathf.MoveTowards(controller.height, want, stanceChangeSpeed * dt);
            if (!Mathf.Approximately(h, controller.height)) ApplyHeight(h);
        }

        private void ApplyHeight(float height)
        {
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);

            if (cameraPivot != null)
                cameraPivot.localPosition = new Vector3(0f, height - eyeOffsetFromTop, 0f);
        }

        private bool HasHeadroom()
        {
            float r = Mathf.Max(0.05f, controller.radius * 0.95f);
            Vector3 bottom = transform.position + Vector3.up * (r + 0.02f);
            Vector3 top = transform.position + Vector3.up * (standHeight - r - 0.02f);
            return !Physics.CheckCapsule(bottom, top, r, obstructionMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ helpers

        private Vector3 GetWishDir()
        {
            Vector2 input = GameInput.Move;
            Vector3 dir = transform.right * input.x + transform.forward * input.y;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        private Vector3 FlatForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.normalized;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.5f)
            {
                groundNormal = hit.normal;
            }
            else if (Mathf.Abs(hit.normal.y) < 0.4f)
            {
                lastWallNormal = hit.normal;
                lastWallTouchTime = Time.time;
            }
        }

        /// <summary>Used by respawn, and later by the digging system when the floor vanishes.</summary>
        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            velocity = Vector3.zero;
            dashCharges = maxDashCharges;
            isSlamming = false;
            dashTimeLeft = 0f;
            StopSliding();
        }
    }
}
