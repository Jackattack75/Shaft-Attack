using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Feeds Digby's Animator (Digby.controller) from how he actually moves:
    ///   Speed     ground speed in m/s (blend tree: Idle 0, Walk 0.5, Run 3.375, Sprint 10)
    ///   MoveRate  playback speed of the locomotion; faster than his fastest clip it plays faster,
    ///             and negative (backwards) when the legs face away from where he's going
    ///   Grounded  false after a short moment off the ground
    ///   VSpeed    how fast he is moving up (m/s): going up / the top / coming down poses in the air
    ///   Jump      fired by PlayerMotor's jump events, or when he shoots upward off the ground
    ///   WallJump + WallSide   kick off a wall, with which side the wall was on
    ///   Sliding / Slamming / Dash / SlamLand   the player's moves, straight from PlayerMotor
    /// and plays the arm moves on their own layers, over whatever the legs are doing:
    ///   PickHit / PickMiss   on every pickaxe swing (MinerTools.OnPickaxeSwing); the pickaxe comes off his back.
    ///                        The clip starts ON the hit (the game digs the instant you click), then winds up and
    ///                        strikes again in time with the next swing. Let go early and he skips the wind-up and
    ///                        hangs the pickaxe back on his back, so one click is one strike.
    ///   Throw                when a bomb is thrown (MinerTools.OnBombThrown)
    /// While he digs or throws, his upper body leans toward where the player is looking.
    /// Works for anything that moves him: the player (PlayerMotor), a bot, or a networked copy
    /// (then it just watches the transform; call Jump / Swing / Throw yourself). Sits on the prefab root
    /// (DigbyBody on the player). Everything is optional: with an older controller the extra bits are skipped.
    /// </summary>
    [DisallowMultipleComponent]
    public class DigbyAnimDriver : MonoBehaviour
    {
        [Tooltip("Ground speed of the Walk clip in m/s (from the Blender export).")]
        public float walkSpeed = 0.5f;

        [Tooltip("Ground speed of the Run clip in m/s.")]
        public float runSpeed = 3.375f;

        [Tooltip("Ground speed of the Sprint clip in m/s (the player's run). Faster than this, it plays faster.")]
        public float sprintSpeed = 10f;

        [Tooltip("Cap on how much faster than normal the fastest clip may play.")]
        public float maxRunRate = 1.6f;

        [Tooltip("Upward speed (m/s) that counts as a jump when no PlayerMotor tells us directly.")]
        public float jumpDetectSpeed = 2.5f;

        [Tooltip("Seconds off the ground before he counts as airborne (stops tiny bumps triggering the fall pose).")]
        public float airGrace = 0.12f;

        [Tooltip("Turn him to face the way he is moving (for bots). Players face where they look instead.")]
        public bool faceMovement = false;
        public float turnSpeed = 540f;

        [Header("Player body")]
        [Tooltip("Player: the body turns part of the way toward strafes and runs backwards when you backpedal.")]
        public bool legsFollowMovement = false;
        [Tooltip("Most the body turns away from where you look (degrees).")]
        public float maxLegYaw = 55f;
        public float legTurnSpeed = 420f;

        [Header("Digging and throwing")]
        [Range(0f, 1f)]
        [Tooltip("While he digs or throws, his upper body leans this much of the way toward where you look (up or down).")]
        public float aimLean = 0.6f;
        [Tooltip("How fast that lean comes in (per second). Fast, so the first hit already lands where you aim.")]
        public float aimInSpeed = 30f;
        [Tooltip("How fast he straightens up again afterwards (per second).")]
        public float aimOutSpeed = 6f;
        [Range(0f, 1f)]
        [Tooltip("Point in the swing clip where he hangs the pickaxe back on his back.")]
        public float stowAt = 0.78f;
        [Tooltip("Seconds after a swing starts. Let go of the dig button before this and he puts the pickaxe away " +
                 "instead of winding up again; after it the next strike is already on its way and finishes.")]
        public float swingCommit = 0.15f;

        /// <summary>Is the dig button still down? The player's MinerTools sets this every frame; bots and
        /// networked copies set it themselves (leave it false and every Swing is a single strike).</summary>
        [System.NonSerialized] public bool digHeld;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int RateId = Animator.StringToHash("MoveRate");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int SlidingId = Animator.StringToHash("Sliding");
        private static readonly int SlammingId = Animator.StringToHash("Slamming");
        private static readonly int DashId = Animator.StringToHash("Dash");
        private static readonly int SlamLandId = Animator.StringToHash("SlamLand");
        private static readonly int VSpeedId = Animator.StringToHash("VSpeed");
        private static readonly int WallJumpId = Animator.StringToHash("WallJump");
        private static readonly int WallSideId = Animator.StringToHash("WallSide");
        private const float SwingClipLength = 20f / 30f;    // PickHit / PickMiss: 20 frames at 30 fps
        private const float SwingPutAwayFrom = 12f / 30f;   // where "lift it out and hang it on his back" starts
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private Animator animator;
        private PlayerMotor motor;
        private PlayerLook look;
        private MinerTools tools;
        private DigbyPickaxe pickaxe;
        private CharacterController controller;
        private Transform spine;
        private Transform chest;
        private Vector3 lastPos;
        private float speed;
        private float airTime;
        private bool wasGrounded = true;
        private bool hasMoves;
        private bool hasSprint;
        private bool hasAir;
        private bool hasWall;
        private int rightArmLayer = -1;
        private int leftArmLayer = -1;
        private float legYaw;
        private float rateSign = 1f;
        private float aimUntil = -1f;
        private float aimWeight;
        private bool swinging;
        private bool puttingAway;
        private string swingState;
        private float swingStart;       // game time the swing clip started (moved when we skip ahead in it)

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            motor = GetComponentInParent<PlayerMotor>();
            look = GetComponentInParent<PlayerLook>();
            tools = GetComponentInParent<MinerTools>();
            pickaxe = GetComponent<DigbyPickaxe>();
            controller = GetComponentInParent<CharacterController>();
            spine = Find(transform, "Spine");
            chest = Find(transform, "Chest");
            lastPos = transform.position;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.nameHash == SlidingId) hasMoves = true;
                    if (p.nameHash == VSpeedId) hasAir = true;
                    if (p.nameHash == WallJumpId) hasWall = true;
                }
                foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
                    if (c != null && c.name == "Sprint") hasSprint = true;
                rightArmLayer = animator.GetLayerIndex("RightArm");
                leftArmLayer = animator.GetLayerIndex("LeftArm");
            }
        }

        private void OnEnable()
        {
            lastPos = transform.position;
            if (motor != null)
            {
                motor.OnJumped += Jump;
                motor.OnWallJumped += WallJumped;
                motor.OnDashed += Dashed;
                motor.OnSlamImpact += SlamLanded;
            }
            if (tools != null)
            {
                tools.OnPickaxeSwing += Swing;
                tools.OnBombThrown += Throw;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnJumped -= Jump;
                motor.OnWallJumped -= WallJumped;
                motor.OnDashed -= Dashed;
                motor.OnSlamImpact -= SlamLanded;
            }
            if (tools != null)
            {
                tools.OnPickaxeSwing -= Swing;
                tools.OnBombThrown -= Throw;
            }
        }

        /// <summary>Play the take-off. Call this from bots / netcode when they jump.</summary>
        public void Jump()
        {
            if (animator != null) animator.SetTrigger(JumpId);
        }

        /// <summary>One pickaxe swing, starting on the hit. hit = false: nothing was in reach, it swings through.</summary>
        public void Swing(bool hit)
        {
            if (animator == null || rightArmLayer < 0) return;
            swingState = hit ? "PickHit" : "PickMiss";
            animator.CrossFadeInFixedTime(swingState, 0.04f, rightArmLayer, 0f);
            swingStart = Time.time;
            swinging = true;
            puttingAway = false;
            if (pickaxe != null) pickaxe.SetInHand(true);
            aimUntil = Time.time + 0.3f;
        }

        /// <summary>Throw with the left arm (bombs).</summary>
        public void Throw()
        {
            if (animator == null || leftArmLayer < 0) return;
            animator.CrossFadeInFixedTime("Throw", 0.03f, leftArmLayer, 0f);
            aimUntil = Mathf.Max(aimUntil, Time.time + 0.2f);
        }

        private void WallJumped()
        {
            if (animator == null) return;
            if (!hasWall) { Jump(); return; }
            // which side was the wall on? Its normal points away from it: toward his right = the wall is on his left.
            Transform frame = transform.parent != null ? transform.parent : transform;
            float away = motor != null ? Vector3.Dot(frame.right, motor.LastWallNormal) : 0f;
            animator.SetFloat(WallSideId, -Mathf.Clamp(away * 1.6f, -1f, 1f));
            animator.SetTrigger(WallJumpId);
        }

        private void Dashed()
        {
            if (animator != null && hasMoves) animator.SetTrigger(DashId);
        }

        private void SlamLanded(Vector3 where)
        {
            if (animator != null && hasMoves) animator.SetTrigger(SlamLandId);
        }

        private void Update()
        {
            if (animator == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 p = transform.position;
            Vector3 v = (p - lastPos) / dt;
            lastPos = p;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float up = v.y;
            if (motor != null)
            {
                Vector3 mv = motor.Velocity;
                flat = new Vector3(mv.x, 0f, mv.z);
                up = mv.y;
            }
            float target = motor != null ? motor.Speed : flat.magnitude;
            speed = Mathf.Lerp(speed, target, 1f - Mathf.Exp(-14f * dt));

            bool onGround = motor != null ? motor.IsGrounded : (controller != null ? controller.isGrounded : Probe());
            airTime = onGround ? 0f : airTime + dt;
            bool grounded = airTime < airGrace;
            if (motor == null && wasGrounded && v.y > jumpDetectSpeed) Jump();
            wasGrounded = onGround;

            bool sliding = motor != null && motor.IsSliding;
            bool slamming = motor != null && motor.State == MoveState.Slamming;
            if (legsFollowMovement) UpdateLegYaw(flat, grounded || sliding, dt);

            float top = hasSprint ? sprintSpeed : runSpeed;
            float rate = speed > top ? Mathf.Min(speed / top, maxRunRate) : 1f;
            animator.SetFloat(SpeedId, speed < 0.05f ? 0f : speed);
            animator.SetFloat(RateId, rate * rateSign);
            animator.SetBool(GroundedId, grounded);
            if (hasAir) animator.SetFloat(VSpeedId, up, 0.06f, dt);
            if (hasMoves)
            {
                animator.SetBool(SlidingId, sliding);
                animator.SetBool(SlammingId, slamming);
            }
            UpdateSwing();

            if (faceMovement && flat.sqrMagnitude > 0.04f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
            }
        }

        /// <summary>
        /// Follows the swing clip by its own clock. Dig button let go soon after a swing: skip the wind-up and go
        /// straight to putting the pickaxe away. When the clip reaches the point where his hand is over his
        /// shoulder, the pickaxe goes back on his back.
        /// </summary>
        private void UpdateSwing()
        {
            if (tools != null) digHeld = tools.SwingHeld;
            if (!swinging) return;
            float t = Time.time - swingStart;
            if (!puttingAway && !digHeld && t >= 0.06f && t < swingCommit)
            {
                animator.CrossFadeInFixedTime(swingState, 0.1f, rightArmLayer, SwingPutAwayFrom);
                swingStart = Time.time - SwingPutAwayFrom;
                t = SwingPutAwayFrom;
                puttingAway = true;
                aimUntil = Time.time;
            }
            if (t >= stowAt * SwingClipLength && pickaxe != null && pickaxe.InHand) pickaxe.SetInHand(false);
            if (t >= SwingClipLength + 0.2f) swinging = false;
        }

        /// <summary>
        /// After the Animator has posed him: while he digs or throws, lean the upper body toward where the player
        /// looks (so the pickaxe lands where you aim, floor or roof), and turn it back to face that way if the legs
        /// have turned to run sideways.
        /// </summary>
        private void LateUpdate()
        {
            if (animator == null || spine == null || chest == null) return;
            bool aiming = Time.time < aimUntil;
            aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, (aiming ? aimInSpeed : aimOutSpeed) * Time.deltaTime);
            if (aimWeight <= 0f) return;
            Transform frame = transform.parent != null ? transform.parent : transform;
            float pitch = look != null ? Mathf.Clamp(look.Pitch, -50f, 70f) : 0f;
            // half on the spine, half on the chest (the chest also carries the spine's half)
            Quaternion turn = Quaternion.AngleAxis(-0.5f * legYaw * aimWeight, Vector3.up) *
                              Quaternion.AngleAxis(0.5f * aimLean * pitch * aimWeight, frame.right);
            spine.rotation = turn * spine.rotation;
            chest.rotation = turn * chest.rotation;
        }

        /// <summary>
        /// Player only: the camera (and the player object) face where you look, but your legs should run where you
        /// go. Strafing turns the body up to maxLegYaw toward the move; backpedalling turns it toward the
        /// opposite direction and plays the run backwards.
        /// </summary>
        private void UpdateLegYaw(Vector3 flatVel, bool onGround, float dt)
        {
            Transform parent = transform.parent;
            Vector3 fwd = parent != null ? parent.forward : Vector3.forward;
            fwd.y = 0f;
            float wantYaw = 0f;
            float wantSign = 1f;
            if (onGround && flatVel.sqrMagnitude > 1f && fwd.sqrMagnitude > 0.001f)
            {
                float a = Vector3.SignedAngle(fwd.normalized, flatVel.normalized, Vector3.up);
                if (Mathf.Abs(a) > 110f)
                {
                    wantSign = -1f;
                    a = a > 0f ? a - 180f : a + 180f;
                }
                wantYaw = Mathf.Clamp(a, -maxLegYaw, maxLegYaw);
            }
            legYaw = Mathf.MoveTowards(legYaw, wantYaw, legTurnSpeed * dt);
            rateSign = wantSign;
            transform.localRotation = Quaternion.Euler(0f, legYaw, 0f);
        }

        private bool Probe()
        {
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 0.45f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!Hits[i].collider.transform.IsChildOf(transform)) return true;
            }
            return false;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
