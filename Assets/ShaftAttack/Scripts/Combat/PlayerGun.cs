using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Shoots the gun in your hand (Loadout). Left mouse: one shot per click, or hold for automatic guns.
    /// Every bullet goes exactly where the crosshair points (from the camera, plus a little spread) and flies as an
    /// effect (see Bullets): fast, but not instant. On rock it bites out a bit of ground, on anything with Health it
    /// does damage. The tracer starts at the gun's muzzle as you see it (first person or third).
    /// Sits on the Player next to Loadout.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Loadout))]
    public class PlayerGun : MonoBehaviour
    {
        [Header("Refs (found automatically if left empty)")]
        public Transform aim;
        public PlayerMotor motor;
        public PlayerLook look;
        public CameraFeel cameraFeel;

        [Header("Effects (placeholders are made if these are empty)")]
        [Tooltip("Tracer streaks. ShaftAttack/Tracer shader.")]
        public Material tracerMaterial;
        [Tooltip("Muzzle flash glow. ShaftAttack/Tracer shader with Radial on.")]
        public Material flashMaterial;
        public Material dustMaterial;
        public Material chipMaterial;
        public Material sparkMaterial;

        /// <summary>0..1: how far the trigger is pulled right now (finger and trigger animation).</summary>
        public float TriggerAmount { get; private set; }
        public float LastShotTime { get; private set; }
        public int ShotsFired { get; private set; }
        /// <summary>Current extra spread from firing fast (degrees).</summary>
        public float Bloom { get { return bloom; } }
        /// <summary>Fires on every shot: recoil, flash, animation, sound.</summary>
        public System.Action<GunDefinition> OnShot;

        private Loadout loadout;
        private Camera aimCamera;
        private Transform muzzle;
        private Camera muzzleCamera;
        private float nextShot;
        private float bloom;
        private float clickUntil = -1f;
        private int mask;

        private void Awake()
        {
            loadout = GetComponent<Loadout>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (look == null) look = GetComponent<PlayerLook>();
            if (aim == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                aim = cam != null ? cam.transform : transform;
            }
            aimCamera = aim.GetComponent<Camera>();
            if (cameraFeel == null) cameraFeel = aim.GetComponent<CameraFeel>();
            LastShotTime = -10f;
            mask = ~((1 << gameObject.layer) | (1 << 2));                     // not us, not Ignore Raycast
            int vm = LayerMask.NameToLayer("Viewmodel");
            if (vm >= 0) mask &= ~(1 << vm);
            if (tracerMaterial != null) Bullets.TracerMaterial = tracerMaterial;
            ImpactFX.SetMaterials(dustMaterial, chipMaterial, sparkMaterial);
        }

        /// <summary>Where the gun's muzzle is right now, as the player sees it. The first-person view sets it (with the
        /// camera that draws the gun); in third person the body's gun does. Null = a point in front of the camera.</summary>
        public void SetMuzzle(Transform m, Camera drawnBy)
        {
            muzzle = m;
            muzzleCamera = drawnBy;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            GunDefinition gun = loadout.CurrentGun;
            bloom = Mathf.MoveTowards(bloom, 0f, (gun != null ? gun.bloomRecovery : 8f) * dt);
            bool locked = look == null || look.CursorLocked;
            bool held = false;
            if (gun != null && locked)
            {
                if (GameInput.PrimaryPressedThisFrame) clickUntil = Time.time + 0.15f;   // a click just before it's ready still fires
                held = gun.automatic ? GameInput.PrimaryHeld : Time.time <= clickUntil;
                if (held && loadout.Ready && Time.time >= nextShot)
                {
                    Fire(gun);
                    clickUntil = -1f;
                }
            }
            // the finger: squeezes for each shot, stays in while an automatic is firing
            bool pulled = gun != null && (Time.time - LastShotTime < 0.06f ||
                                          (gun.automatic && locked && GameInput.PrimaryHeld && loadout.Ready));
            TriggerAmount = Mathf.MoveTowards(TriggerAmount, pulled ? 1f : 0f, dt / (pulled ? 0.035f : 0.09f));
        }

        private void Fire(GunDefinition gun)
        {
            nextShot = Time.time + 1f / Mathf.Max(0.1f, gun.fireRate);
            LastShotTime = Time.time;
            ShotsFired++;
            float spread = gun.spread + bloom;
            if (motor != null)
            {
                spread += gun.moveSpread * Mathf.Clamp01(motor.Speed / Mathf.Max(1f, motor.runSpeed));
                if (!motor.IsGrounded) spread += gun.airSpread;
            }
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
            Vector3 dir = (aim.forward + aim.right * r.x + aim.up * r.y).normalized;

            BulletShot s = new BulletShot();
            s.origin = aim.position;
            s.direction = dir;
            s.visualStart = MuzzlePoint();
            s.speed = gun.bulletSpeed;
            s.range = gun.range;
            s.damage = gun.damage;
            s.digRadius = gun.digRadius;
            s.digInset = gun.digInset;
            s.tracerColor = gun.tracerColor;
            s.tracerWidth = gun.tracerWidth;
            s.tracerLength = gun.tracerLength;
            s.owner = gameObject;
            s.mask = mask;
            s.weapon = gun.displayName;
            Bullets.Fire(s);

            bloom = Mathf.Min(gun.maxBloom, bloom + gun.bloomPerShot);
            if (look != null) look.AddRecoil(gun.aimClimb, Random.Range(-1f, 1f) * gun.aimWander);
            if (cameraFeel != null) cameraFeel.GunKick(gun.cameraKick);
            if (OnShot != null) OnShot(gun);
        }

        /// <summary>The muzzle as the player sees it. The first-person gun is drawn by its own camera (another field of
        /// view), so its muzzle is moved to the spot in the world that shows at the same place on screen.</summary>
        private Vector3 MuzzlePoint()
        {
            if (muzzle == null) return aim.position + aim.forward * 0.5f + aim.right * 0.15f - aim.up * 0.15f;
            if (muzzleCamera == null || aimCamera == null || !aimCamera.isActiveAndEnabled) return muzzle.position;
            Vector3 sp = muzzleCamera.WorldToScreenPoint(muzzle.position);
            if (sp.z <= 0.01f) return muzzle.position;
            return aimCamera.ScreenToWorldPoint(sp);
        }
    }
}
