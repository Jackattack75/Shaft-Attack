using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Sells the speed and the impacts. Movement code decides how fast you ARE; this decides
    /// how fast you FEEL, and in a game like this that is at least half the job.
    ///
    /// Lives on the Camera itself. PlayerLook owns the pivot's rotation and PlayerMotor owns
    /// the pivot's position, so this can use the camera's own local transform freely.
    /// </summary>
    public class CameraFeel : MonoBehaviour
    {
        [Header("Refs")]
        public PlayerMotor motor;
        public MinerTools tools;
        public Camera cam;

        [Header("Field of view")]
        public float baseFov = 80f;
        public float maxFov = 108f;
        [Tooltip("Speed at which FOV is fully widened.")]
        public float fovMaxSpeed = 24f;
        public float fovLerpSpeed = 7f;

        [Header("Punches")]
        public float dashFovPunch = 10f;
        public float wallJumpFovPunch = 6f;
        public float punchDecay = 6f;

        [Header("Lean")]
        [Tooltip("Degrees of roll when strafing. Subtle - this is seasoning, not the meal.")]
        public float strafeRoll = 2.2f;
        public float wallRoll = 6f;
        public float rollLerpSpeed = 8f;

        [Header("Impacts")]
        public float slamDip = 0.28f;
        public float landDip = 0.06f;
        public float dipRecovery = 7f;

        [Header("Pickaxe kick")]
        [Tooltip("Degrees the view nods down on each pickaxe hit.")]
        public float pickaxeKick = 2.2f;
        public float kickRecovery = 16f;

        [Header("Explosion shake")]
        public float maxShakeAngle = 3.5f;
        public float maxShakeOffset = 0.12f;
        [Tooltip("How fast shake fades, per second.")]
        public float traumaDecay = 1.8f;
        [Tooltip("Explosions shake the camera out to this many blast-radii away.")]
        public float shakeRange = 6f;

        private float fovPunch;
        private float roll;
        private float dip;
        private float kick;
        private float trauma;
        private bool wasGrounded = true;

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (tools == null) tools = GetComponentInParent<MinerTools>();
        }

        private void OnEnable()
        {
            if (motor != null)
            {
                motor.OnDashed += HandleDash;
                motor.OnWallJumped += HandleWallJump;
                motor.OnSlamImpact += HandleSlam;
            }
            if (tools != null) tools.OnPickaxeHit += HandlePickaxe;
            GameEvents.Explosion += HandleExplosion;
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnDashed -= HandleDash;
                motor.OnWallJumped -= HandleWallJump;
                motor.OnSlamImpact -= HandleSlam;
            }
            if (tools != null) tools.OnPickaxeHit -= HandlePickaxe;
            GameEvents.Explosion -= HandleExplosion;
        }

        private void HandleDash() { fovPunch += dashFovPunch; }
        private void HandleWallJump() { fovPunch += wallJumpFovPunch; }
        private void HandleSlam(Vector3 position) { dip += slamDip; }
        private void HandlePickaxe(Vector3 point) { kick = pickaxeKick; }

        private void HandleExplosion(Vector3 position, float radius)
        {
            float dist = Vector3.Distance(transform.position, position);
            float amount = Mathf.Clamp01(1f - dist / (radius * shakeRange));
            trauma = Mathf.Clamp01(trauma + amount * 0.9f);
        }

        private void LateUpdate()
        {
            if (motor == null || cam == null) return;

            float dt = Time.deltaTime;

            // Small dip on any landing, bigger one after a slam.
            if (motor.IsGrounded && !wasGrounded) dip += landDip;
            wasGrounded = motor.IsGrounded;

            // --- field of view tracks speed, so going fast LOOKS like going fast ---
            float t = Mathf.Clamp01(Mathf.InverseLerp(motor.runSpeed, fovMaxSpeed, motor.Speed));
            float targetFov = Mathf.Lerp(baseFov, maxFov, t) + fovPunch;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovLerpSpeed * dt);
            fovPunch = Mathf.MoveTowards(fovPunch, 0f, punchDecay * dt * Mathf.Max(1f, fovPunch));

            // --- lean into strafes, and lean harder off a wall ---
            float targetRoll = -GameInput.Move.x * strafeRoll;
            if (!motor.IsGrounded && motor.TouchingWall)
            {
                float side = Vector3.Dot(transform.right, motor.LastWallNormal);
                targetRoll += side * wallRoll;
            }
            if (motor.IsSliding) targetRoll *= 2f;
            roll = Mathf.Lerp(roll, targetRoll, rollLerpSpeed * dt);

            // --- impact dip, pickaxe kick ---
            dip = Mathf.MoveTowards(dip, 0f, dipRecovery * dt * Mathf.Max(0.5f, dip * 4f));
            kick = Mathf.MoveTowards(kick, 0f, kickRecovery * dt);

            // --- explosion shake: smooth noise, scaled by trauma squared so small blasts stay subtle ---
            trauma = Mathf.MoveTowards(trauma, 0f, traumaDecay * dt);
            float s = trauma * trauma;
            float n = Time.time * 28f;
            float sx = (Mathf.PerlinNoise(n, 0.1f) - 0.5f) * 2f;
            float sy = (Mathf.PerlinNoise(0.2f, n) - 0.5f) * 2f;
            float sz = (Mathf.PerlinNoise(n, n) - 0.5f) * 2f;

            transform.localRotation = Quaternion.Euler(
                kick + sx * maxShakeAngle * s,
                sy * maxShakeAngle * s,
                roll + sz * maxShakeAngle * s);

            transform.localPosition = new Vector3(
                sy * maxShakeOffset * s,
                -dip + sx * maxShakeOffset * s,
                0f);
        }
    }
}
