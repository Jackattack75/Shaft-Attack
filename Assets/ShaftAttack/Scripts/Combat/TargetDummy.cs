using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Test target: something to shoot. Gives the object a capsule to hit (if it has no collider), flinches away from
    /// each hit, tips over backwards when its Health runs out and gets back up with full health a few seconds later.
    /// Put it on a Digby in the test scene (Shaft Attack > Make Test Target does it for you).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour
    {
        [Tooltip("Seconds lying on the ground before getting back up.")]
        public float respawnDelay = 3f;
        [Tooltip("How far a hit rocks him (degrees).")]
        public float flinch = 7f;
        [Tooltip("Capsule made for him if he has no collider: height and radius in metres.")]
        public float height = 1.75f;
        public float radius = 0.45f;

        private Health health;
        private Transform model;
        private Quaternion modelRest;
        private Vector3 tilt;           // flinch: axis * degrees, springs back
        private Vector3 tiltVel;
        private float fall;             // 0 standing .. 1 flat on his back
        private float deadAt = -1f;
        private Vector3 fallAxis = Vector3.right;
        private Animator animator;

        private void Awake()
        {
            health = GetComponent<Health>();
            if (GetComponentInChildren<Collider>() == null)
            {
                CapsuleCollider c = gameObject.AddComponent<CapsuleCollider>();
                c.height = height;
                c.radius = radius;
                c.center = new Vector3(0f, height * 0.5f, 0f);
            }
            animator = GetComponentInChildren<Animator>();
            model = animator != null ? animator.transform : (transform.childCount > 0 ? transform.GetChild(0) : null);
            if (model != null) modelRest = model.localRotation;
        }

        private void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            health.OnDamaged += Hit;
            health.OnDied += Died;
        }

        private void OnDisable()
        {
            health.OnDamaged -= Hit;
            health.OnDied -= Died;
        }

        private void Hit(DamageInfo info)
        {
            Vector3 dir = Vector3.ProjectOnPlane(info.direction, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f) return;
            // rock away from the shot: about the horizontal axis across the shot
            Vector3 axis = Vector3.Cross(Vector3.up, dir.normalized);
            tiltVel += transform.InverseTransformDirection(axis) * flinch * 18f;
        }

        private void Died(DamageInfo info)
        {
            deadAt = Time.time;
            Vector3 dir = Vector3.ProjectOnPlane(info.direction, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            fallAxis = transform.InverseTransformDirection(Vector3.Cross(Vector3.up, dir.normalized));
            if (animator != null) animator.speed = 0.3f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            // flinch spring
            tiltVel += (-tilt * 160f - tiltVel * 14f) * dt;
            tilt += tiltVel * dt;
            // falling over / getting up
            if (health.IsDead)
            {
                fall = Mathf.MoveTowards(fall, 1f, dt / 0.45f);
                if (Time.time - deadAt >= respawnDelay)
                {
                    health.ResetHealth();
                    if (animator != null) animator.speed = 1f;
                }
            }
            else
            {
                fall = Mathf.MoveTowards(fall, 0f, dt / 0.6f);
            }
            if (model == null) return;
            float f = fall * fall * (3f - 2f * fall);
            Quaternion q = Quaternion.AngleAxis(tilt.magnitude, tilt.sqrMagnitude > 1e-6f ? tilt.normalized : Vector3.right) *
                           Quaternion.AngleAxis(84f * f, fallAxis);
            model.localRotation = q * modelRest;
        }
    }
}
