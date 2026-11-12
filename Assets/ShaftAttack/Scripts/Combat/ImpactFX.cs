using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>
    /// Bullet impacts, made from three shared particle systems (no prefab, no object per hit):
    ///   Dust    soft puffs in the rock's colour that swell and shrink away
    ///   Chips   little lit cubes of rock that fly off the wall and fall
    ///   Sparks  quick bright streaks
    /// Rock hits use all three; hits on something with Health get a burst of sparks and a white puff instead.
    /// PlayerGun hands over the materials from its inspector; placeholders are made if they are empty.
    /// </summary>
    public static class ImpactFX
    {
        private static ParticleSystem dust, chips, sparks;
        private static Material dustMat, chipMat, sparkMat;
        private static Mesh sphere, cube;

        public static void SetMaterials(Material dustMaterial, Material chipMaterial, Material sparkMaterial)
        {
            if (dustMaterial != null) dustMat = dustMaterial;
            if (chipMaterial != null) chipMat = chipMaterial;
            if (sparkMaterial != null) sparkMat = sparkMaterial;
        }

        /// <summary>A bullet hit rock (or anything without Health).</summary>
        public static void Rock(Vector3 point, Vector3 normal, Vector3 dir, Color rock)
        {
            if (!Ensure()) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : -dir;
            Vector3 bounce = Vector3.Reflect(dir, n).normalized;
            Color light = Color.Lerp(rock, Color.white, 0.18f);
            Color dark = rock * 0.7f;
            dark.a = 1f;
            for (int i = 0; i < 6; i++)
                Emit(dust, point + n * 0.06f, Cone(n, 50f) * Random.Range(0.6f, 2.2f), Random.Range(0.14f, 0.3f),
                     Random.Range(0.35f, 0.6f), Color.Lerp(light, rock, Random.value));
            for (int i = 0; i < 7; i++)
                Emit(chips, point + n * 0.03f, (Cone(n, 55f) + bounce * 0.4f).normalized * Random.Range(3f, 7.5f),
                     Random.Range(0.03f, 0.075f), Random.Range(0.5f, 0.9f), Color.Lerp(dark, rock, Random.value));
            for (int i = 0; i < 4; i++)
                Emit(sparks, point + n * 0.02f, (Cone(n, 60f) + bounce).normalized * Random.Range(7f, 13f),
                     Random.Range(0.02f, 0.035f), Random.Range(0.06f, 0.14f), new Color(1f, 0.82f, 0.5f));
        }

        /// <summary>A bullet hit something that can be hurt.</summary>
        public static void Hit(Vector3 point, Vector3 normal, Vector3 dir)
        {
            if (!Ensure()) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : -dir;
            for (int i = 0; i < 9; i++)
                Emit(sparks, point + n * 0.03f, Cone(n, 70f) * Random.Range(5f, 11f), Random.Range(0.025f, 0.04f),
                     Random.Range(0.08f, 0.18f), Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.55f, 0.2f), Random.value));
            for (int i = 0; i < 3; i++)
                Emit(dust, point + n * 0.05f, Cone(n, 40f) * Random.Range(0.4f, 1.2f), Random.Range(0.1f, 0.18f),
                     Random.Range(0.18f, 0.3f), new Color(0.95f, 0.93f, 0.88f));
        }

        private static Vector3 Cone(Vector3 n, float degrees)
        {
            Vector3 r = Random.onUnitSphere;
            return Vector3.Slerp(n, (r + n * 0.001f).normalized, Random.Range(0f, degrees / 180f)).normalized;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color color)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = pos;
            ep.velocity = vel;
            ep.startSize = size;
            ep.startLifetime = life;
            ep.startColor = color;
            ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            ep.applyShapeToPosition = false;
            ps.Emit(ep, 1);
        }

        private static bool Ensure()
        {
            if (dust != null && chips != null && sparks != null) return true;
            GameObject root = new GameObject("Impacts (effects)");
            dust = Make(root.transform, "Dust", dustMat != null ? dustMat : Runtime(false), true, Sphere(), -0.08f, 2.5f);
            chips = Make(root.transform, "Chips", chipMat != null ? chipMat : Runtime(true), true, Cube(), 1.6f, 0.4f);
            sparks = Make(root.transform, "Sparks", sparkMat != null ? sparkMat : Runtime(false), false, null, 0.5f, 0f);
            // dust: swells a little then shrinks away (it's opaque, so it can't fade)
            SizeCurve(dust, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            SizeCurve(chips, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            SizeCurve(sparks, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
            ParticleSystem.MainModule cm = chips.main;
            cm.startRotation3D = true;
            ParticleSystem.RotationOverLifetimeModule rot = chips.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
            rot.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
            rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            ParticleSystemRenderer sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.025f;
            sr.lengthScale = 1.5f;
            return dust != null;
        }

        private static ParticleSystem Make(Transform parent, string name, Material mat, bool mesh, Mesh m, float gravity, float drag)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);    // a new system is already playing: reset first
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = false;
            if (drag > 0f)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = 1000f;                  // the default limit (1 m/s) would freeze everything
                lv.drag = drag;
                lv.multiplyDragByParticleSize = false;
                lv.multiplyDragByParticleVelocity = false;
            }
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (mesh && m != null)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = m;
            }
            ps.Play(true);
            return ps;
        }

        private static void SizeCurve(ParticleSystem ps, AnimationCurve curve)
        {
            ParticleSystem.SizeOverLifetimeModule s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static Material Runtime(bool lit)
        {
            Shader s = Shader.Find(lit ? "Universal Render Pipeline/Particles/Lit" : "Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            Material m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            return m;
        }

        private static Mesh Sphere()
        {
            if (sphere == null) sphere = Borrow(PrimitiveType.Sphere);
            return sphere;
        }

        private static Mesh Cube()
        {
            if (cube == null) cube = Borrow(PrimitiveType.Cube);
            return cube;
        }

        /// <summary>The mesh off a Unity primitive (the primitive itself is thrown away: it comes with a collider).</summary>
        private static Mesh Borrow(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh m = temp.GetComponent<MeshFilter>().sharedMesh;
            temp.SetActive(false);
            Object.Destroy(temp);
            return m;
        }
    }
}
