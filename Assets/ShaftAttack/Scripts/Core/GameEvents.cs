using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Game-wide events that lots of unrelated things care about. A bomb shouldn't need to
    /// know the camera exists in order to shake it.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>(position, radius). Camera shake, sound, later: damage and networking.</summary>
        public static event System.Action<Vector3, float> Explosion;

        public static void RaiseExplosion(Vector3 position, float radius)
        {
            if (Explosion != null) Explosion(position, radius);
        }

        /// <summary>Something with Health took damage: (what was hit, the hit, did it die). Hit markers, damage
        /// numbers, kill feed, later: networking.</summary>
        public static event System.Action<Health, DamageInfo, bool> Damaged;

        public static void RaiseDamaged(Health target, DamageInfo info, bool killed)
        {
            if (Damaged != null) Damaged(target, info, killed);
        }
    }
}
