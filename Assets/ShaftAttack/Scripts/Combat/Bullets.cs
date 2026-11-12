using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>One shot, as PlayerGun hands it over.</summary>
    public struct BulletShot
    {
        /// <summary>Where the bullet really starts (the camera, so it goes exactly where you aim).</summary>
        public Vector3 origin;
        public Vector3 direction;
        /// <summary>Where the tracer starts (the gun's muzzle, as you see it).</summary>
        public Vector3 visualStart;
        public float speed;
        public float range;
        public float damage;
        public float digRadius;
        public float digInset;
        public Color tracerColor;
        public float tracerWidth;
        public float tracerLength;
        public GameObject owner;
        public int mask;
        public string weapon;
    }

    /// <summary>
    /// Bullets are effects, not objects: each one is a few numbers in a list, moved every frame at bulletSpeed with a
    /// raycast over the bit of path it covers that frame, so it hits whatever is there when it ARRIVES (a fast bullet,
    /// not an instant laser). What you see is a tracer streak (a pooled LineRenderer) flying from the muzzle; it ends
    /// exactly where the bullet hits. On a hit: damage (anything with IDamageable, e.g. Health), a small bite out of the
    /// rock (SmoothTerrain.Dig) and a puff of dust and chips in the rock's colour (ImpactFX).
    /// Made by itself the first time a gun fires.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class Bullets : MonoBehaviour
    {
        /// <summary>Set by PlayerGun from its inspector (so the shader ends up in builds). Placeholder if empty.</summary>
        public static Material TracerMaterial;
        /// <summary>Every bullet that hits something: (hit, shot). Sounds later.</summary>
        public static System.Action<RaycastHit, BulletShot> OnImpact;

        private const float FadeTime = 0.05f;

        private class Bullet
        {
            public BulletShot shot;
            public float traveled;
            public float predicted;        // distance to what was in the way when it was fired
            public Vector3 visualEnd;
            public bool done;
            public float doneAt;
            public LineRenderer line;
        }

        private static Bullets instance;
        private readonly List<Bullet> live = new List<Bullet>();
        private readonly Stack<LineRenderer> pool = new Stack<LineRenderer>();
        private static readonly Gradient Fade = new Gradient();
        private static Material fallback;

        public static int LiveCount { get { return instance != null ? instance.live.Count : 0; } }

        public static void Fire(BulletShot shot)
        {
            if (instance == null)
            {
                GameObject go = new GameObject("Bullets (effects)");
                instance = go.AddComponent<Bullets>();
            }
            instance.Spawn(shot);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Spawn(BulletShot shot)
        {
            Bullet b = new Bullet();
            shot.direction = shot.direction.normalized;
            b.shot = shot;
            RaycastHit hit;
            b.predicted = Physics.Raycast(shot.origin, shot.direction, out hit, shot.range, shot.mask, QueryTriggerInteraction.Ignore)
                ? hit.distance : shot.range;
            b.visualEnd = shot.origin + shot.direction * b.predicted;
            b.line = GetLine();
            b.line.startWidth = shot.tracerWidth;
            b.line.endWidth = shot.tracerWidth;
            live.Add(b);
            Step(b, Time.deltaTime * 0.5f);          // already on its way in the frame it was fired
            Draw(b);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Bullet b = live[i];
                if (!b.done) Step(b, dt);
                if (b.done && Time.time - b.doneAt >= FadeTime)
                {
                    Release(b.line);
                    live.RemoveAt(i);
                    continue;
                }
                Draw(b);
            }
        }

        private void Step(Bullet b, float dt)
        {
            if (b.done) return;
            BulletShot s = b.shot;
            float len = Mathf.Min(s.speed * dt, s.range - b.traveled);
            Vector3 from = s.origin + s.direction * b.traveled;
            RaycastHit hit;
            if (len > 0f && Physics.Raycast(from, s.direction, out hit, len, s.mask, QueryTriggerInteraction.Ignore))
            {
                b.traveled += hit.distance;
                b.done = true;
                b.doneAt = Time.time;
                Impact(s, hit);
                return;
            }
            b.traveled += Mathf.Max(len, 0f);
            if (b.traveled >= s.range - 0.001f)
            {
                b.done = true;
                b.doneAt = Time.time;
            }
        }

        /// <summary>The streak: its head runs from the muzzle to where the bullet will end up, in step with the bullet.</summary>
        private void Draw(Bullet b)
        {
            BulletShot s = b.shot;
            Vector3 head = b.traveled <= b.predicted
                ? Vector3.Lerp(s.visualStart, b.visualEnd, b.predicted > 0.001f ? b.traveled / b.predicted : 1f)
                : b.visualEnd + s.direction * (b.traveled - b.predicted);
            Vector3 along = head - s.visualStart;
            float fromStart = along.magnitude;
            Vector3 dir = fromStart > 0.0001f ? along / fromStart : s.direction;
            float a = 1f;
            float length = Mathf.Min(s.tracerLength, fromStart);
            if (b.done)
            {
                a = 1f - Mathf.Clamp01((Time.time - b.doneAt) / FadeTime);
                length *= a;                                  // the tail catches up with the head
            }
            b.line.SetPosition(0, head - dir * length);
            b.line.SetPosition(1, head);
            Color c = s.tracerColor;
            Fade.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f * a, 0.6f), new GradientAlphaKey(a, 1f) });
            b.line.colorGradient = Fade;
        }

        private void Impact(BulletShot s, RaycastHit hit)
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                target.TakeDamage(new DamageInfo(s.damage, hit.point, hit.normal, s.direction, s.owner, s.weapon));
                ImpactFX.Hit(hit.point, hit.normal, s.direction);
            }
            else
            {
                Color rock = new Color(0.45f, 0.43f, 0.42f);
                SmoothTerrain terrain = SmoothTerrain.Instance;
                if (terrain != null && hit.collider.GetComponent<TerrainChunk>() != null)
                {
                    rock = terrain.ColorAt(hit.point - hit.normal * 0.1f);
                    if (s.digRadius > 0f) terrain.Dig(hit.point + s.direction * s.digInset, s.digRadius);
                }
                ImpactFX.Rock(hit.point, hit.normal, s.direction, rock);
            }
            if (hit.rigidbody != null) hit.rigidbody.AddForceAtPosition(s.direction * s.damage * 0.08f, hit.point, ForceMode.Impulse);
            if (OnImpact != null) OnImpact(hit, s);
        }

        private LineRenderer GetLine()
        {
            LineRenderer lr;
            if (pool.Count > 0)
            {
                lr = pool.Pop();
                lr.enabled = true;
                return lr;
            }
            GameObject go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lr.allowOcclusionWhenDynamic = false;
            lr.sharedMaterial = TracerMaterial != null ? TracerMaterial : Fallback();
            return lr;
        }

        private void Release(LineRenderer lr)
        {
            lr.enabled = false;
            pool.Push(lr);
        }

        private static Material Fallback()
        {
            if (fallback != null) return fallback;
            Shader s = Shader.Find("ShaftAttack/Tracer");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            fallback = new Material(s);
            return fallback;
        }
    }
}
