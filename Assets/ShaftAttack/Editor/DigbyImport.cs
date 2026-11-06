#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>Runs the Digby build when Assets/ShaftAttack/_ClaudeShots~/digby_build.txt appears
    /// (lets tools outside Unity ask for a rebuild). digby_animator.txt also rebuilds Digby.controller.</summary>
    [InitializeOnLoad]
    internal static class DigbyBuildRequest
    {
        static DigbyBuildRequest()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;   // works in Play mode too
            string dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
            if (File.Exists(Path.Combine(dir, "cleanup.txt")))         // remote tools are done: tidy up
            {
                try { Directory.Delete(dir, true); } catch (System.Exception) { }
                return;
            }
            string request = Path.Combine(dir, "digby_build.txt");
            string animRequest = Path.Combine(dir, "digby_animator.txt");
            bool anim = File.Exists(animRequest);
            if (!anim && !File.Exists(request)) return;
            if (anim) File.Delete(animRequest);
            if (File.Exists(request)) File.Delete(request);
            DigbyImport.SafeBuild(true, anim);
        }
    }

    /// <summary>
    /// Import settings and setup for Digby, the default miner (Assets/ShaftAttack/Characters/Digby).
    /// Digby.fbx is rigged (Generic) with the clips Idle, Walk, Run, Jump, JumpStart, JumpAir, JumpLand.
    /// Builds the two URP Lit materials (Digby_Base, and Digby_Team for the helmet), Digby.controller
    /// (only if it doesn't exist yet, so hand edits survive) and Digby.prefab whenever the files are
    /// imported. "Shaft Attack/Rebuild Digby" redoes it by hand; "Rebuild Digby Animator" also remakes
    /// the controller from scratch.
    /// </summary>
    public class DigbyImport : AssetPostprocessor
    {
        public const string Dir = "Assets/ShaftAttack/Characters/Digby";
        public const string ControllerPath = Dir + "/Digby.controller";
        public static readonly Color DefaultTeam = new Color(0.965f, 0.675f, 0.071f);   // yellow
        private const float EmissionStrength = 6f;                                       // baked as strength / 6
        public const float WalkSpeed = 0.499f;     // ground speed of the clips, m/s (Blender export)
        public const float RunSpeed = 1.823f;
        private static readonly string[] Loops = { "Idle", "Walk", "Run", "JumpAir" };

        private bool Mine
        {
            get { return assetPath.Replace('\\', '/').StartsWith(Dir + "/"); }
        }

        private void OnPreprocessTexture()
        {
            if (!Mine) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.EndsWith("_Normal");
            bool data = normal || file.EndsWith("_MetallicSmoothness");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !data;
            ti.alphaSource = file.EndsWith("_MetallicSmoothness")
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        private void OnPreprocessModel()
        {
            if (!Mine) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importConstraints = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.weldVertices = true;
            // rig + animations: Generic, so the clips play exactly as made in Blender, on the spot
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;
            mi.motionNodeName = "";
            mi.importAnimation = true;
            mi.importAnimatedCustomProperties = false;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.animationRotationError = 0.2f;
            mi.animationPositionError = 0.2f;
            mi.animationScaleError = 0.5f;
        }

        private void OnPreprocessAnimation()
        {
            if (!Mine) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                string take = clips[i].takeName;
                string clipName = take.Substring(take.LastIndexOf('|') + 1);   // "Digby_Rig|Walk" -> "Walk"
                clips[i].name = clipName;
                clips[i].loopTime = System.Array.IndexOf(Loops, clipName) >= 0;
                clips[i].loopPose = false;
            }
            mi.clipAnimations = clips;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string p in imported)
            {
                string q = p.Replace('\\', '/');
                if (q.StartsWith(Dir + "/") && (q.EndsWith(".fbx") || q.EndsWith(".png")))
                {
                    EditorApplication.delayCall -= BuildQuiet;
                    EditorApplication.delayCall += BuildQuiet;
                    return;
                }
            }
        }

        private static void BuildQuiet()
        {
            SafeBuild(false, false);
        }

        public static void SafeBuild(bool verbose)
        {
            SafeBuild(verbose, false);
        }

        /// <summary>Build, and write what happened to Assets/ShaftAttack/_ClaudeShots~/digby_build_log.txt.</summary>
        public static void SafeBuild(bool verbose, bool rebuildAnimator)
        {
            string report;
            try
            {
                report = Build(verbose, rebuildAnimator);
            }
            catch (System.Exception e)
            {
                report = "EXCEPTION " + e;
                Debug.LogException(e);
            }
            try
            {
                string dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
                if (!Directory.Exists(dir)) return;                 // only when a remote rebuild asked for it
                File.AppendAllText(Path.Combine(dir, "digby_build_log.txt"),
                    System.DateTime.Now.ToString("HH:mm:ss") + " " + report + "\n");
            }
            catch (System.Exception)
            {
            }
        }

        [MenuItem("Shaft Attack/Rebuild Digby")]
        private static void BuildFromMenu()
        {
            SafeBuild(true, false);
        }

        [MenuItem("Shaft Attack/Rebuild Digby Animator")]
        private static void BuildAnimatorFromMenu()
        {
            SafeBuild(true, true);
        }

        public static string Build(bool verbose, bool rebuildAnimator)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Digby.fbx");
            Texture2D baseMap = Tex("Digby_BaseColor");
            Texture2D normal = Tex("Digby_Normal");
            Texture2D metal = Tex("Digby_MetallicSmoothness");
            Texture2D glow = Tex("Digby_Emission");
            if (model == null || baseMap == null || normal == null || metal == null || glow == null)
            {
                if (verbose) Debug.LogWarning("[Digby] files missing in " + Dir);
                return "files missing";
            }

            Material body = MakeMaterial("Digby_Base", Color.white, baseMap, normal, metal, glow);
            Material team = MakeMaterial("Digby_Team", DefaultTeam, baseMap, normal, metal, glow);
            Dictionary<string, AnimationClip> clips = LoadClips();
            AnimatorController controller = MakeController(clips, rebuildAnimator);

            GameObject root = new GameObject("Digby");
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            // turn him so the prefab's forward (+Z) is his face: his left hand must end up on -X
            float yaw = 180f;
            Transform leftHand = FindDeep(inst.transform, "LeftHand");
            if (leftHand != null) yaw = inst.transform.InverseTransformPoint(leftHand.position).x < 0f ? 0f : 180f;
            inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Renderer r = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (r == null) r = inst.GetComponentInChildren<MeshRenderer>();
            r.sharedMaterials = new[] { body, team };
            SkinnedMeshRenderer skin = r as SkinnedMeshRenderer;
            if (skin != null)
            {
                Bounds b = skin.localBounds;                  // room for the jump and swinging arms
                b.Expand(new Vector3(0.8f, 1.2f, 0.8f));
                skin.localBounds = b;
            }
            Animator animator = inst.GetComponent<Animator>();
            if (animator == null && controller != null) animator = inst.AddComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            root.AddComponent<MinerTeamColour>();
            root.AddComponent<SnapToGround>();
            if (controller != null) root.AddComponent<DigbyAnimDriver>();
            Mesh mesh = skin != null ? skin.sharedMesh : null;
            if (mesh == null)
            {
                MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            string report = "built: " + (mesh != null ? mesh.triangles.Length / 3 + " triangles, " + mesh.subMeshCount +
                            " material slots, " : "no mesh?, ") + (skin != null ? skin.bones.Length + " bones, " : "not skinned, ") +
                            clips.Count + " clips (" + string.Join(" ", new List<string>(clips.Keys).ToArray()) + "), model yaw " + yaw +
                            ", controller " + (controller != null ? AssetDatabase.GetAssetPath(controller) : "none");
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "/Digby.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[Digby] " + report);
            return report;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(Dir + "/Digby.fbx"))
            {
                AnimationClip c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) clips[c.name] = c;
            }
            return clips;
        }

        /// <summary>
        /// Digby.controller. Parameters: Speed (m/s), MoveRate (walk/run playback speed), Grounded, Jump (trigger).
        /// Locomotion = 1D blend tree Idle / Walk / Run by Speed. Jump: JumpStart -> JumpAir (loop) -> JumpLand.
        /// "Jump" is the full show-off jump with the wind-up (played from code, e.g. DigbyShowcase).
        /// </summary>
        private static AnimatorController MakeController(Dictionary<string, AnimationClip> clips, bool rebuild)
        {
            string[] needed = { "Idle", "Walk", "Run", "Jump", "JumpStart", "JumpAir", "JumpLand" };
            foreach (string n in needed)
                if (!clips.ContainsKey(n)) return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !rebuild) return existing;
            if (existing != null) AssetDatabase.DeleteAsset(ControllerPath);

            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveRate", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            AnimatorControllerParameter[] ps = ac.parameters;
            foreach (AnimatorControllerParameter p in ps)
            {
                if (p.name == "MoveRate") p.defaultFloat = 1f;
                if (p.name == "Grounded") p.defaultBool = true;
            }
            ac.parameters = ps;

            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            BlendTree tree;
            AnimatorState loco = ac.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], WalkSpeed);
            tree.AddChild(clips["Run"], RunSpeed);
            loco.speedParameterActive = true;
            loco.speedParameter = "MoveRate";
            sm.defaultState = loco;

            AnimatorState start = sm.AddState("JumpStart");
            start.motion = clips["JumpStart"];
            AnimatorState air = sm.AddState("JumpAir");
            air.motion = clips["JumpAir"];
            AnimatorState land = sm.AddState("JumpLand");
            land.motion = clips["JumpLand"];
            AnimatorState show = sm.AddState("Jump");
            show.motion = clips["Jump"];

            AnimatorStateTransition t = sm.AddAnyStateTransition(start);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            Timing(t, false, 0f, 0.05f);
            t.canTransitionToSelf = false;

            t = start.AddTransition(air);
            Timing(t, true, 0.9f, 0.08f);
            t = start.AddTransition(land);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            Timing(t, true, 0.7f, 0.05f);

            t = loco.AddTransition(air);                       // walked off a ledge
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            Timing(t, false, 0f, 0.15f);

            t = air.AddTransition(land);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            Timing(t, false, 0f, 0.04f);

            t = land.AddTransition(loco);                      // landed running: get going again sooner
            t.AddCondition(AnimatorConditionMode.Greater, 1.2f, "Speed");
            Timing(t, true, 0.3f, 0.18f);
            t = land.AddTransition(loco);
            Timing(t, true, 0.85f, 0.2f);

            t = show.AddTransition(loco);
            Timing(t, true, 0.97f, 0.1f);

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        private static void Timing(AnimatorStateTransition t, bool exitTime, float exit, float seconds)
        {
            t.hasExitTime = exitTime;
            t.exitTime = exit;
            t.hasFixedDuration = true;
            t.duration = seconds;
        }

        private static Texture2D Tex(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + name + ".png");
        }

        private static Material MakeMaterial(string name, Color baseColor, Texture2D baseMap, Texture2D normal,
            Texture2D metal, Texture2D glow)
        {
            string path = Dir + "/" + name + ".mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = lit;
            }
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", baseColor);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", metal);
            m.SetFloat("_Smoothness", 1f);                 // with the map assigned this scales the map's alpha
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the metallic map's alpha
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_EmissionMap", glow);
            m.SetVector("_EmissionColor", new Vector4(EmissionStrength, EmissionStrength, EmissionStrength, 1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
#endif
