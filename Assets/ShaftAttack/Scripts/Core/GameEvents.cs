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
    }
}
