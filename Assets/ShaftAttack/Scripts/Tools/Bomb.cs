using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// A thrown bomb. Detonates on the first thing it touches (or after its fuse runs out),
    /// carves a crater, and launches anything nearby - including whoever threw it.
    ///
    /// The visual can be any prefab. Whatever it's missing is fixed up at spawn: collider,
    /// Rigidbody, convex mesh colliders, continuous collision detection. On top of that it
    /// sweeps its own path every physics step, so even a badly set up model can't fall
    /// through the floor without going off.
    /// </summary>
    public class Bomb : MonoBehaviour
    {
        [Tooltip("Seconds before it goes off on its own if it somehow never touches anything.")]
        public float maxFuse = 4f;

        private static readonly Collider[] OverlapBuffer = new Collider[64];
        private static readonly RaycastHit[] SweepBuffer = new RaycastHit[16];
        private static bool warnedAboutCollider;

        private MinerTools owner;
        private Rigidbody body;
        private Collider ignoreCollider;
        private float sweepRadius = 0.15f;
        private Vector3 lastPosition;
        private float fuse;
        private bool exploded;

        public static Bomb Spawn(Vector3 position, Vector3 velocity, MinerTools owner, Collider ignore)
        {
            GameObject go;
            bool custom = owner != null && owner.bombPrefab != null;
            float spin = owner != null ? owner.bombSpin : 0f;
            Vector3 spinAxis = SpinAxis(owner, velocity);

            if (custom)
            {
                // A spinning bomb starts upright and facing the throw, so the roll always reads the
                // same way: the top goes over and away from you. No spin = random angle, as before.
                Quaternion start = spin > 0f
                    ? Quaternion.LookRotation(Vector3.Cross(spinAxis, Vector3.up), Vector3.up) * owner.bombPrefab.transform.rotation
                    : Random.rotation;
                go = Instantiate(owner.bombPrefab, position, start);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = position;
                go.transform.localScale = Vector3.one * (owner != null ? owner.placeholderBombScale : 0.35f);
                if (owner != null && owner.bombMaterial != null)
                    go.GetComponent<MeshRenderer>().sharedMaterial = owner.bombMaterial;
            }

            go.name = "Bomb";

            float radius = SetUpCollision(go);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = go.AddComponent<Rigidbody>();
                rb.mass = 2f;
            }

            // Enforced whether the prefab brought its own Rigidbody or not: a small fast object
            // needs continuous detection or it will punch straight through the terrain mesh.
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;

            // Forward roll: spin about the thrower's right-hand side, which carries the top of the
            // bomb away from them. No angular damping, so the spin holds all the way to impact.
            if (spin > 0f)
            {
                float radiansPerSecond = spin * 2f * Mathf.PI;
                rb.maxAngularVelocity = Mathf.Max(rb.maxAngularVelocity, radiansPerSecond * 1.5f);
                rb.angularDamping = 0f;
                rb.angularVelocity = spinAxis * radiansPerSecond;
            }

            if (ignore != null)
            {
                Collider[] mine = go.GetComponentsInChildren<Collider>();
                for (int i = 0; i < mine.Length; i++) Physics.IgnoreCollision(mine[i], ignore);
            }

            Bomb bomb = go.GetComponent<Bomb>();
            if (bomb == null) bomb = go.AddComponent<Bomb>();
            bomb.owner = owner;
            bomb.body = rb;
            bomb.ignoreCollider = ignore;
            bomb.sweepRadius = radius;
            bomb.lastPosition = position;
            bomb.fuse = bomb.maxFuse;
            return bomb;
        }

        /// <summary>
        /// The axis a forward roll turns about: the thrower's right-hand side, kept level. A positive
        /// spin about it carries the top of the bomb forward, away from them.
        /// </summary>
        private static Vector3 SpinAxis(MinerTools owner, Vector3 velocity)
        {
            if (owner != null && owner.aim != null)
            {
                Vector3 right = owner.aim.right;
                right.y = 0f;
                if (right.sqrMagnitude > 0.0001f) return right.normalized;
            }

            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 0.0001f) return Vector3.Cross(Vector3.up, flat.normalized);
            return Vector3.right;
        }

        /// <summary>
        /// Makes sure the bomb actually has a usable collider, whatever the art came with.
        /// Returns a radius to sweep with.
        /// </summary>
        private static float SetUpCollision(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>();

            if (colliders.Length == 0)
            {
                // No collider anywhere - size a sphere from what the model actually looks like.
                SphereCollider sphere = go.AddComponent<SphereCollider>();
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                    Vector3 s = go.transform.lossyScale;
                    float scale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))));
                    float worldRadius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));

                    sphere.radius = Mathf.Max(0.02f, worldRadius / scale);
                    sphere.center = go.transform.InverseTransformPoint(bounds.center);
                }
                else
                {
                    sphere.radius = 0.2f;
                }

                if (!warnedAboutCollider)
                {
                    warnedAboutCollider = true;
                    Debug.LogWarning("[Bomb] The bomb prefab had no collider, so one was added automatically. " +
                                     "Adding a Sphere Collider to the prefab yourself will fit it better.");
                }

                colliders = go.GetComponentsInChildren<Collider>();
            }

            float biggest = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];

                // A trigger never raises OnCollisionEnter, so a trigger-only bomb would fall forever.
                c.isTrigger = false;

                // Physics requires convex mesh colliders on moving objects.
                MeshCollider mc = c as MeshCollider;
                if (mc != null && !mc.convex) mc.convex = true;

                Vector3 e = c.bounds.extents;
                biggest = Mathf.Max(biggest, Mathf.Max(e.x, Mathf.Max(e.y, e.z)));
            }

            return Mathf.Clamp(biggest, 0.05f, 1f);
        }

        private void Update()
        {
            fuse -= Time.deltaTime;
            if (fuse <= 0f) Explode(transform.position);
        }

        private void FixedUpdate()
        {
            if (exploded || body == null) return;

            // Safety net: check the line the bomb actually travelled this step. Catches anything
            // the normal collision pass misses - bad colliders, extreme speed, thin geometry.
            Vector3 position = body.position;
            Vector3 delta = position - lastPosition;
            float distance = delta.magnitude;

            if (distance > 0.0001f)
            {
                Vector3 dir = delta / distance;
                int count = Physics.SphereCastNonAlloc(lastPosition, sweepRadius, dir, SweepBuffer,
                                                       distance, ~0, QueryTriggerInteraction.Ignore);

                float nearest = float.MaxValue;
                Vector3 point = Vector3.zero;
                bool found = false;

                for (int i = 0; i < count; i++)
                {
                    Collider c = SweepBuffer[i].collider;
                    if (c == null) continue;
                    if (c == ignoreCollider) continue;
                    if (c.transform.IsChildOf(transform)) continue;   // our own collider

                    if (SweepBuffer[i].distance < nearest)
                    {
                        nearest = SweepBuffer[i].distance;
                        // distance 0 means the cast started already overlapping; use the bomb's own spot.
                        point = SweepBuffer[i].distance > 0.0001f ? SweepBuffer[i].point : lastPosition;
                        found = true;
                    }
                }

                if (found)
                {
                    Explode(point);
                    return;
                }
            }

            lastPosition = position;
        }

        private void OnCollisionEnter(Collision collision)
        {
            Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Explode(at);
        }

        private void Explode(Vector3 at)
        {
            if (exploded) return;
            exploded = true;
            Detonate(at, owner);
            Destroy(gameObject);
        }

        /// <summary>The blast itself, usable without a physical bomb (point-blank throws).</summary>
        public static void Detonate(Vector3 at, MinerTools owner)
        {
            float digRadius = owner != null ? owner.bombDigRadius : 3.6f;
            float knockback = owner != null ? owner.bombKnockback : 28f;
            float knockRadius = owner != null ? owner.bombKnockbackRadius : 7f;
            float upBias = owner != null ? owner.bombUpwardBias : 0.4f;

            // 1. Carve the crater (this also clears any rock the blast leaves floating).
            if (SmoothTerrain.Instance != null) SmoothTerrain.Instance.Dig(at, digRadius);

            // 2. Launch players. Full strength through the inner third of the blast, then fading,
            //    with an upward bias so a bomb at your feet sends you UP rather than sideways.
            for (int i = 0; i < PlayerMotor.All.Count; i++)
            {
                PlayerMotor m = PlayerMotor.All[i];
                Vector3 delta = m.BodyCenter - at;
                float dist = delta.magnitude;
                if (dist > knockRadius) continue;

                float strength = knockback * Mathf.Clamp01((1f - dist / knockRadius) * 1.5f);
                Vector3 dir = dist > 0.01f ? delta / dist : Vector3.up;
                dir = (dir + Vector3.up * upBias).normalized;
                m.AddImpulse(dir * strength);
            }

            // 3. Shove loose physics objects (other bombs, debris later). No allocation per blast.
            int count = Physics.OverlapSphereNonAlloc(at, knockRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb = OverlapBuffer[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddExplosionForce(knockback, at, knockRadius, 0.4f, ForceMode.VelocityChange);
            }

            // 4. Tell everyone else (camera shake, sound, later: damage, networking).
            GameEvents.RaiseExplosion(at, digRadius);
            ExplosionFX.Spawn(at, digRadius, owner);
        }
    }
}
