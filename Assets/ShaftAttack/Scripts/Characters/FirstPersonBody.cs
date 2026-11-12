using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>
    /// Digby's body as seen by the player himself (first person). Sits on the player's DigbyBody (child of the
    /// Player, next to the camera pivot), with the rigged model underneath.
    ///
    ///  - Head hidden in your own view: a copy of the skinned mesh draws the body, with everything that belongs
    ///    only to the head (face, helmet, goggles) folded into a point. Bits shared with the neck stay on the neck.
    ///  - Whole body in your shadow: the original mesh only casts shadows, head and all.
    ///  - Body kept under the camera: every frame the body slides sideways so his head sits right below the
    ///    camera (his eyes a little behind it, so the neck never shows when you look straight down). It follows
    ///    the top of his neck, not his eyes, so the body stays put when he looks around in the idle.
    ///    In a slide the camera drops below his eyes, so it sits in front of his chest instead (SlideCam bone),
    ///    following the animator's blend into and out of the slide. Whenever the camera is lower than his eyes
    ///    (the slide drop, a slam or landing dip) the body also steps back, so the camera never ends up inside him.
    ///    Looking down, the body moves back as well (lookDownBack: like leaning your head out over your chest), so
    ///    your chest only just shows at the bottom of the screen instead of covering half of it. Looking ahead it
    ///    stays where it was, so the dash / throw / dig arms show as before.
    ///    The feet stay on the ground: only the horizontal position changes. Runs after CameraFeel, so it follows
    ///    the camera's dips and shakes too.
    ///  - Third person (ThirdPersonView, a test view): the whole body shows, head included, and it stands in
    ///    the middle of the player, where other players would see it.
    ///  - Guns: while one is out, the first-person gun view (GunViewmodel) has its own arms, so the body's arm(s)
    ///    holding the gun are folded away in your view (SetArmsHidden). Your shadow still shows them.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class FirstPersonBody : MonoBehaviour
    {
        [Tooltip("The player's camera pivot. Found on the PlayerMotor if left empty.")]
        public Transform cameraPivot;

        [Tooltip("The camera sits this far in front of his eyes (metres), so his neck never shows when you look straight down.")]
        public float eyeForwardOffset = 0.15f;

        [Tooltip("How far his eyes are in front of the top of his neck when he stands at rest (metres). Set by the Digby import.")]
        public float restEyeAhead = 0.2f;

        [Tooltip("When the camera is lower than his eyes (dropping into a slide before his body has), the camera moves up to this much further in front of him so it never ends up inside his chest.")]
        public float lowCameraForward = 0.35f;

        [Header("Looking down")]
        [Tooltip("Looking straight down, the body sits this much further back (metres), so you only just see your chest " +
                 "at the bottom of the screen. 0 = it stays where it is.")]
        public float lookDownBack = 0.5f;
        [Tooltip("Looking further down than this (degrees below level), the body starts moving back...")]
        public float lookDownFrom = 45f;
        [Tooltip("...and from this angle on it is all the way back (straight down = 90).")]
        public float lookDownTo = 89f;

        [Header("Hiding")]
        [Tooltip("Hide the head in your own view.")]
        public bool hideHead = true;

        [Tooltip("Your shadow shows the whole body, head and all.")]
        public bool fullBodyShadow = true;

        private PlayerMotor motor;
        private Transform view;          // the camera itself (CameraFeel dips and shakes it inside the pivot)
        private Transform eye;
        private Transform headJoint;
        private Transform slideCam;
        private SkinnedMeshRenderer source;
        private SkinnedMeshRenderer firstPerson;
        private Animator animator;
        private bool thirdPerson;
        private Transform[] armProxiesRight = new Transform[0];
        private Transform[] armProxiesLeft = new Transform[0];
        private bool armsHiddenRight, armsHiddenLeft;
        private static readonly int SlideHash = Animator.StringToHash("Slide");
        private static readonly string[] RightArm = { "RightUpperArm", "RightLowerArm", "RightHand", "RightIndex1", "RightIndex2", "Half_Elbow.R" };
        private static readonly string[] LeftArm = { "LeftUpperArm", "LeftLowerArm", "LeftHand", "LeftIndex1", "LeftIndex2", "Half_Elbow.L" };

        public bool IsThirdPerson { get { return thirdPerson; } }

        private void Start()
        {
            motor = GetComponentInParent<PlayerMotor>();
            if (cameraPivot == null && motor != null) cameraPivot = motor.cameraPivot;
            Camera cam = cameraPivot != null ? cameraPivot.GetComponentInChildren<Camera>() : null;
            view = cam != null ? cam.transform : cameraPivot;
            source = GetComponentInChildren<SkinnedMeshRenderer>();
            if (source == null) return;
            animator = GetComponentInChildren<Animator>();
            eye = Find(transform, "Eye");
            headJoint = Find(transform, "Head");
            slideCam = Find(transform, "SlideCam");
            if (hideHead) BuildFirstPersonMesh();
            ApplyView();
            MinerTeamColour tc = GetComponent<MinerTeamColour>();
            if (tc != null) tc.Apply();
        }

        private void BuildFirstPersonMesh()
        {
            Transform head = Find(transform, "Head");
            Transform neck = Find(transform, "Neck");
            Mesh mesh = source.sharedMesh;
            if (head == null || neck == null || mesh == null || !mesh.isReadable) return;
            Transform[] bones = source.bones;
            int headIdx = System.Array.IndexOf(bones, head);
            int neckIdx = System.Array.IndexOf(bones, neck);
            if (headIdx < 0 || neckIdx < 0) return;

            // a bone that follows the head at zero size: what is skinned to it folds into a point
            GameObject fold = new GameObject("HeadFolded");
            fold.transform.SetParent(head, false);
            fold.transform.localScale = Vector3.one * 0.0001f;
            int foldIdx = bones.Length;
            // and a stand-in for every arm bone (same place, full size): shrunk to nothing when a gun is out
            // (SetArmsHidden), which folds that arm away in your view only
            var proxies = new System.Collections.Generic.List<Transform>();
            var proxyOf = new System.Collections.Generic.Dictionary<int, int>();
            var right = new System.Collections.Generic.List<Transform>();
            var left = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bool r = System.Array.IndexOf(RightArm, bones[i].name) >= 0;
                bool l = System.Array.IndexOf(LeftArm, bones[i].name) >= 0;
                if (!r && !l) continue;
                GameObject px = new GameObject("FP_" + bones[i].name);
                px.transform.SetParent(bones[i], false);
                proxyOf[i] = bones.Length + 1 + proxies.Count;
                proxies.Add(px.transform);
                (r ? right : left).Add(px.transform);
            }
            armProxiesRight = right.ToArray();
            armProxiesLeft = left.ToArray();
            Transform[] newBones = new Transform[bones.Length + 1 + proxies.Count];
            bones.CopyTo(newBones, 0);
            newBones[foldIdx] = fold.transform;
            for (int i = 0; i < proxies.Count; i++) newBones[bones.Length + 1 + i] = proxies[i];

            Mesh copy = Instantiate(mesh);
            copy.name = mesh.name + " (first person)";
            Matrix4x4[] bind = mesh.bindposes;
            Matrix4x4[] newBind = new Matrix4x4[newBones.Length];
            bind.CopyTo(newBind, 0);
            newBind[foldIdx] = bind[headIdx];
            foreach (var kv in proxyOf) newBind[kv.Value] = bind[kv.Key];
            copy.bindposes = newBind;

            BoneWeight[] w = mesh.boneWeights;
            for (int i = 0; i < w.Length; i++)
            {
                BoneWeight b = w[i];
                float h = (b.boneIndex0 == headIdx ? b.weight0 : 0f) + (b.boneIndex1 == headIdx ? b.weight1 : 0f) +
                          (b.boneIndex2 == headIdx ? b.weight2 : 0f) + (b.boneIndex3 == headIdx ? b.weight3 : 0f);
                if (h > 0f)
                {
                    int to = h >= 0.99f ? foldIdx : neckIdx;      // all head: fold it away; shared with the neck: keep it
                    if (b.boneIndex0 == headIdx) b.boneIndex0 = to;
                    if (b.boneIndex1 == headIdx) b.boneIndex1 = to;
                    if (b.boneIndex2 == headIdx) b.boneIndex2 = to;
                    if (b.boneIndex3 == headIdx) b.boneIndex3 = to;
                }
                int p;
                if (b.weight0 > 0f && proxyOf.TryGetValue(b.boneIndex0, out p)) b.boneIndex0 = p;
                if (b.weight1 > 0f && proxyOf.TryGetValue(b.boneIndex1, out p)) b.boneIndex1 = p;
                if (b.weight2 > 0f && proxyOf.TryGetValue(b.boneIndex2, out p)) b.boneIndex2 = p;
                if (b.weight3 > 0f && proxyOf.TryGetValue(b.boneIndex3, out p)) b.boneIndex3 = p;
                w[i] = Merge(b);
            }
            copy.boneWeights = w;

            GameObject go = new GameObject("FirstPersonMesh");
            go.transform.SetParent(source.transform.parent, false);
            go.transform.localPosition = source.transform.localPosition;
            go.transform.localRotation = source.transform.localRotation;
            go.transform.localScale = source.transform.localScale;
            go.layer = source.gameObject.layer;
            firstPerson = go.AddComponent<SkinnedMeshRenderer>();
            firstPerson.sharedMesh = copy;
            firstPerson.bones = newBones;
            firstPerson.rootBone = source.rootBone;
            firstPerson.sharedMaterials = source.sharedMaterials;
            firstPerson.localBounds = source.localBounds;
            firstPerson.updateWhenOffscreen = true;
            firstPerson.quality = source.quality;
            firstPerson.shadowCastingMode = ShadowCastingMode.Off;
            firstPerson.receiveShadows = true;
        }

        /// <summary>Fold the right / left arm away in your own view (a gun is out: the gun view has its own arms).
        /// Third person and your shadow always show both.</summary>
        public void SetArmsHidden(bool right, bool left)
        {
            if (right != armsHiddenRight)
            {
                armsHiddenRight = right;
                foreach (Transform t in armProxiesRight) t.localScale = Vector3.one * (right ? 0.0001f : 1f);
            }
            if (left != armsHiddenLeft)
            {
                armsHiddenLeft = left;
                foreach (Transform t in armProxiesLeft) t.localScale = Vector3.one * (left ? 0.0001f : 1f);
            }
        }

        /// <summary>Third-person test view: true shows the whole body (head and all) standing in the middle of the
        /// player, the way other players see it. False goes back to the first-person body.</summary>
        public void SetThirdPerson(bool on)
        {
            thirdPerson = on;
            ApplyView();
        }

        private void ApplyView()
        {
            if (source == null) return;
            if (firstPerson != null)
            {
                firstPerson.enabled = !thirdPerson;
                source.enabled = thirdPerson || fullBodyShadow;
                source.shadowCastingMode = thirdPerson ? ShadowCastingMode.On
                                         : fullBodyShadow ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off;
            }
            if (thirdPerson) Centre();
        }

        private void Centre()
        {
            Vector3 p = transform.localPosition;
            transform.localPosition = new Vector3(0f, p.y, 0f);
        }

        /// <summary>Two slots pointing at the same bone (head moved onto the neck) become one.</summary>
        private static BoneWeight Merge(BoneWeight b)
        {
            int[] idx = { b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
            float[] wt = { b.weight0, b.weight1, b.weight2, b.weight3 };
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (wt[j] > 0f && idx[j] == idx[i]) { wt[i] += wt[j]; wt[j] = 0f; idx[j] = 0; }
            // keep them sorted by weight, biggest first (Unity expects that)
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (wt[j] > wt[i]) { float tw = wt[i]; wt[i] = wt[j]; wt[j] = tw; int ti = idx[i]; idx[i] = idx[j]; idx[j] = ti; }
            b.boneIndex0 = idx[0]; b.weight0 = wt[0];
            b.boneIndex1 = idx[1]; b.weight1 = wt[1];
            b.boneIndex2 = idx[2]; b.weight2 = wt[2];
            b.boneIndex3 = idx[3]; b.weight3 = wt[3];
            return b;
        }

        /// <summary>How much of the slide pose the animator is showing right now (0..1, follows its blends).</summary>
        private float SlidePose()
        {
            if (animator == null) return motor != null && motor.IsSliding ? 1f : 0f;
            float w = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == SlideHash ? 1f : 0f;
            if (animator.IsInTransition(0))
            {
                float next = animator.GetNextAnimatorStateInfo(0).shortNameHash == SlideHash ? 1f : 0f;
                w = Mathf.Lerp(w, next, Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
            }
            return w;
        }

        private void LateUpdate()
        {
            if (thirdPerson) { Centre(); return; }
            if (view == null || eye == null) return;
            Vector3 camPos = view.position;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            // camera below his eyes (it drops into a slide a moment before his body does, slam dip): keep it in front of him
            float low = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, eye.position.y - camPos.y));
            // where the camera goes on his body: in front of his face, measured from the top of his neck
            Vector3 face = headJoint != null ? headJoint.position + fwd * restEyeAhead : eye.position;
            // looking down: the body moves back too, so the chest only just shows at the bottom of the screen
            float down = Mathf.Asin(Mathf.Clamp(-view.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float back = lookDownBack * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lookDownFrom, lookDownTo, down));
            Vector3 spot = face + fwd * (eyeForwardOffset + back + lowCameraForward * low);
            float slide = SlidePose();
            if (slideCam != null && slide > 0f)
                spot = Vector3.Lerp(spot, slideCam.position, Mathf.SmoothStep(0f, 1f, slide));
            Vector3 shift = camPos - spot;
            shift.y = 0f;
            transform.position += shift;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}

