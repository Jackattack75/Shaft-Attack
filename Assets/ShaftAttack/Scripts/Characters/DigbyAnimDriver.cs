using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Feeds Digby's Animator (Digby.controller) from how he actually moves:
    ///   Speed     ground speed in m/s (blend tree: Idle 0, Walk 0.5, Run 3.375, Sprint 10)
    ///   MoveRate  playback speed of the locomotion; faster than his fastest clip it plays faster,
    ///             and negative (backwards) when the legs face away from where he's going
    ///   Grounded  false after a short moment off the ground
    ///   Jump      fired by PlayerMotor's jump events, or when he shoots upward off the ground
    ///   Sliding / Slamming / Dash / SlamLand   the player's moves, straight from PlayerMotor (if the controller has them)
    /// Works for anything that moves him: the player (PlayerMotor), a bot, or a networked copy
    /// (then it just watches the transform). Sits on the prefab root (DigbyBody on the player).
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

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int RateId = Animator.StringToHash("MoveRate");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int SlidingId = Animator.StringToHash("Sliding");
        private static readonly int SlammingId = Animator.StringToHash("Slamming");
        private static readonly int DashId = Animator.StringToHash("Dash");
        private static readonly int SlamLandId = Animator.StringToHash("SlamLand");
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private Animator animator;
        private PlayerMotor motor;
        private CharacterController controller;
        private Vector3 lastPos;
        private float speed;
        private float airTime;
        private bool wasGrounded = true;
        private bool hasMoves;
        private bool hasSprint;
        private float legYaw;
        private float rateSign = 1f;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            motor = GetComponentInParent<PlayerMotor>();
            controller = GetComponentInParent<CharacterController>();
            lastPos = transform.position;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                    if (p.nameHash == SlidingId) hasMoves = true;
                foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
                    if (c != null && c.name == "Sprint") hasSprint = true;
            }
        }

        private void OnEnable()
        {
            lastPos = transform.position;
            if (motor != null)
            {
                motor.OnJumped += Jump;
                motor.OnWallJumped += Jump;
                motor.OnDashed += Dashed;
                motor.OnSlamImpact += SlamLanded;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnJumped -= Jump;
                motor.OnWallJumped -= Jump;
                motor.OnDashed -= Dashed;
                motor.OnSlamImpact -= SlamLanded;
            }
        }

        /// <summary>Play the take-off. Call this from bots / netcode when they jump.</summary>
        public void Jump()
        {
            if (animator != null) animator.SetTrigger(JumpId);
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
            if (motor != null)
            {
                Vector3 mv = motor.Velocity;
                flat = new Vector3(mv.x, 0f, mv.z);
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
            if (hasMoves)
            {
                animator.SetBool(SlidingId, sliding);
                animator.SetBool(SlammingId, slamming);
            }

            if (faceMovement && flat.sqrMagnitude > 0.04f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
            }
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
    }
}
