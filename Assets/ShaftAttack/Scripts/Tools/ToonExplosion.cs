using System.Collections.Generic;
using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Cartoon explosion built on Unity's particle system, configured entirely from code.
    ///
    /// Four particle systems layered over a few hand-animated shapes:
    ///
    ///   FIRE    unlit sphere-mesh particles, bursting outward, cooling yellow -> orange -> red
    ///   SMOKE   lots of small unlit sphere-mesh puffs, near-black, unaffected by any light
    ///   DEBRIS  lit cube-mesh particles with gravity that bounce off the terrain
    ///   SPARKS  tiny fast particles with trails
    ///
    /// Plus a white-hot core flash, comic-book spikes, a shockwave ring and a ground dust wave.
    ///
    /// Nothing here has a collider. Particles never collide with the player, and every mesh is
    /// built by hand rather than from Unity primitives (which would come with colliders attached).
    ///
    /// FOUR GOTCHAS this code works around, all of which look like broken code but are defaults:
    ///  1. AddComponent&lt;ParticleSystem&gt; starts the system playing right away with Unity's
    ///     defaults, so a burst registered at time 0 is already in the past. Stopped and cleared first.
    ///  2. Enabling Limit Velocity Over Lifetime clamps particle speed to 1 unit/s by default,
    ///     which freezes the whole blast. Its limit is set explicitly.
    ///  3. A cone whose length is scaled to 0 still draws its base cap at full width. The spikes
    ///     collapse thickness and length together, or they leave a little spiky ball behind.
    ///  4. The rings are already flat, so squashing them on Y does nothing at all. They have to be
    ///     shrunk and switched off to actually go away.
    /// </summary>
    public class ToonExplosion : MonoBehaviour
    {
        [Header("Size and timing")]
        [Tooltip("Blast size this effect is built for. MinerTools scales the object to match the real crater.")]
        public float baseRadius = 3.6f;
        [Tooltip("Makes the VISUAL bigger than the crater without changing how much rock the bomb removes.")]
        public float blastScale = 1.35f;
        [Tooltip("Total length including smoke drifting away. The violent part is the first half second.")]
        public float duration = 2f;

        [Header("How much stuff")]
        [Range(0, 80)] public int fireParticles = 30;
        [Tooltip("Lots of small puffs read as a cloud. A few big ones read as balloons.")]
        [Range(0, 120)] public int smokeParticles = 45;
        [Range(0, 80)] public int debrisParticles = 26;
        [Range(0, 120)] public int sparkParticles = 34;
        [Range(0, 12)] public int spikeCount = 7;
        [Tooltip("Debris bounces off the terrain instead of passing through. Costs a little performance.")]
        public bool debrisBounces = true;

        [Header("Smoke")]
        [Tooltip("Size of each puff, as a fraction of the blast. Small values + a high count = a real cloud.")]
        public float smokePuffMin = 0.22f;
        public float smokePuffMax = 0.5f;
        [Tooltip("Darkest the smoke gets. Pure black vanishes in a dark tunnel, so this bottoms out just above it.")]
        public Color smokeDark = new Color(0.05f, 0.05f, 0.05f);
        [Tooltip("Colour right as it forms, before it turns to soot.")]
        public Color smokeBirth = new Color(0.35f, 0.22f, 0.16f);

        [Header("Rings")]
        public bool shockwaveRing = true;
        [Tooltip("How far the bright shockwave travels, as a multiple of the blast size.")]
        public float shockwaveReach = 0.8f;
        [Tooltip("Seconds before the shockwave is gone. Raise toward the full duration to have it linger with the smoke.")]
        public float shockwaveLifetime = 0.4f;
        public bool dustWave = true;
        [Tooltip("How far the ground dust wave travels, as a multiple of the blast size.")]
        public float dustReach = 1.1f;
        [Tooltip("Seconds before the dust wave is gone.")]
        public float dustLifetime = 0.7f;
        public bool addLight = true;

        [Header("Debris look")]
        [Tooltip("Forces the lit debris to be completely matte, so rock chips don't look like wet plastic.")]
        public bool matteSmoke = true;
        [Range(0f, 1f)] public float smokeSmoothness = 0f;
        [Range(0f, 1f)] public float smokeMetallic = 0f;

        [Header("Particle materials (need a Particles shader)")]
        public Material fireParticleMaterial;
        [Tooltip("UNLIT particle material for smoke - unlit is what stops the sun shining off it.")]
        public Material smokeParticleMaterial;
        public Material debrisParticleMaterial;
        public Material sparkParticleMaterial;

        [Header("Shape materials (core flash, spikes, rings)")]
        public Material coreMaterial;
        public Material flameMaterial;
        public Material ringMaterial;
        public Material smokeMaterial;

        private static readonly Color CoreColor = new Color(1f, 0.97f, 0.82f);
        private static readonly Color FlameColor = new Color(1f, 0.58f, 0.12f);
        private static readonly Color RingColor = new Color(1f, 0.87f, 0.45f);
        private static readonly Color DustColor = new Color(0.4f, 0.34f, 0.28f);

        private class Blade
        {
            public Transform t;
            public float birth, length, thickness;
        }

        private readonly List<Blade> blades = new List<Blade>();
        private readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        private Transform core;
        private Transform innerFire;
        private Transform ring;
        private Transform dust;
        private Light flash;
        private float age;
        private float scaledRadius;

        private static Mesh sphereMesh;
        private static Mesh cubeMesh;
        private static Mesh coneMesh;
        private static Mesh ringMesh;
        private static Material runtimeParticleLit;
        private static Material runtimeParticleUnlit;
        private static readonly Dictionary<Color, Material> RuntimeMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Material, Material> MatteCache = new Dictionary<Material, Material>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            scaledRadius = baseRadius * Mathf.Max(0.1f, blastScale);

            BuildParticles();
            BuildShapes();
            Animate(0f);

            // Everything is configured - now start them all, from a clean slate, together.
            for (int i = 0; i < systems.Count; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = age / Mathf.Max(0.1f, duration);

            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            Animate(t);
        }

        // ------------------------------------------------------------------ particles

        private void BuildParticles()
        {
            float r = scaledRadius;

            // ---- FIRE: fast, bright, cools as it goes ----
            if (fireParticles > 0)
            {
                ParticleSystem fire = MakeSystem("Fire", fireParticleMaterial, false, SphereMesh());
                ParticleSystem.MainModule main = fire.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(r * 1.2f, r * 3f);
                main.startSize = new ParticleSystem.MinMaxCurve(r * 0.4f, r * 0.85f);
                RandomSpin(main);
                main.gravityModifier = -0.15f;   // fire lifts slightly

                Burst(fire, fireParticles);
                Shape(fire, r * 0.3f, 0.2f);
                SizeOverLife(fire, new Keyframe(0f, 0.25f), new Keyframe(0.18f, 1.1f), new Keyframe(1f, 0f));
                ColorOverLife(fire,
                    new Color(1f, 0.95f, 0.72f),
                    new Color(1f, 0.6f, 0.15f),
                    new Color(0.9f, 0.22f, 0.08f),
                    new Color(0.35f, 0.2f, 0.15f));
                Drag(fire, 1.2f);
            }

            // ---- SMOKE: many small UNLIT puffs. Unlit means no sun, no shine, no shading at all ----
            if (smokeParticles > 0)
            {
                ParticleSystem smoke = MakeSystem("Smoke", smokeParticleMaterial, false, SphereMesh());
                ParticleSystem.MainModule main = smoke.main;
                main.startDelay = new ParticleSystem.MinMaxCurve(0f, 0.12f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(r * 0.5f, r * 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(r * smokePuffMin, r * smokePuffMax);
                RandomSpin(main);
                main.gravityModifier = -0.12f;   // rises

                // Flat black everywhere would read as one solid blob, so each puff gets its own
                // brightness. That variation is what gives an unlit cloud any sense of depth.
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f), new Color(0.6f, 0.6f, 0.6f));

                Burst(smoke, smokeParticles);
                Shape(smoke, r * 0.45f, 0.4f);

                // Keeps billowing the whole time instead of shrinking straight away - that slow
                // expansion is most of what makes smoke look like smoke.
                SizeOverLife(smoke, new Keyframe(0f, 0.35f), new Keyframe(0.45f, 1f), new Keyframe(0.8f, 1.15f), new Keyframe(1f, 0f));
                ColorOverLife(smoke,
                    smokeBirth,
                    Color.Lerp(smokeBirth, smokeDark, 0.65f),
                    Color.Lerp(smokeBirth, smokeDark, 0.9f),
                    smokeDark);
                Drag(smoke, 1.8f);
                RotateOverLife(smoke, 1.2f);
            }

            // ---- DEBRIS: chunks with real gravity that bounce off the rock ----
            if (debrisParticles > 0)
            {
                ParticleSystem debris = MakeSystem("Debris", debrisParticleMaterial, true, CubeMesh());
                ParticleSystem.MainModule main = debris.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(r * 1.5f, r * 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(r * 0.05f, r * 0.16f);
                RandomSpin(main);
                main.gravityModifier = 1.4f;

                Burst(debris, debrisParticles);
                Shape(debris, r * 0.3f, 0.25f);
                SizeOverLife(debris, new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f));
                RotateOverLife(debris, 6f);

                if (debrisBounces)
                {
                    ParticleSystem.CollisionModule collision = debris.collision;
                    collision.enabled = true;
                    collision.type = ParticleSystemCollisionType.World;
                    collision.mode = ParticleSystemCollisionMode.Collision3D;
                    collision.bounce = 0.35f;
                    collision.dampen = 0.45f;
                    collision.lifetimeLoss = 0.15f;
                    collision.quality = ParticleSystemCollisionQuality.Medium;
                    collision.enableDynamicColliders = false;   // terrain only, never players
                }
            }

            // ---- SPARKS: tiny, fast, trailing ----
            if (sparkParticles > 0)
            {
                ParticleSystem sparks = MakeSystem("Sparks", sparkParticleMaterial, false, SphereMesh());
                ParticleSystem.MainModule main = sparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(r * 3f, r * 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(r * 0.02f, r * 0.05f);
                main.gravityModifier = 0.8f;

                Burst(sparks, sparkParticles);
                Shape(sparks, r * 0.2f, 0.1f);
                SizeOverLife(sparks, new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f));
                ColorOverLife(sparks,
                    new Color(1f, 0.98f, 0.8f),
                    new Color(1f, 0.85f, 0.4f),
                    new Color(1f, 0.55f, 0.15f),
                    new Color(0.8f, 0.25f, 0.1f));

                ParticleSystem.TrailModule trails = sparks.trails;
                trails.enabled = true;
                trails.ratio = 1f;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.6f);
                trails.dieWithParticles = false;

                ParticleSystemRenderer psr = sparks.GetComponent<ParticleSystemRenderer>();
                psr.trailMaterial = psr.sharedMaterial;
            }
        }

        private ParticleSystem MakeSystem(string name, Material material, bool lit, Mesh mesh)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            // A freshly added system is ALREADY PLAYING with Unity's defaults. Stop and wipe it
            // before touching anything, or the burst we register at time 0 is already in the past.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.simulationSpeed = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // follows the object's scale
            main.maxParticles = 500;
            main.startDelay = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;   // bursts only
            emission.rateOverDistance = 0f;

            Material chosen = material != null ? material : RuntimeParticleMaterial(lit);
            if (lit && matteSmoke) chosen = Matte(chosen, smokeSmoothness, smokeMetallic);

            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            if (mesh != null) psr.mesh = mesh;
            psr.sharedMaterial = chosen;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            systems.Add(ps);
            return ps;
        }

        /// <summary>
        /// A dull copy of a lit material, applied to a cached copy so the material asset itself
        /// is never altered.
        /// </summary>
        private static Material Matte(Material source, float smoothness, float metallic)
        {
            if (source == null) return null;

            Material cached;
            if (MatteCache.TryGetValue(source, out cached) && cached != null) return cached;

            Material m = new Material(source);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            // URP keeps specular and reflections behind keywords as well as floats.
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            MatteCache[source] = m;
            return m;
        }

        private static void RandomSpin(ParticleSystem.MainModule main)
        {
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static void Shape(ParticleSystem ps, float radius, float randomDirection)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0.6f;
            shape.randomDirectionAmount = randomDirection;
        }

        private static void SizeOverLife(ParticleSystem ps, params Keyframe[] keys)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        private static void ColorOverLife(ParticleSystem ps, Color a, Color b, Color c, Color d)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(a, 0f),
                    new GradientColorKey(b, 0.25f),
                    new GradientColorKey(c, 0.55f),
                    new GradientColorKey(d, 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>
        /// Air resistance. The speed limit is set explicitly: this module's limit defaults to 1,
        /// which would pin every particle in place.
        /// </summary>
        private static void Drag(ParticleSystem ps, float drag)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(9999f);
            limit.dampen = 0f;
            limit.drag = new ParticleSystem.MinMaxCurve(drag);
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
        }

        private static void RotateOverLife(ParticleSystem ps, float radiansPerSecond)
        {
            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
            rot.y = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
            rot.z = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
        }

        // ------------------------------------------------------------------ hand-animated shapes

        private void BuildShapes()
        {
            core = MakeShape("Core", SphereMesh(), coreMaterial, CoreColor);
            innerFire = MakeShape("InnerFire", SphereMesh(), flameMaterial, FlameColor);

            for (int i = 0; i < spikeCount; i++)
            {
                Vector3 dir = (Random.onUnitSphere + Vector3.up * 0.2f).normalized;
                Transform t = MakeShape("Spike", ConeMesh(), i % 2 == 0 ? coreMaterial : flameMaterial,
                                        i % 2 == 0 ? CoreColor : FlameColor);
                t.localRotation = Quaternion.LookRotation(dir);

                blades.Add(new Blade
                {
                    t = t,
                    birth = Random.Range(0f, 0.02f),
                    length = scaledRadius * Random.Range(1.1f, 1.6f),
                    thickness = scaledRadius * Random.Range(0.1f, 0.2f)
                });
            }

            if (shockwaveRing) ring = MakeShape("Shockwave", RingMesh(), ringMaterial, RingColor);
            if (dustWave) dust = MakeShape("DustWave", RingMesh(), smokeMaterial, DustColor);

            if (addLight)
            {
                GameObject lightGo = new GameObject("Flash");
                lightGo.transform.SetParent(transform, false);
                flash = lightGo.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1f, 0.68f, 0.32f);
                flash.range = scaledRadius * 6f;
                flash.intensity = 0f;
            }
        }

        private void Animate(float t)
        {
            float r = scaledRadius;

            // Core: instant white flash, gone fast.
            float ct = Mathf.Clamp01(t / 0.14f);
            float coreScale = ct < 0.3f
                ? Mathf.Lerp(0.25f, 1.1f, EaseOut(ct / 0.3f))
                : Mathf.Lerp(1.1f, 0f, EaseIn((ct - 0.3f) / 0.7f));
            core.localScale = Vector3.one * (r * coreScale);

            // A slower fireball inside gives the blast depth.
            float ft = Mathf.Clamp01(t / 0.25f);
            float fireScale = ft < 0.35f
                ? Mathf.Lerp(0.3f, 1.3f, EaseOut(ft / 0.35f))
                : Mathf.Lerp(1.3f, 0f, EaseIn((ft - 0.35f) / 0.65f));
            innerFire.localScale = Vector3.one * (r * fireScale);

            for (int i = 0; i < blades.Count; i++)
            {
                Blade s = blades[i];
                float st = Mathf.Clamp01((t - s.birth) / 0.2f);

                float len = st < 0.35f
                    ? Mathf.Lerp(0f, s.length, EaseOut(st / 0.35f))
                    : Mathf.Lerp(s.length, 0f, EaseIn((st - 0.35f) / 0.65f));

                // Thickness follows length exactly. A cone scaled to zero LENGTH still draws its
                // base cap at full width - seven of those left a little spiky ball behind.
                float k = s.length > 0.0001f ? Mathf.Clamp01(len / s.length) : 0f;
                float thick = s.thickness * k;

                s.t.localScale = k < 0.002f ? Vector3.zero : new Vector3(thick, thick, len);
            }

            AnimateRing(ring, shockwaveReach, shockwaveLifetime, 0.2f, 0f);
            AnimateRing(dust, dustReach, dustLifetime, 0.35f, r * 0.05f);

            if (flash != null)
            {
                float burst = Mathf.Lerp(34f, 0f, EaseOut(Mathf.Clamp01(t / 0.08f)));
                float glow = Mathf.Lerp(9f, 0f, Mathf.Clamp01((t - 0.06f) / 0.3f));
                flash.intensity = Mathf.Max(burst, glow);
                flash.color = Color.Lerp(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.5f, 0.2f), Mathf.Clamp01(t / 0.18f));
            }
        }

        /// <summary>
        /// Expands a ring to its reach over its own lifetime in seconds, snaps it away at the end,
        /// then switches it off. Squashing a ring on Y does nothing - it's already flat - so it has
        /// to be shrunk and deactivated or it just sits there at full size until the effect dies.
        /// </summary>
        private void AnimateRing(Transform ringT, float reach, float lifetime, float startScale, float height)
        {
            if (ringT == null) return;

            float p = lifetime <= 0.01f ? 1f : age / lifetime;

            if (p >= 1f)
            {
                if (ringT.gameObject.activeSelf) ringT.gameObject.SetActive(false);
                return;
            }

            float scale = Mathf.Lerp(scaledRadius * startScale, scaledRadius * reach, EaseOut(p));

            // Hold full size, then collapse over the last 15% so it pops out rather than lingering.
            float collapse = p < 0.85f ? 1f : 1f - (p - 0.85f) / 0.15f;

            ringT.localScale = new Vector3(scale * collapse, scale * 0.08f * collapse, scale * collapse);
            ringT.localPosition = new Vector3(0f, height, 0f);
        }

        private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
        private static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x; }

        private Transform MakeShape(string name, Mesh mesh, Material material, Color fallback)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.zero;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material != null ? material : RuntimeMaterial(fallback);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        private static Material RuntimeParticleMaterial(bool lit)
        {
            if (lit && runtimeParticleLit != null) return runtimeParticleLit;
            if (!lit && runtimeParticleUnlit != null) return runtimeParticleUnlit;

            Shader shader = Shader.Find(lit
                ? "Universal Render Pipeline/Particles/Lit"
                : "Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");

            Material m = new Material(shader);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.white);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, Color.white);

            if (lit) runtimeParticleLit = m; else runtimeParticleUnlit = m;
            return m;
        }

        private static Material RuntimeMaterial(Color color)
        {
            Material cached;
            if (RuntimeMaterials.TryGetValue(color, out cached) && cached != null) return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");

            Material m = new Material(shader);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, color);

            RuntimeMaterials[color] = m;
            return m;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>
        /// Borrows the mesh off a Unity primitive once, then throws the object away. We never keep
        /// the primitive itself, because primitives come with colliders.
        /// </summary>
        private static Mesh StealPrimitiveMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            temp.SetActive(false);    // takes the collider out of the world immediately
            Destroy(temp);            // safe deferred destroy, legal inside physics callbacks
            return mesh;
        }

        private static Mesh SphereMesh()
        {
            if (sphereMesh == null) sphereMesh = StealPrimitiveMesh(PrimitiveType.Sphere);
            return sphereMesh;
        }

        private static Mesh CubeMesh()
        {
            if (cubeMesh == null) cubeMesh = StealPrimitiveMesh(PrimitiveType.Cube);
            return cubeMesh;
        }

        /// <summary>Cone pointing down +Z: base radius 1 at z=0, tip at z=1.</summary>
        private static Mesh ConeMesh()
        {
            if (coneMesh != null) return coneMesh;

            const int segments = 8;
            Vector3[] verts = new Vector3[segments + 2];
            int[] tris = new int[segments * 6];

            verts[0] = new Vector3(0f, 0f, 1f);   // tip
            verts[1] = Vector3.zero;              // base centre
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts[i + 2] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            }

            int k = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i + 2;
                int b = (i + 1) % segments + 2;
                tris[k++] = 0; tris[k++] = b; tris[k++] = a;   // side
                tris[k++] = 1; tris[k++] = a; tris[k++] = b;   // base cap
            }

            coneMesh = new Mesh { name = "ToonSpike" };
            coneMesh.vertices = verts;
            coneMesh.triangles = tris;
            coneMesh.RecalculateNormals();
            coneMesh.RecalculateBounds();
            return coneMesh;
        }

        /// <summary>Flat ring in the XZ plane, outer radius 1, drawn on both faces. Thin band.</summary>
        private static Mesh RingMesh()
        {
            if (ringMesh != null) return ringMesh;

            const int segments = 28;
            const float inner = 0.86f;   // thin band, so the ring reads as a line

            Vector3[] verts = new Vector3[segments * 2];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                verts[i * 2] = new Vector3(cos * inner, 0f, sin * inner);
                verts[i * 2 + 1] = new Vector3(cos, 0f, sin);
            }

            List<int> tris = new List<int>(segments * 12);
            for (int i = 0; i < segments; i++)
            {
                int i0 = i * 2, i1 = i * 2 + 1;
                int j0 = ((i + 1) % segments) * 2, j1 = j0 + 1;

                tris.Add(i0); tris.Add(i1); tris.Add(j1);
                tris.Add(i0); tris.Add(j1); tris.Add(j0);
                tris.Add(i0); tris.Add(j1); tris.Add(i1);
                tris.Add(i0); tris.Add(j0); tris.Add(j1);
            }

            ringMesh = new Mesh { name = "ToonShockwave" };
            ringMesh.vertices = verts;
            ringMesh.triangles = tris.ToArray();
            ringMesh.RecalculateNormals();
            ringMesh.RecalculateBounds();
            return ringMesh;
        }
    }
}
