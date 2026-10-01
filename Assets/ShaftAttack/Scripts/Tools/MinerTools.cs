using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// The miner's digging kit.
    ///
    ///  Pickaxe (hold left mouse): fast, wide, close-range bites. Your tunnelling tool.
    ///  Bombs (right mouse or Q): big craters, slow to refill, and they launch you hard if
    ///  you're close - throw one at your feet and you've got a rocket jump.
    ///
    /// Art is swappable: drop your own bomb and explosion prefabs into the Art section and
    /// the placeholders are ignored. No code changes needed.
    ///
    /// Works without terrain too (bombs still launch you on the movement course).
    /// </summary>
    public class MinerTools : MonoBehaviour
    {
        [Header("Refs (found automatically if left empty)")]
        public Transform aim;
        public PlayerMotor motor;
        public PlayerLook look;

        [Header("Pickaxe (hold left mouse)")]
        public float pickaxeReach = 3.5f;
        [Tooltip("Radius of each bite. 1.25 = a 2.5 m wide hole per swing.")]
        public float pickaxeRadius = 1.25f;
        [Tooltip("Seconds between swings while held.")]
        public float swingInterval = 0.22f;
        [Tooltip("How far past the surface the bite is centred. Deeper = tunnels faster.")]
        public float digInset = 0.35f;

        [Header("Bombs (right mouse or Q)")]
        public int maxBombs = 2;
        [Tooltip("Seconds to refill one bomb. Long on purpose: bombs are a big play, not spam.")]
        public float bombRechargeTime = 4.5f;
        public float throwSpeed = 20f;
        [Range(0f, 1f)]
        [Tooltip("How much of your own movement the bomb inherits when thrown.")]
        public float inheritVelocity = 0.5f;
        [Tooltip("Crater radius. 3.6 = a 7.2 m wide room from one bomb.")]
        public float bombDigRadius = 3.6f;

        [Header("Bomb jump")]
        [Tooltip("THIS IS THE BOMB-JUMP DIAL. Launch speed at the centre of the blast, in m/s. " +
                 "Straight up, 28 throws you about 11.5 m high. Raise it for more air.")]
        public float bombKnockback = 28f;
        [Tooltip("How far the launch reaches. Full strength within the inner third, fading to nothing at the edge.")]
        public float bombKnockbackRadius = 7f;
        [Range(0f, 1.5f)]
        [Tooltip("How much the launch is bent upward. 0 = straight away from the blast, higher = more lift.")]
        public float bombUpwardBias = 0.4f;

        [Header("Art - drop your own prefabs here")]
        [Tooltip("Thrown bomb. A Rigidbody, collider and Bomb script are added automatically if it " +
                 "doesn't have them. Leave empty to use the placeholder red ball.")]
        public GameObject bombPrefab;
        [Tooltip("Explosion effect, usually a particle system. Leave empty for the placeholder flash.")]
        public GameObject explosionPrefab;
        [Tooltip("The blast size your explosion prefab was built for. The effect is scaled by " +
                 "bombDigRadius divided by this, so it always matches the crater.")]
        public float explosionPrefabRadius = 3.6f;
        public bool scaleExplosionToBlast = true;
        [Tooltip("Seconds before the explosion effect is cleaned up.")]
        public float explosionLifetime = 4f;

        [Header("Placeholder art (ignored once prefabs are set)")]
        public float placeholderBombScale = 0.35f;
        public Material bombMaterial;
        public Material explosionMaterial;

        public int Bombs { get { return bombs; } }
        public float BombRecharge { get { return bombs >= maxBombs ? 1f : bombTimer / bombRechargeTime; } }

        /// <summary>Fires on every pickaxe swing that connects. Camera kick, sound, particles.</summary>
        public System.Action<Vector3> OnPickaxeHit;
        public System.Action OnBombThrown;

        private int bombs;
        private float bombTimer;
        private float nextSwingTime;
        private CharacterController ownCollider;

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (look == null) look = GetComponent<PlayerLook>();
            if (aim == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                aim = cam != null ? cam.transform : transform;
            }
            ownCollider = GetComponent<CharacterController>();
            bombs = maxBombs;
        }

        private void Update()
        {
            RechargeBombs(Time.deltaTime);

            // While the cursor is free, clicks belong to the editor, not the game.
            if (look != null && !look.CursorLocked) return;

            if (GameInput.PrimaryHeld && Time.time >= nextSwingTime) Swing();
            if (GameInput.SecondaryPressedThisFrame) ThrowBomb();
        }

        // ------------------------------------------------------------------ pickaxe

        private void Swing()
        {
            nextSwingTime = Time.time + swingInterval;

            int mask = ~(1 << gameObject.layer);
            RaycastHit hit;
            if (!Physics.Raycast(aim.position, aim.forward, out hit, pickaxeReach, mask, QueryTriggerInteraction.Ignore))
                return;

            SmoothTerrain terrain = SmoothTerrain.Instance;
            if (terrain == null || hit.collider.GetComponent<TerrainChunk>() == null) return;

            Vector3 center = hit.point + aim.forward * digInset;
            terrain.Dig(center, pickaxeRadius);

            if (OnPickaxeHit != null) OnPickaxeHit(hit.point);
        }

        // ------------------------------------------------------------------ bombs

        private void RechargeBombs(float dt)
        {
            if (bombs >= maxBombs)
            {
                bombTimer = 0f;
                return;
            }

            bombTimer += dt;
            if (bombTimer >= bombRechargeTime)
            {
                bombTimer = 0f;
                bombs++;
            }
        }

        private void ThrowBomb()
        {
            if (bombs <= 0) return;
            bombs--;

            const float spawnDistance = 0.6f;
            Vector3 dir = aim.forward;

            // Point-blank at a wall or the floor? Detonate right there instead of spawning the
            // bomb inside the rock. This is what makes bomb-jumping off a wall reliable.
            int mask = ~(1 << gameObject.layer);
            RaycastHit hit;
            if (Physics.Raycast(aim.position, dir, out hit, spawnDistance, mask, QueryTriggerInteraction.Ignore))
            {
                Bomb.Detonate(hit.point, this);
                if (OnBombThrown != null) OnBombThrown();
                return;
            }

            Vector3 inherited = motor != null ? motor.Velocity * inheritVelocity : Vector3.zero;
            Bomb.Spawn(aim.position + dir * spawnDistance, dir * throwSpeed + inherited, this, ownCollider);
            if (OnBombThrown != null) OnBombThrown();
        }
    }
}
