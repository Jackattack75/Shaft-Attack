using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Feeds Digby's Animator (Digby.controller) from how he actually moves:
    ///   Speed     ground speed in m/s (blend tree: Idle 0, Walk 0.5, Run 1.82)
    ///   MoveRate  playback speed of the walk/run; above his natural run pace the run plays faster
    ///   Grounded  false after a short moment off the ground
    ///   Jump      fired by PlayerMotor's jump events, or when he shoots upward off the ground
    /// Works for anything that moves him: the player (PlayerMotor), a bot, or a networked copy
    /// (then it just watches the transform). Sits on the prefab root.
    /// </summary>
    [DisallowMultipleComponent]
    public class DigbyAnimDriver : MonoBehaviour
    {
        [Tooltip("Ground speed of the Walk clip in m/s (from the Blender export).")]
        public float walkSpeed = 0.5f;

        [Tooltip("Ground speed of the Run clip in m/s. Faster than this, the run plays faster.")]
        public float runSpeed = 1.82f;

        [Tooltip("Cap on how much faster than normal the run is allowed to play.")]
        public float maxRunRate = 1.8f;

        [Tooltip("Upward speed (m/s) that counts as a jump when no PlayerMotor tells us directly.")]
        public float jumpDetectSpeed = 2.5f;

        [Tooltip("Seconds off the ground before he counts as airborne (stops tiny bumps triggering the fall pose).")]
        public float airGrace = 0.12f;

        [Tooltip("Turn him to face the way he is moving (for bots). Players face where they look instead.")]
        public bool faceMovement = false;
        public float turnSpeed = 540f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int RateId = Animator.StringToHash("MoveRate");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private Animator animator;
        private PlayerMotor motor;
        private CharacterController controller;
        private Vector3 lastPos;
        private float speed;
        private float airTime;
        private bool wasGrounded = true;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            motor = GetComponentInParent<PlayerMotor>();
            controller = GetComponentInParent<CharacterController>();
            lastPos = transform.position;
        }

        private void OnEnable()
        {
            lastPos = transform.position;
            if (motor != null)
            {
                motor.OnJumped += Jump;
                motor.OnWallJumped += Jump;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnJumped -= Jump;
                motor.OnWallJumped -= Jump;
            }
        }

        /// <summary>Play the take-off. Call this from bots / netcode when they jump.</summary>
        public void Jump()
        {
            if (animator != null) animator.SetTrigger(JumpId);
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
            float target = motor != null ? motor.Speed : flat.magnitude;
            speed = Mathf.Lerp(speed, target, 1f - Mathf.Exp(-14f * dt));

            bool onGround = motor != null ? motor.IsGrounded : (controller != null ? controller.isGrounded : Probe());
            airTime = onGround ? 0f : airTime + dt;
            bool grounded = airTime < airGrace;
            if (motor == null && wasGrounded && v.y > jumpDetectSpeed) Jump();
            wasGrounded = onGround;

            animator.SetFloat(SpeedId, speed < 0.05f ? 0f : speed);
            animator.SetFloat(RateId, speed > runSpeed ? Mathf.Min(speed / runSpeed, maxRunRate) : 1f);
            animator.SetBool(GroundedId, grounded);

            if (faceMovement && flat.sqrMagnitude > 0.04f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
            }
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
