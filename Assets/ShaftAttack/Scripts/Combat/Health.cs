using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Hit points. Put it on anything that can be shot (players, the test dummy). Bullets find it on the collider they
    /// hit or on a parent of it. Damage is kept simple for now: every hit takes `amount` off, at 0 it dies and stays
    /// dead until ResetHealth. (Damage rules come later: headshots, armour, falloff...)
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;

        public float Current { get; private set; }
        public bool IsDead { get { return Current <= 0f; } }
        /// <summary>0..1</summary>
        public float Fraction { get { return maxHealth > 0f ? Current / maxHealth : 0f; } }

        public System.Action<DamageInfo> OnDamaged;
        public System.Action<DamageInfo> OnDied;
        public System.Action OnReset;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || info.amount <= 0f) return;
            Current = Mathf.Max(0f, Current - info.amount);
            bool killed = IsDead;
            if (OnDamaged != null) OnDamaged(info);
            GameEvents.RaiseDamaged(this, info, killed);
            if (killed && OnDied != null) OnDied(info);
        }

        /// <summary>Back to full health (respawn).</summary>
        public void ResetHealth()
        {
            Current = maxHealth;
            if (OnReset != null) OnReset();
        }
    }
}
