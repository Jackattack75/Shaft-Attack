using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShaftAttack
{
    /// <summary>
    /// The gun in your view (first person): Digby's own forearms and gloves holding it in front of the camera.
    ///
    ///  - Drawn by a second camera (an overlay on the player's camera, its own field of view, layer "Viewmodel"), so it
    ///    never sinks into a wall and doesn't stretch when the view widens at speed.
    ///  - The arms are a copy of Digby's model with everything but the arms folded away. The gun is placed in front of
    ///    the camera (GunDefinition.viewOffset / viewEuler, plus sway, bob, recoil and the draw when you switch), then
    ///    the arms reach for it: right hand on the Grip, left hand on the Foregrip for two-handed guns (two-bone IK).
    ///    So any gun model with those markers is held right, nothing to animate.
    ///  - The trigger finger squeezes with every shot and the gun's Trigger part goes back with it.
    ///  - Hidden while the pickaxe is out (your body's arms do the digging, as before) and in third person.
    ///    During a bomb throw the left hand lets go of a two-handed gun (your body's left arm throws).
    /// Sits on the Player.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]             // after the camera has moved (CameraFeel, FirstPersonBody, ThirdPersonView)
    public class GunViewmodel : MonoBehaviour
    {
        [Tooltip("Digby's model (Characters/Digby/Digby.fbx): its arms are your arms.")]
        public GameObject armsModel;
        [Tooltip("Digby_Base and Digby_Team.")]
        public Material[] armsMaterials;
        [Tooltip("Field of view of the gun camera (the world uses CameraFeel's, which widens with speed).")]
        public float viewFieldOfView = 68f;
        [Tooltip("Size of the arms and gun in your view (Digby himself is drawn at 1.125).")]
        public float armsScale = 0.9f;

        [Header("Arms (camera space: right, up, forward)")]
        [Tooltip("Where the right shoulder would be. Off screen; it moves closer if the hand can't reach.")]
        public Vector3 rightShoulder = new Vector3(0.24f, -0.52f, -0.1f);
        public Vector3 leftShoulder = new Vector3(-0.26f, -0.52f, -0.1f);
        [Tooltip("Which way the elbows point.")]
        public Vector3 rightElbow = new Vector3(0.7f, -1f, -0.3f);
        public Vector3 leftElbow = new Vector3(-0.7f, -1f, -0.3f);

        [Header("Feel")]
        [Tooltip("The gun lags behind when you turn: metres per unit of mouse movement...")]
        public float sway = 0.004f;
        [Tooltip("...and degrees per unit.")]
        public float swayTurn = 0.8f;
        [Tooltip("Most mouse movement the sway follows (units).")]
        public float maxSway = 7f;
        [Tooltip("Up-and-down bob while running (metres).")]
        public float bob = 0.009f;
        [Tooltip("Bob cycles per metre run.")]
        public float bobRate = 0.16f;
        [Tooltip("How far below the view the gun starts when you pull it out (metres).")]
        public float drawDrop = 0.32f;

        public bool Showing { get; private set; }

        private Loadout loadout;
        private PlayerGun shooter;
        private PlayerMotor motor;
        private PlayerLook look;
        private ThirdPersonView thirdPerson;
        private DigbyAnimDriver bodyDriver;
        private Camera cam;
        private Camera vmCam;
        private int layer;
        private Transform root;
        private SkinnedMeshRenderer arms;
        private Transform armsRoot;
        private float scaleNow;
        private ArmChain right, left;
        private Vector3 leftScale = Vector3.one;
        private readonly Dictionary<GunDefinition, GunModel> models = new Dictionary<GunDefinition, GunModel>();
        private GunModel shown;
        private GunDefinition shownGun;
        private Vector2 swayNow;
        private float bobPhase;
        private float bobWeight;
        private float kickBack;
        private float kickUp;
        private float leftHold = 1f;

        private void Start()
        {
            loadout = GetComponent<Loadout>();
            shooter = GetComponent<PlayerGun>();
            motor = GetComponent<PlayerMotor>();
            look = GetComponent<PlayerLook>();
            thirdPerson = GetComponent<ThirdPersonView>();
            bodyDriver = GetComponentInChildren<DigbyAnimDriver>();
            Transform pivot = motor != null ? motor.cameraPivot : null;
            cam = pivot != null ? pivot.GetComponentInChildren<Camera>() : GetComponentInChildren<Camera>();
            if (cam == null || loadout == null) { enabled = false; return; }
            MakeCamera();
            root = new GameObject("Viewmodel").transform;
            root.SetParent(cam.transform, false);
            BuildArms();
            if (shooter != null) shooter.OnShot += Shot;
            Hide();
        }

        private void OnDestroy()
        {
            if (shooter != null) shooter.OnShot -= Shot;
        }

        // ------------------------------------------------------------------ setup

        private void MakeCamera()
        {
            layer = LayerMask.NameToLayer("Viewmodel");
            if (layer < 0)
            {
                Debug.LogWarning("[Viewmodel] no \"Viewmodel\" layer: the gun is drawn with the world (Shaft Attack > Rebuild Weapons adds it).");
                layer = cam.gameObject.layer;
                return;
            }
            GameObject go = new GameObject("ViewmodelCamera");
            go.transform.SetParent(cam.transform, false);
            vmCam = go.AddComponent<Camera>();
            vmCam.clearFlags = CameraClearFlags.Depth;
            vmCam.cullingMask = 1 << layer;
            vmCam.fieldOfView = viewFieldOfView;
            vmCam.nearClipPlane = 0.01f;
            vmCam.farClipPlane = 6f;
            UniversalAdditionalCameraData data = vmCam.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            if (baseData != null)
            {
                data.renderPostProcessing = baseData.renderPostProcessing;
                data.antialiasing = baseData.antialiasing;
                if (!baseData.cameraStack.Contains(vmCam)) baseData.cameraStack.Add(vmCam);
            }
            cam.cullingMask &= ~(1 << layer);
        }

        private void BuildArms()
        {
            if (armsModel == null) { Debug.LogWarning("[Viewmodel] no arms model set"); return; }
            GameObject inst = Instantiate(armsModel, root, false);
            inst.name = "FirstPersonArms";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one * armsScale;
            armsRoot = inst.transform;
            scaleNow = armsScale;
            foreach (Animator a in inst.GetComponentsInChildren<Animator>())
            {
                a.enabled = false;
                Destroy(a);
            }
            Transform pick = ArmChain.Deep(inst.transform, "PickaxeMesh");
            if (pick != null) pick.gameObject.SetActive(false);
            arms = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            right = ArmChain.Find(inst.transform, "Right", "Pickaxe");
            left = ArmChain.Find(inst.transform, "Left", "LeftGrip");
            if (arms == null || right == null || left == null) { Debug.LogWarning("[Viewmodel] arms model has no arms"); return; }
            right.CaptureRest();
            left.CaptureRest();
            leftScale = left.upper.localScale;
            ArmsOnly(arms);
            if (armsMaterials != null && armsMaterials.Length > 0) arms.sharedMaterials = armsMaterials;
            arms.updateWhenOffscreen = true;
            arms.shadowCastingMode = ShadowCastingMode.Off;
            arms.receiveShadows = true;
            GunModel.SetLayer(inst.transform, layer);
        }

        /// <summary>A copy of the mesh where everything except the two arms is folded into the shoulders.</summary>
        private void ArmsOnly(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) return;
            Transform[] bones = smr.bones;
            HashSet<int> armR = Indices(bones, "RightUpperArm", "RightLowerArm", "RightHand", "RightIndex1", "RightIndex2", "Half_Elbow.R");
            HashSet<int> armL = Indices(bones, "LeftUpperArm", "LeftLowerArm", "LeftHand", "LeftIndex1", "LeftIndex2", "Half_Elbow.L");
            int upR = System.Array.IndexOf(bones, right.upper), upL = System.Array.IndexOf(bones, left.upper);
            if (upR < 0 || upL < 0) return;
            Matrix4x4[] bind = mesh.bindposes;
            Vector3 shR = bind[upR].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 shL = bind[upL].inverse.MultiplyPoint3x4(Vector3.zero);

            Transform[] nb = new Transform[bones.Length + 2];
            bones.CopyTo(nb, 0);
            int foldR = bones.Length, foldL = bones.Length + 1;
            nb[foldR] = FoldBone(right.upper, "ArmsFold.R");
            nb[foldL] = FoldBone(left.upper, "ArmsFold.L");
            Matrix4x4[] nbind = new Matrix4x4[bind.Length + 2];
            bind.CopyTo(nbind, 0);
            nbind[foldR] = bind[upR];
            nbind[foldL] = bind[upL];

            Vector3[] v = mesh.vertices;
            BoneWeight[] w = mesh.boneWeights;
            int kept = 0;
            for (int i = 0; i < w.Length; i++)
            {
                BoneWeight b = w[i];
                float r = Sum(b, armR), l = Sum(b, armL);
                if (r >= 0.5f || l >= 0.5f)
                {
                    bool isR = r >= l;
                    HashSet<int> set = isR ? armR : armL;
                    int up = isR ? upR : upL;
                    if (!set.Contains(b.boneIndex0)) b.boneIndex0 = up;      // bits of the shoulder follow the arm
                    if (!set.Contains(b.boneIndex1)) b.boneIndex1 = up;
                    if (!set.Contains(b.boneIndex2)) b.boneIndex2 = up;
                    if (!set.Contains(b.boneIndex3)) b.boneIndex3 = up;
                    w[i] = Merge(b);
                    kept++;
                }
                else
                {
                    int f = (v[i] - shR).sqrMagnitude < (v[i] - shL).sqrMagnitude ? foldR : foldL;
                    w[i] = new BoneWeight { boneIndex0 = f, weight0 = 1f };
                }
            }
            Mesh copy = Instantiate(mesh);
            copy.name = mesh.name + " (first-person arms)";
            copy.bindposes = nbind;
            copy.boneWeights = w;
            smr.sharedMesh = copy;
            smr.bones = nb;
            Debug.Log("[Viewmodel] arms: " + kept + " of " + w.Length + " vertices kept");
        }

        private static Transform FoldBone(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.0001f;
            return go.transform;
        }

        private static HashSet<int> Indices(Transform[] bones, params string[] names)
        {
            HashSet<int> s = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && System.Array.IndexOf(names, bones[i].name) >= 0) s.Add(i);
            return s;
        }

        private static float Sum(BoneWeight b, HashSet<int> set)
        {
            return (set.Contains(b.boneIndex0) ? b.weight0 : 0f) + (set.Contains(b.boneIndex1) ? b.weight1 : 0f) +
                   (set.Contains(b.boneIndex2) ? b.weight2 : 0f) + (set.Contains(b.boneIndex3) ? b.weight3 : 0f);
        }

        private static BoneWeight Merge(BoneWeight b)
        {
            int[] idx = { b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
            float[] wt = { b.weight0, b.weight1, b.weight2, b.weight3 };
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (wt[j] > 0f && idx[j] == idx[i]) { wt[i] += wt[j]; wt[j] = 0f; idx[j] = 0; }
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (wt[j] > wt[i]) { float tw = wt[i]; wt[i] = wt[j]; wt[j] = tw; int ti = idx[i]; idx[i] = idx[j]; idx[j] = ti; }
            b.boneIndex0 = idx[0]; b.weight0 = wt[0];
            b.boneIndex1 = idx[1]; b.weight1 = wt[1];
            b.boneIndex2 = idx[2]; b.weight2 = wt[2];
            b.boneIndex3 = idx[3]; b.weight3 = wt[3];
            return b;
        }

        private GunModel ModelFor(GunDefinition gun)
        {
            GunModel m;
            if (models.TryGetValue(gun, out m) && m != null) return m;
            if (gun.model == null) return null;
            GameObject go = Instantiate(gun.model, root, false);
            go.name = gun.displayName + " (first person)";
            go.transform.localScale = gun.model.transform.localScale * armsScale;
            m = go.GetComponent<GunModel>();
            if (m == null) m = go.AddComponent<GunModel>();
            m.Init(layer, ShadowCastingMode.Off, true, shooter != null ? shooter.flashMaterial : null);
            m.SetVisible(false);
            models[gun] = m;
            return m;
        }

        // ------------------------------------------------------------------ every frame

        private void Shot(GunDefinition gun)
        {
            kickBack = Mathf.Min(kickBack + gun.viewKickBack, gun.viewKickBack * 2.2f);
            kickUp = Mathf.Min(kickUp + gun.viewKickUp, gun.viewKickUp * 2.2f);
            if (shown != null && shownGun == gun && Showing) shown.Flash();
        }

        private void LateUpdate()
        {
            if (right == null) return;
            float dt = Time.deltaTime;
            bool fp = thirdPerson == null || !thirdPerson.IsThirdPerson;

            // which gun, and how far up: the new one comes up over its draw time; switching to the pickaxe, the old
            // one goes down first
            GunDefinition gun = loadout.CurrentGun;
            float raise;
            if (gun != null)
            {
                raise = loadout.DrawProgress;
            }
            else
            {
                gun = loadout.GunIn(loadout.PreviousSlot);
                raise = 1f - loadout.DrawProgress;
            }
            GunModel m = gun != null && raise > 0f ? ModelFor(gun) : null;
            if (m != shown)
            {
                if (shown != null) shown.SetVisible(false);
                shown = m;
                shownGun = gun;
                if (shown != null) shown.SetVisible(true);
            }
            if (shown == null || !fp)
            {
                Hide();
                if (shooter != null && fp) shooter.SetMuzzle(null, null);
                return;
            }
            Show();
            if (vmCam != null) vmCam.fieldOfView = viewFieldOfView;
            if (armsRoot != null && !Mathf.Approximately(scaleNow, armsScale))
            {
                // size changed in the Inspector while playing
                armsRoot.localScale = Vector3.one * armsScale;
                foreach (KeyValuePair<GunDefinition, GunModel> kv in models)
                    if (kv.Value != null && kv.Key.model != null) kv.Value.transform.localScale = kv.Key.model.transform.localScale * armsScale;
                scaleNow = armsScale;
            }

            // ---- where the gun goes, in camera space
            Vector2 d = (look == null || look.CursorLocked) ? GameInput.LookDelta : Vector2.zero;
            swayNow = Vector2.Lerp(swayNow, Vector2.ClampMagnitude(d, maxSway), 1f - Mathf.Exp(-9f * dt));
            float speed = motor != null ? motor.Speed : 0f;
            bool grounded = motor == null || motor.IsGrounded;
            bobWeight = Mathf.MoveTowards(bobWeight, grounded && !(motor != null && motor.IsSliding) ? Mathf.Clamp01(speed / 6f) : 0f, 4f * dt);
            bobPhase += speed * bobRate * dt * Mathf.PI * 2f;
            kickBack = Mathf.Lerp(kickBack, 0f, 1f - Mathf.Exp(-16f * dt));
            kickUp = Mathf.Lerp(kickUp, 0f, 1f - Mathf.Exp(-14f * dt));
            float e = 1f - (1f - raise) * (1f - raise) * (1f - raise);                // ease out

            Vector3 pos = shownGun.viewOffset;
            pos += new Vector3(-swayNow.x, -swayNow.y, 0f) * sway;
            pos += new Vector3(Mathf.Sin(bobPhase) * bob * 0.7f, -Mathf.Abs(Mathf.Cos(bobPhase)) * bob, 0f) * bobWeight;
            pos += new Vector3(0f, kickBack * 0.25f, -kickBack);
            pos += new Vector3(0f, -drawDrop * (1f - e), -0.05f * (1f - e));
            Quaternion rot = Quaternion.Euler(shownGun.viewEuler) *
                             Quaternion.Euler(-kickUp + 32f * (1f - e) + swayNow.y * swayTurn,
                                              -swayNow.x * swayTurn, 8f * (1f - e) - swayNow.x * swayTurn * 0.6f);

            Transform ct = cam.transform;
            shown.transform.rotation = shown.RootFor(ct.rotation * rot);
            shown.transform.position += ct.TransformPoint(pos) - shown.grip.position;

            // ---- arms reach for it
            float trig = shooter != null ? shooter.TriggerAmount : 0f;
            right.ResetPose();
            Vector3 hp;
            Quaternion hr;
            right.HandForGrip(shown.grip.position, GunModel.HandOn(shown.grip), out hp, out hr);
            PlaceShoulder(right, rightShoulder, hp);
            right.Solve(hp, hr, ct.TransformDirection(rightElbow), 1f);
            Vector2 finger = Vector2.Lerp(shownGun.fingerHold, shownGun.fingerPull, trig);
            right.SetFinger(finger.x, finger.y, 1f);
            shown.SetTrigger(trig, shownGun.triggerTravel);

            bool twoHands = shownGun.hands == GunDefinition.Hands.Two && shown.foregrip != null;
            bool throwing = bodyDriver != null && bodyDriver.ThrowActive;
            leftHold = Mathf.MoveTowards(leftHold, twoHands && !throwing ? 1f : 0f, dt / 0.08f);
            if (leftHold > 0.01f)
            {
                left.upper.localScale = leftScale;
                left.ResetPose();
                Vector3 lp;
                Quaternion lr;
                left.HandForGrip(shown.foregrip.position, GunModel.HandOn(shown.foregrip), out lp, out lr);
                // letting go (a throw): the hand drops away below the view
                lp += ct.up * (-0.45f * (1f - leftHold)) + ct.forward * (-0.15f * (1f - leftHold));
                PlaceShoulder(left, leftShoulder, lp);
                left.Solve(lp, lr, ct.TransformDirection(leftElbow), 1f);
                left.SetFinger(0f, 0f, 1f);
            }
            else
            {
                left.upper.localScale = leftScale * 0.0001f;
            }
            if (shooter != null) shooter.SetMuzzle(shown.muzzle, vmCam);
        }

        /// <summary>Put the arm's shoulder joint at its spot beside the camera, moved closer if the hand can't reach.</summary>
        private void PlaceShoulder(ArmChain arm, Vector3 local, Vector3 handTarget)
        {
            Vector3 s = cam.transform.TransformPoint(local);
            float reach = arm.Reach * 0.93f;
            Vector3 toHand = handTarget - s;
            if (toHand.magnitude > reach) s = handTarget - toHand.normalized * reach;
            arm.upper.position = s;
        }

        private void Show()
        {
            if (Showing) return;
            Showing = true;
            if (arms != null) arms.enabled = true;
            if (shown != null) shown.SetVisible(true);
        }

        private void Hide()
        {
            Showing = false;
            if (arms != null && arms.enabled) arms.enabled = false;
            foreach (GunModel m in models.Values)
                if (m != null && m.Renderers != null && m.Renderers.Length > 0 && m.Renderers[0].enabled) m.SetVisible(false);
        }
    }
}
