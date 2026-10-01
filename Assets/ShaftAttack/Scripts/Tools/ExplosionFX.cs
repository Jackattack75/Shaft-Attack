using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Spawns the blast effect.
    ///
    /// If MinerTools.explosionPrefab is set, that prefab is used, scaled so it matches the
    /// actual crater size, and cleaned up after explosionLifetime seconds.
    /// Otherwise you get the placeholder: a glowing ball that pops and collapses, plus a flash.
    /// </summary>
    public class ExplosionFX : MonoBehaviour
    {
        private const float Life = 0.32f;
        private const float PeakAt = 0.3f;   // fraction of Life when the ball is biggest

        private Transform ball;
        private Light flash;
        private float maxScale;
        private float age;

        public static void Spawn(Vector3 position, float radius, MinerTools owner)
        {
            if (owner != null && owner.explosionPrefab != null)
            {
                GameObject fx = Instantiate(owner.explosionPrefab, position, Quaternion.identity);

                if (owner.scaleExplosionToBlast && owner.explosionPrefabRadius > 0.01f)
                    fx.transform.localScale *= radius / owner.explosionPrefabRadius;

                Destroy(fx, Mathf.Max(0.1f, owner.explosionLifetime));
                return;
            }

            SpawnPlaceholder(position, radius, owner != null ? owner.explosionMaterial : null);
        }

        private static void SpawnPlaceholder(Vector3 position, float radius, Material material)
        {
            GameObject root = new GameObject("Explosion");
            root.transform.position = position;

            GameObject ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            // Remove the collider immediately - otherwise it could shove the player this frame.
            DestroyImmediate(ballGo.GetComponent<Collider>());
            ballGo.transform.SetParent(root.transform, false);
            ballGo.transform.localScale = Vector3.zero;
            MeshRenderer mr = ballGo.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) mr.sharedMaterial = material;

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = radius * 4f;
            light.intensity = 12f;

            ExplosionFX fx = root.AddComponent<ExplosionFX>();
            fx.ball = ballGo.transform;
            fx.flash = light;
            fx.maxScale = radius * 2f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = age / Life;

            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float scale = k < PeakAt
                ? Mathf.Lerp(0.2f, maxScale, k / PeakAt)
                : Mathf.Lerp(maxScale, 0f, (k - PeakAt) / (1f - PeakAt));

            ball.localScale = Vector3.one * scale;
            flash.intensity = Mathf.Lerp(12f, 0f, k);
        }
    }
}
