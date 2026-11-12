using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>
    /// A gun model in the world or in your view: finds its marker children (Grip, Muzzle, Trigger, Foregrip - see
    /// GunDefinition), turns the Trigger part, and makes the muzzle flash (a little glow mesh and a light, built in
    /// code). Added to every gun instance by GunViewmodel / DigbyGunHold, nothing to set up.
    /// </summary>
    public class GunModel : MonoBehaviour
    {
        public Transform grip, muzzle, trigger, foregrip;

        /// <summary>Digby's grip bones (Pickaxe in the right fist, LeftGrip in the left) are the markers' frame turned
        /// half way round about Y: markers have X right, Y up the handle, Z forward; the bones X left, Y up, Z back.</summary>
        public static readonly Quaternion ToHand = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>The prefab root's own rotation. With it the gun points along +Z with its top up (an FBX root
        /// usually carries Blender's axis conversion here). Turn the gun with RootFor.</summary>
        public Quaternion FrameOffset { get; private set; }

        /// <summary>The root rotation that makes the gun point along frame's +Z with its top along frame's +Y.</summary>
        public Quaternion RootFor(Quaternion frame)
        {
            return frame * FrameOffset;
        }

        /// <summary>The rotation a hand's grip bone needs to hold this marker (Grip or Foregrip).</summary>
        public static Quaternion HandOn(Transform marker)
        {
            return marker.rotation * ToHand;
        }

        private Quaternion triggerRest;
        private float triggerSign = 1f;
        private Transform flash;
        private Renderer flashRenderer;
        private Light flashLight;
        private float flashUntil;
        private Renderer[] renderers;

        public Renderer[] Renderers { get { return renderers; } }

        /// <summary>Set up once: layer for everything, whether it casts shadows, the flash material.</summary>
        public void Init(int layer, ShadowCastingMode shadows, bool receiveShadows, Material flashMaterial)
        {
            FrameOffset = transform.localRotation;          // as it came out of the prefab
            grip = Find("Grip");
            muzzle = Find("Muzzle");
            trigger = Find("Trigger");
            foregrip = Find("Foregrip");
            if (grip == null) grip = transform;
            if (muzzle == null)
            {
                // no marker: the front of the model, at mid height
                Bounds b = LocalBounds();
                GameObject m = new GameObject("Muzzle");
                m.transform.SetParent(transform, false);
                m.transform.localPosition = new Vector3(b.center.x, b.center.y, b.max.z);
                muzzle = m.transform;
            }
            if (trigger != null)
            {
                triggerRest = trigger.localRotation;
                triggerSign = PullSign();
            }
            SetLayer(transform, layer);
            renderers = GetComponentsInChildren<Renderer>(true);
            SetShadows(shadows, receiveShadows);
            MakeFlash(flashMaterial, layer);
        }

        public void SetShadows(ShadowCastingMode shadows, bool receive)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
            {
                if (r == null || r == flashRenderer) continue;
                r.shadowCastingMode = shadows;
                r.receiveShadows = receive;
            }
        }

        public void SetVisible(bool on)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null && r != flashRenderer) r.enabled = on;
            if (!on) HideFlash();
        }

        /// <summary>0 = trigger forward, 1 = pulled all the way (degrees).</summary>
        public void SetTrigger(float amount, float travel)
        {
            if (trigger == null) return;
            trigger.localRotation = triggerRest * Quaternion.AngleAxis(travel * amount * triggerSign, Vector3.right);
        }

        public void Flash()
        {
            if (flash == null) return;
            flashUntil = Time.time + 0.05f;
            flash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.localScale = Vector3.one * Random.Range(0.8f, 1.25f);
            if (flashRenderer != null) flashRenderer.enabled = true;
            if (flashLight != null) flashLight.enabled = true;
        }

        private void HideFlash()
        {
            if (flashRenderer != null) flashRenderer.enabled = false;
            if (flashLight != null) flashLight.enabled = false;
        }

        private void LateUpdate()
        {
            if (flash != null && Time.time >= flashUntil && (flashRenderer.enabled || (flashLight != null && flashLight.enabled)))
                HideFlash();
        }

        /// <summary>Which way does the trigger turn to go back toward the grip? (Depends on how the model was made.)</summary>
        private float PullSign()
        {
            Renderer r = trigger.GetComponentInChildren<Renderer>();
            if (r == null || grip == null) return 1f;
            Quaternion keep = trigger.localRotation;
            Vector3 c = r.bounds.center;
            float plus = Vector3.Distance(RotatedCentre(c, keep, 15f), grip.position);
            float minus = Vector3.Distance(RotatedCentre(c, keep, -15f), grip.position);
            trigger.localRotation = keep;
            return plus < minus ? 1f : -1f;
        }

        private Vector3 RotatedCentre(Vector3 centre, Quaternion keep, float deg)
        {
            // where the trigger's centre goes if the trigger turns by deg about its X axis
            Vector3 local = trigger.InverseTransformPoint(centre);
            trigger.localRotation = keep * Quaternion.AngleAxis(deg, Vector3.right);
            Vector3 world = trigger.TransformPoint(local);
            trigger.localRotation = keep;
            return world;
        }

        private void MakeFlash(Material mat, int layer)
        {
            if (muzzle == null) return;
            GameObject go = new GameObject("MuzzleFlash");
            go.layer = layer;
            go.transform.SetParent(muzzle, false);
            flash = go.transform;
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = FlashMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat != null ? mat : FallbackFlash();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.enabled = false;
            flashRenderer = mr;
            GameObject lg = new GameObject("FlashLight");
            lg.transform.SetParent(muzzle, false);
            lg.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            flashLight = lg.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.72f, 0.38f);
            flashLight.range = 7f;
            flashLight.intensity = 3f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        private static Mesh flashMesh;
        private static Material fallbackFlash;

        /// <summary>Three glowing quads: two crossed along the barrel (the side flame) and one facing forward (the star).</summary>
        private static Mesh FlashMesh()
        {
            if (flashMesh != null) return flashMesh;
            const float len = 0.26f, wid = 0.075f, face = 0.075f;
            Vector3[] v =
            {
                new Vector3(-wid, 0f, 0f), new Vector3(wid, 0f, 0f), new Vector3(wid, 0f, len), new Vector3(-wid, 0f, len),
                new Vector3(0f, -wid, 0f), new Vector3(0f, wid, 0f), new Vector3(0f, wid, len), new Vector3(0f, -wid, len),
                new Vector3(-face, -face, 0.02f), new Vector3(face, -face, 0.02f), new Vector3(face, face, 0.02f), new Vector3(-face, face, 0.02f),
            };
            Vector2[] uv =
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            int[] tri = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11 };
            Color[] col = new Color[12];
            for (int i = 0; i < 12; i++) col[i] = new Color(1f, 0.8f, 0.45f, 1f);
            flashMesh = new Mesh { name = "MuzzleFlash", vertices = v, uv = uv, triangles = tri, colors = col };
            flashMesh.RecalculateBounds();
            return flashMesh;
        }

        private static Material FallbackFlash()
        {
            if (fallbackFlash != null) return fallbackFlash;
            Shader s = Shader.Find("ShaftAttack/Tracer");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            fallbackFlash = new Material(s);
            if (fallbackFlash.HasProperty("_Radial")) fallbackFlash.SetFloat("_Radial", 1f);
            return fallbackFlash;
        }

        private Bounds LocalBounds()
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private Transform Find(string name)
        {
            return ArmChain.Deep(transform, name);
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>Move/turn the gun so its Grip marker lands exactly on (pos, rot). To put it in a fist:
        /// PlaceByGrip(gripBone.position, gripBone.rotation * ToHand).</summary>
        public void PlaceByGrip(Vector3 pos, Quaternion rot)
        {
            Quaternion rel = Quaternion.Inverse(transform.rotation) * grip.rotation;
            transform.rotation = rot * Quaternion.Inverse(rel);
            transform.position += pos - grip.position;
        }
    }
}
