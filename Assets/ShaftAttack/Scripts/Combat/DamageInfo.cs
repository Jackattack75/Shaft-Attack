using UnityEngine;

namespace ShaftAttack
{
    /// <summary>One hit: how much, where, which way it was going, who did it and with what.</summary>
    public struct DamageInfo
    {
        public float amount;
        /// <summary>Where it hit (world).</summary>
        public Vector3 point;
        /// <summary>The surface normal at the hit.</summary>
        public Vector3 normal;
        /// <summary>The way the bullet (or blast) was travelling.</summary>
        public Vector3 direction;
        /// <summary>Who fired it (the player object). Can be null.</summary>
        public GameObject attacker;
        /// <summary>The gun's name, for kill feeds later.</summary>
        public string weapon;

        public DamageInfo(float amount, Vector3 point, Vector3 normal, Vector3 direction, GameObject attacker, string weapon)
        {
            this.amount = amount;
            this.point = point;
            this.normal = normal;
            this.direction = direction;
            this.attacker = attacker;
            this.weapon = weapon;
        }
    }

    /// <summary>Anything bullets can hurt. Health is the usual one; a bullet looks for this on whatever collider it hits
    /// (or the collider's parents).</summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
    }
}
