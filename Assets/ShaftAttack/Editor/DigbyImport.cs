#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>Runs the Digby build when Assets/ShaftAttack/_ClaudeShots~/digby_build.txt appears
    /// (lets tools outside Unity ask for a rebuild). digby_animator.txt also rebuilds Digby.controller;
    /// digby_player.txt (re)builds Digby's first-person body on Player.prefab.</summary>
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
            string playerRequest = Path.Combine(dir, "digby_player.txt");
            if (File.Exists(playerRequest))
            {
                File.Delete(playerRequest);
                DigbyImport.SafeBuildPlayerBody();
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
    /// Digby.fbx is rigged (Generic) with the clips Idle, Walk, Run, Jump, JumpStart, JumpRise / JumpAir / JumpFall,
    /// JumpLand, WallJumpL / WallJumpR and the player's Sprint (10 m/s), Slide, Dash, Slam, SlamLand, PickHit, PickMiss,
    /// Throw; bones Eye and SlideCam mark where the first-person camera goes; his pickaxe (PickaxeMesh) rides on the
    /// Pickaxe bone in his right fist and is stowed on the PickaxeBack bone by DigbyPickaxe.
    /// Builds the two URP Lit materials (Digby_Base, and Digby_Team for the helmet), Digby.controller
    /// (only if it doesn't exist yet, so hand edits survive) and Digby.prefab whenever the files are
    /// imported. "Shaft Attack/Rebuild Digby" redoes it by hand; "Rebuild Digby Animator" also remakes
    /// the controller from scratch.
    /// </summary>
    public class DigbyImport : AssetPostprocessor
    {
        public const string Dir = "Assets/ShaftAttack/Characters/Digby";
        public const string ControllerPath = Dir + "/Digby.controller";
        /// <summary>URP Lit with near lights capped, written by DigbyLitShader (his own headlamp no longer burns his
        /// arms and pickaxe out to white).</summary>
        public const string LitShaderPath = Dir + "/DigbyLit.shader";
        public static readonly Color DefaultTeam = new Color(0.965f, 0.675f, 0.071f);   // yellow
        private const float EmissionStrength = 6f;                                       // baked as strength / 6
        public const float WalkSpeed = 0.499f;     // ground speed of the clips, m/s (Blender export)
        public const float RunSpeed = 3.375f;     // v2 (11-06): heavy sprint
        public const float SprintSpeed = 10f;     // v4 (11-09): the player's run speed (PlayerMotor.runSpeed)
        public const float PlayerBodyScale = 1.125f;   // his eyes (1.44 m) at the player's camera (1.62 m)
        public const string PlayerPrefabPath = "Assets/ShaftAttack/Prefabs/Player.prefab";
        private static readonly string[] Loops = { "Idle", "Walk", "Run", "JumpAir", "JumpRise", "JumpFall", "Sprint", "Slide", "Slam" };
        // bones each arm layer of the animator may move (v5): the pickaxe swing and the bomb throw play over the legs
        private static readonly string[] RightArmBones = { "Chest", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
                                                           "Half_Shoulder.R", "Half_Elbow.R", "Pickaxe" };
        private static readonly string[] LeftArmBones = { "Chest", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
                                                          "Half_Shoulder.L", "Half_Elbow.L" };

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
            mi.isReadable = true;            // FirstPersonBody copies the mesh at runtime (head hidden in first person)
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

        [MenuItem("Shaft Attack/Put Digby Body On Player")]
        private static void BuildPlayerBodyFromMenu()
        {
            SafeBuildPlayerBody();
        }

        public static void SafeBuildPlayerBody()
        {
            string report;
            try
            {
                report = BuildPlayerBody();
            }
            catch (System.Exception e)
            {
                report = "EXCEPTION " + e;
                Debug.LogException(e);
            }
            Debug.Log("[Digby] player body: " + report);
            try
            {
                string dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
                if (Directory.Exists(dir))
                    File.AppendAllText(Path.Combine(dir, "digby_build_log.txt"),
                        System.DateTime.Now.ToString("HH:mm:ss") + " player body: " + report + "\n");
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>
        /// Gives Player.prefab Digby's body for first person: child "DigbyBody" (scaled so his eyes are at the
        /// camera) holding the rigged model with Digby.controller, plus DigbyAnimDriver (fed by PlayerMotor, legs
        /// turn toward strafes, plays backwards when backpedalling), FirstPersonBody (head hidden in your own view,
        /// whole body in your shadow, body kept under the camera) and MinerTeamColour. Replaces an older DigbyBody.
        /// Also puts ThirdPersonView on the Player (V switches to a third-person test view).
        /// </summary>
        public static string BuildPlayerBody()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Digby.fbx");
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Material body = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Digby_Base.mat");
            Material team = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Digby_Team.mat");
            if (model == null || controller == null || body == null || team == null) return "Digby files missing";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null) return "no " + PlayerPrefabPath;

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform old = root.transform.Find("DigbyBody");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                GameObject bodyGo = new GameObject("DigbyBody");
                bodyGo.transform.SetParent(root.transform, false);
                bodyGo.transform.localScale = Vector3.one * PlayerBodyScale;

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model, bodyGo.transform);
                inst.name = "Model";
                float yaw = 180f;
                Transform leftHand = FindDeep(inst.transform, "LeftHand");
                if (leftHand != null) yaw = inst.transform.InverseTransformPoint(leftHand.position).x < 0f ? 0f : 180f;
                inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                SkinnedMeshRenderer skin = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null) return "model not skinned";
                skin.sharedMaterials = new[] { body, team };
                Bounds b = skin.localBounds;
                b.Expand(new Vector3(0.8f, 1.2f, 0.8f));
                skin.localBounds = b;
                Animator animator = inst.GetComponent<Animator>();
                if (animator == null) animator = inst.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                bool hasPick = DressPickaxe(inst);
                bodyGo.AddComponent<MinerTeamColour>();
                DigbyAnimDriver driver = bodyGo.AddComponent<DigbyAnimDriver>();
                driver.legsFollowMovement = true;
                if (hasPick) bodyGo.AddComponent<DigbyPickaxe>();
                FirstPersonBody fp = bodyGo.AddComponent<FirstPersonBody>();
                Transform eyeBone = FindDeep(inst.transform, "Eye");
                Transform headBone = FindDeep(inst.transform, "Head");
                if (eyeBone != null && headBone != null)        // rest pose here: how far his eyes are in front of his neck
                    fp.restEyeAhead = Vector3.Dot(eyeBone.position - headBone.position, bodyGo.transform.forward);
                if (root.GetComponent<ThirdPersonView>() == null) root.AddComponent<ThirdPersonView>();   // V: test view
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return "DigbyBody on Player.prefab: scale " + PlayerBodyScale + ", model yaw " + yaw + ", " +
                       skin.bones.Length + " bones, controller " + controller.name + ", eyes " + fp.restEyeAhead.ToString("0.000") +
                       " m ahead of the neck" + (hasPick ? ", pickaxe" : ", NO pickaxe");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
            MakePickaxeMaterials();
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
            bool hasPick = DressPickaxe(inst);
            root.AddComponent<MinerTeamColour>();
            root.AddComponent<SnapToGround>();
            if (controller != null) root.AddComponent<DigbyAnimDriver>();
            if (hasPick) root.AddComponent<DigbyPickaxe>();
            Mesh mesh = skin != null ? skin.sharedMesh : null;
            if (mesh == null)
            {
                MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            string report = "built: " + (mesh != null ? mesh.triangles.Length / 3 + " triangles, " + mesh.subMeshCount +
                            " material slots, " : "no mesh?, ") + (skin != null ? skin.bones.Length + " bones, " : "not skinned, ") +
                            clips.Count + " clips (" + string.Join(" ", new List<string>(clips.Keys).ToArray()) + "), model yaw " + yaw +
                            ", controller " + (controller != null ? AssetDatabase.GetAssetPath(controller) : "none") +
                            (controller != null ? " (" + controller.layers.Length + " layers, " + controller.parameters.Length + " parameters)" : "") +
                            (hasPick ? ", pickaxe" : ", NO pickaxe");
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "/Digby.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[Digby] " + report);
            return report;
        }

        /// <summary>Gives the pickaxe that came in with the model (PickaxeMesh, on the Pickaxe bone) its materials.</summary>
        private static bool DressPickaxe(GameObject modelInstance)
        {
            Transform pick = FindDeep(modelInstance.transform, "PickaxeMesh");
            MeshRenderer mr = pick != null ? pick.GetComponent<MeshRenderer>() : null;
            if (mr == null) return false;
            Material pb = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Pickaxe_Base.mat");
            Material pt = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Pickaxe_Team.mat");
            if (pb == null || pt == null) return false;
            mr.sharedMaterials = new[] { pb, pt };
            return true;
        }

        /// <summary>Pickaxe_Base (wood, leather, iron) and Pickaxe_Team (the cloth wrap, tinted with the team colour):
        /// Digby's Lit shader, base colour + metallic/smoothness map, no normal map and no glow.</summary>
        private static void MakePickaxeMaterials()
        {
            Texture2D baseMap = Tex("Pickaxe_BaseColor");
            Texture2D metal = Tex("Pickaxe_MetallicSmoothness");
            if (baseMap == null) return;
            foreach (string name in new[] { "Pickaxe_Base", "Pickaxe_Team" })
            {
                string path = Dir + "/" + name + ".mat";
                Shader lit = DigbyLitShader.Get(LitShaderPath);
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
                m.SetColor("_BaseColor", name == "Pickaxe_Team" ? DefaultTeam : Color.white);
                if (metal != null)
                {
                    m.SetTexture("_MetallicGlossMap", metal);
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                m.DisableKeyword("_NORMALMAP");
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m);
            }
        }

        /// <summary>A mask for one animator layer: only the named bones of the model are driven by it.</summary>
        private static AvatarMask MakeMask(string name, GameObject model, string[] bones)
        {
            AvatarMask mask = new AvatarMask();
            mask.name = name;
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(all[i], model.transform));
                mask.SetTransformActive(i, System.Array.IndexOf(bones, all[i].name) >= 0);
            }
            return mask;
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
        /// Digby.controller. Parameters: Speed (m/s), MoveRate (walk/run playback speed), Grounded, Jump (trigger);
        /// with the player's clips also Sliding, Slamming (bools), Dash, SlamLand (triggers), VSpeed (m/s up),
        /// WallJump (trigger), WallSide (-1 = the wall is on his left, +1 = on his right).
        /// Base layer: Locomotion = 1D blend tree Idle / Walk / Run / Sprint by Speed. Jump: JumpStart -> JumpAir ->
        /// JumpLand, where JumpAir is a blend tree by VSpeed (JumpFall / JumpAir / JumpRise). WallJump = blend of
        /// WallJumpL / WallJumpR by WallSide. Slide (while Sliding), Dash (trigger), Slam (while Slamming) -> SlamLand.
        /// "Jump" is the full show-off jump with the wind-up (played from code, e.g. DigbyShowcase).
        /// Layers RightArm (PickHit, PickMiss) and LeftArm (Throw) only move that arm and the chest: DigbyAnimDriver
        /// starts those states from code when the player digs or throws, so they play over running, sliding, jumping.
        /// </summary>
        private static AnimatorController MakeController(Dictionary<string, AnimationClip> clips, bool rebuild)
        {
            string[] needed = { "Idle", "Walk", "Run", "Jump", "JumpStart", "JumpAir", "JumpLand" };
            foreach (string n in needed)
                if (!clips.ContainsKey(n)) return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !rebuild) return existing;

            // Rebuild in place: deleting and re-creating the asset left everything already loaded that uses it (the
            // player's body in the open scene and in Player.prefab) with no controller until Unity reloaded them.
            AnimatorController ac = existing != null ? Wipe(existing)
                                                     : AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveRate", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            bool moves = clips.ContainsKey("Sprint") && clips.ContainsKey("Slide") && clips.ContainsKey("Dash") &&
                         clips.ContainsKey("Slam") && clips.ContainsKey("SlamLand");
            bool airBlend = clips.ContainsKey("JumpRise") && clips.ContainsKey("JumpFall");
            bool wall = clips.ContainsKey("WallJumpL") && clips.ContainsKey("WallJumpR");
            bool arms = clips.ContainsKey("PickHit") && clips.ContainsKey("PickMiss") && clips.ContainsKey("Throw");
            if (moves)
            {
                ac.AddParameter("Sliding", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Slamming", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("SlamLand", AnimatorControllerParameterType.Trigger);
            }
            if (airBlend) ac.AddParameter("VSpeed", AnimatorControllerParameterType.Float);
            if (wall)
            {
                ac.AddParameter("WallJump", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("WallSide", AnimatorControllerParameterType.Float);
            }
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
            if (moves) tree.AddChild(clips["Sprint"], SprintSpeed);
            loco.speedParameterActive = true;
            loco.speedParameter = "MoveRate";
            sm.defaultState = loco;

            AnimatorState start = sm.AddState("JumpStart");
            start.motion = clips["JumpStart"];
            AnimatorState air;
            if (airBlend)
            {
                // going up -> the top -> coming down, by how fast he is moving up (m/s)
                BlendTree airTree;
                air = ac.CreateBlendTreeInController("JumpAir", out airTree, 0);
                airTree.blendType = BlendTreeType.Simple1D;
                airTree.blendParameter = "VSpeed";
                airTree.useAutomaticThresholds = false;
                airTree.AddChild(clips["JumpFall"], -8f);
                airTree.AddChild(clips["JumpAir"], 0f);
                airTree.AddChild(clips["JumpRise"], 7f);
            }
            else
            {
                air = sm.AddState("JumpAir");
                air.motion = clips["JumpAir"];
            }
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

            if (wall)
            {
                // kick off a wall: left / right version blended by which side the wall is on
                BlendTree wallTree;
                AnimatorState wj = ac.CreateBlendTreeInController("WallJump", out wallTree, 0);
                wallTree.blendType = BlendTreeType.Simple1D;
                wallTree.blendParameter = "WallSide";
                wallTree.useAutomaticThresholds = false;
                wallTree.AddChild(clips["WallJumpL"], -1f);
                wallTree.AddChild(clips["WallJumpR"], 1f);
                t = sm.AddAnyStateTransition(wj);
                t.AddCondition(AnimatorConditionMode.If, 0f, "WallJump");
                Timing(t, false, 0f, 0.04f);
                t.canTransitionToSelf = true;
                t = wj.AddTransition(air);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                Timing(t, true, 0.75f, 0.12f);
                t = wj.AddTransition(land);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                Timing(t, true, 0.5f, 0.05f);
            }

            if (moves)
            {
                // the player's moves (PlayerMotor): slide, dash, ground slam
                AnimatorState slide = sm.AddState("Slide");
                slide.motion = clips["Slide"];
                AnimatorState dash = sm.AddState("Dash");
                dash.motion = clips["Dash"];
                AnimatorState slam = sm.AddState("Slam");
                slam.motion = clips["Slam"];
                AnimatorState slamLand = sm.AddState("SlamLand");
                slamLand.motion = clips["SlamLand"];

                t = sm.AddAnyStateTransition(dash);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Dash");
                Timing(t, false, 0f, 0.03f);
                t.canTransitionToSelf = true;
                t = sm.AddAnyStateTransition(slam);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Slamming");
                Timing(t, false, 0f, 0.06f);
                t.canTransitionToSelf = false;
                t = sm.AddAnyStateTransition(slide);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Sliding");
                Timing(t, false, 0f, 0.06f);                    // the camera drops into a slide in ~0.06 s
                t.canTransitionToSelf = false;

                t = slide.AddTransition(loco);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sliding");
                t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                Timing(t, false, 0f, 0.15f);
                t = slide.AddTransition(air);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sliding");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                Timing(t, false, 0f, 0.12f);

                t = dash.AddTransition(loco);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                Timing(t, true, 0.7f, 0.12f);
                t = dash.AddTransition(air);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                Timing(t, true, 0.7f, 0.12f);

                t = slam.AddTransition(slamLand);
                t.AddCondition(AnimatorConditionMode.If, 0f, "SlamLand");
                Timing(t, false, 0f, 0.02f);
                t = slam.AddTransition(slamLand);                  // landed but the trigger was missed
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Slamming");
                t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                Timing(t, false, 0f, 0.03f);
                t = slam.AddTransition(air);                       // slam cancelled in the air
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Slamming");
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                Timing(t, false, 0f, 0.1f);

                t = slamLand.AddTransition(loco);
                t.AddCondition(AnimatorConditionMode.Greater, 1.2f, "Speed");
                Timing(t, true, 0.45f, 0.15f);
                t = slamLand.AddTransition(loco);
                Timing(t, true, 0.9f, 0.2f);
            }

            // Let the AnyState moves (dash, slam, slide, wall jump, jump) cut into a blend that is already under
            // way. By default a transition in progress can't be interrupted, so e.g. a slam started while he was
            // still blending out of a landing never showed its dive.
            foreach (ChildAnimatorState cs in sm.states)
                foreach (AnimatorStateTransition tr in cs.state.transitions)
                    tr.interruptionSource = TransitionInterruptionSource.Destination;

            if (arms)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Digby.fbx");
                AnimatorStateMachine right = AddArmLayer(ac, "RightArm", MakeMask("Digby_RightArm", model, RightArmBones));
                AnimatorState rest = right.defaultState;
                foreach (string clip in new[] { "PickHit", "PickMiss" })
                {
                    AnimatorState st = right.AddState(clip);
                    st.motion = clips[clip];
                    st.writeDefaultValues = false;
                    t = st.AddTransition(rest);                    // done digging: let the arm go back to what it was doing
                    Timing(t, true, 0.9f, 0.12f);
                }
                AnimatorStateMachine left = AddArmLayer(ac, "LeftArm", MakeMask("Digby_LeftArm", model, LeftArmBones));
                AnimatorState thr = left.AddState("Throw");
                thr.motion = clips["Throw"];
                thr.writeDefaultValues = false;
                t = thr.AddTransition(left.defaultState);
                Timing(t, true, 0.8f, 0.15f);
            }

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        /// <summary>An extra animator layer that only moves the bones in `mask`. Its default state "Empty" plays
        /// nothing, so the base layer shows until code starts one of its other states.</summary>
        private static AnimatorStateMachine AddArmLayer(AnimatorController ac, string name, AvatarMask mask)
        {
            mask.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(mask, ac);
            ac.AddLayer(name);
            AnimatorControllerLayer[] layers = ac.layers;
            int i = layers.Length - 1;
            layers[i].avatarMask = mask;
            layers[i].defaultWeight = 1f;
            layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;
            AnimatorStateMachine sm = ac.layers[i].stateMachine;
            AnimatorState empty = sm.AddState("Empty");
            empty.writeDefaultValues = false;
            sm.defaultState = empty;
            return sm;
        }

        /// <summary>Empties a controller (layers, parameters, states, blend trees, transitions) but keeps the asset
        /// itself, so every reference to it stays good; then gives it an empty Base Layer like a new controller.</summary>
        private static AnimatorController Wipe(AnimatorController ac)
        {
            ac.layers = new AnimatorControllerLayer[0];
            ac.parameters = new AnimatorControllerParameter[0];
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != null && sub != ac) Object.DestroyImmediate(sub, true);
            ac.AddLayer("Base Layer");
            EditorUtility.SetDirty(ac);
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
            Shader lit = DigbyLitShader.Get(LitShaderPath);
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

