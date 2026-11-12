#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>Runs the weapons setup when Assets/ShaftAttack/_ClaudeShots~/weapons_build.txt appears (lets tools outside
    /// Unity ask for it), the same way DigbyBuildRequest does for Digby.</summary>
    [InitializeOnLoad]
    internal static class WeaponBuildRequest
    {
        static WeaponBuildRequest()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string req = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~/weapons_build.txt");
            if (!File.Exists(req)) return;
            File.Delete(req);
            WeaponSetup.SafeBuild();
        }
    }

    /// <summary>
    /// Sets up the guns (Assets/ShaftAttack/Weapons):
    ///   - import settings for Pistol.fbx / Rifle.fbx and the palette textures (Guns_BaseColor, Guns_MetallicSmoothness)
    ///   - materials Gun_Base / Gun_Team (Digby's Lit shader, so his headlamp doesn't burn them white; Team is tinted)
    ///   - the effect materials: Tracer, MuzzleFlash (ShaftAttack/Tracer shader), Impact_Dust, Impact_Chips, Impact_Sparks
    ///   - prefabs Pistol.prefab / Rifle.prefab (model + materials + GunModel)
    ///   - gun assets Pistol.asset / Rifle.asset (made once with starting values; later runs only update the model, so
    ///     your tuning stays)
    ///   - Player.prefab: Loadout (2 = pistol, 3 = rifle), PlayerGun, GunViewmodel, CombatHUD; DigbyGunHold on DigbyBody
    ///   - the "Viewmodel" layer (the first-person gun is drawn by its own camera)
    /// "Shaft Attack/Rebuild Weapons" runs it; "Shaft Attack/Make Test Target" turns the Digby standing in the open scene
    /// into something to shoot (Health + TargetDummy).
    /// </summary>
    public class WeaponSetup : AssetPostprocessor
    {
        public const string Dir = "Assets/ShaftAttack/Weapons";
        private const string TracerShaderPath = "Assets/ShaftAttack/Shaders/Tracer.shader";
        public static readonly string[] Guns = { "Pistol", "Rifle" };

        private bool Mine { get { return assetPath.Replace('\\', '/').StartsWith(Dir + "/"); } }

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
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
        }

        private void OnPreprocessTexture()
        {
            if (!Mine) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            bool data = Path.GetFileNameWithoutExtension(assetPath).EndsWith("_MetallicSmoothness");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !data;
            ti.alphaSource = data ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.mipmapEnabled = false;                 // a palette: mipmaps would blend the colour cells
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }

        [MenuItem("Shaft Attack/Rebuild Weapons")]
        private static void Menu()
        {
            SafeBuild();
        }

        [MenuItem("Shaft Attack/Make Test Target")]
        private static void TargetMenu()
        {
            Debug.Log("[Weapons] " + MakeTestTarget());
        }

        public static void SafeBuild()
        {
            string report;
            try
            {
                report = Build();
            }
            catch (System.Exception e)
            {
                report = "EXCEPTION " + e;
                Debug.LogException(e);
            }
            Debug.Log("[Weapons] " + report);
            try
            {
                string dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
                if (Directory.Exists(dir))
                    File.AppendAllText(Path.Combine(dir, "weapons_log.txt"), System.DateTime.Now.ToString("HH:mm:ss") + " " + report + "\n");
            }
            catch (System.Exception)
            {
            }
        }

        public static string Build()
        {
            string log = "";
            int layer = EnsureLayer("Viewmodel");
            log += "Viewmodel layer " + layer + "; ";

            Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Guns_BaseColor.png");
            Texture2D metal = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Guns_MetallicSmoothness.png");
            if (baseMap == null) return log + "no Guns_BaseColor.png";
            Material gunBase = GunMaterial("Gun_Base", Color.white, baseMap, metal);
            Material gunTeam = GunMaterial("Gun_Team", DigbyImport.DefaultTeam, baseMap, metal);

            Shader tracerShader = AssetDatabase.LoadAssetAtPath<Shader>(TracerShaderPath);
            Material tracer = EffectMaterial("Tracer", tracerShader, m => { m.SetFloat("_Intensity", 3f); m.SetFloat("_Radial", 0f); });
            Material flash = EffectMaterial("MuzzleFlash", tracerShader, m => { m.SetFloat("_Intensity", 4f); m.SetFloat("_Radial", 1f); m.SetFloat("_Softness", 0.9f); });
            Shader pUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Shader pLit = Shader.Find("Universal Render Pipeline/Particles/Lit");
            Material dust = EffectMaterial("Impact_Dust", pUnlit, m => Plain(m, 0f));
            Material chips = EffectMaterial("Impact_Chips", pLit, m => Plain(m, 0f));
            Material sparks = EffectMaterial("Impact_Sparks", pUnlit, m => Plain(m, 0f));

            GunDefinition[] defs = new GunDefinition[Guns.Length];
            for (int i = 0; i < Guns.Length; i++)
            {
                GameObject prefab = GunPrefab(Guns[i], gunBase, gunTeam);
                if (prefab == null) { log += Guns[i] + ": no model; "; continue; }
                defs[i] = GunAsset(Guns[i], prefab);
                log += Guns[i] + " ok; ";
            }
            if (defs[0] == null || defs[1] == null) return log + "guns missing";

            log += Player(defs, tracer, flash, dust, chips, sparks) + "; ";
            log += MakeTestTarget();
            AssetDatabase.SaveAssets();
            return log;
        }

        // ------------------------------------------------------------------ pieces

        private static int EnsureLayer(string name)
        {
            int have = LayerMask.NameToLayer(name);
            if (have >= 0) return have;
            Object[] tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tm == null || tm.Length == 0) return -1;
            SerializedObject so = new SerializedObject(tm[0]);
            SerializedProperty layers = so.FindProperty("layers");
            for (int i = 9; i < layers.arraySize; i++)
            {
                SerializedProperty sp = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(sp.stringValue)) continue;
                sp.stringValue = name;
                so.ApplyModifiedProperties();
                return i;
            }
            return -1;
        }

        private static Material GunMaterial(string name, Color tint, Texture2D baseMap, Texture2D metal)
        {
            string path = Dir + "/" + name + ".mat";
            Shader lit = DigbyLitShader.Get(DigbyImport.LitShaderPath);
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
            m.SetColor("_BaseColor", tint);
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
            return m;
        }

        private static Material EffectMaterial(string name, Shader shader, System.Action<Material> setup)
        {
            if (shader == null) return null;
            string path = Dir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                setup(m);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
                setup(m);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Plain(Material m, float smoothness)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }

        private static GameObject GunPrefab(string name, Material gunBase, Material gunTeam)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/" + name + ".fbx");
            if (model == null) return null;
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                inst.name = name;
                foreach (MeshRenderer mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    int n = mf != null && mf.sharedMesh != null ? Mathf.Max(1, mf.sharedMesh.subMeshCount) : 1;
                    Material[] mats = new Material[n];
                    for (int i = 0; i < n; i++) mats[i] = i == 1 ? gunTeam : gunBase;
                    mr.sharedMaterials = mats;
                }
                if (inst.GetComponent<GunModel>() == null) inst.AddComponent<GunModel>();
                return PrefabUtility.SaveAsPrefabAsset(inst, Dir + "/" + name + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        private static GunDefinition GunAsset(string name, GameObject prefab)
        {
            string path = Dir + "/" + name + ".asset";
            GunDefinition g = AssetDatabase.LoadAssetAtPath<GunDefinition>(path);
            if (g == null)
            {
                g = ScriptableObject.CreateInstance<GunDefinition>();
                if (name == "Pistol") PistolDefaults(g); else RifleDefaults(g);
                AssetDatabase.CreateAsset(g, path);
            }
            g.model = prefab;
            EditorUtility.SetDirty(g);
            return g;
        }

        private static void PistolDefaults(GunDefinition g)
        {
            g.displayName = "Pistol";
            g.hands = GunDefinition.Hands.One;
            g.automatic = false;
            g.fireRate = 6.5f;
            g.damage = 25f;
            g.range = 200f;
            g.spread = 0.2f;
            g.bloomPerShot = 0.45f;
            g.maxBloom = 2f;
            g.bloomRecovery = 7f;
            g.moveSpread = 0.3f;
            g.airSpread = 0.5f;
            g.bulletSpeed = 360f;
            g.tracerColor = new Color(1f, 0.75f, 0.35f);
            g.tracerWidth = 0.035f;
            g.tracerLength = 4.5f;
            g.digRadius = 0.55f;
            g.digInset = 0.1f;
            g.aimClimb = 0.6f;
            g.aimWander = 0.15f;
            g.cameraKick = 1.4f;
            g.viewKickBack = 0.06f;
            g.viewKickUp = 9f;
            g.viewOffset = new Vector3(0.21f, -0.27f, 0.6f);
            g.viewEuler = new Vector3(0f, -5f, 0f);
            g.drawTime = 0.25f;
            g.fingerHold = new Vector2(0f, 8f);
            g.fingerPull = new Vector2(-2f, -22f);
            g.triggerTravel = 16f;
        }

        private static void RifleDefaults(GunDefinition g)
        {
            g.displayName = "Rifle";
            g.hands = GunDefinition.Hands.Two;
            g.automatic = true;
            g.fireRate = 10f;
            g.damage = 14f;
            g.range = 220f;
            g.spread = 0.3f;
            g.bloomPerShot = 0.22f;
            g.maxBloom = 2.6f;
            g.bloomRecovery = 6f;
            g.moveSpread = 0.5f;
            g.airSpread = 0.8f;
            g.bulletSpeed = 520f;
            g.tracerColor = new Color(1f, 0.62f, 0.24f);
            g.tracerWidth = 0.03f;
            g.tracerLength = 6f;
            g.digRadius = 0.45f;
            g.digInset = 0.08f;
            g.aimClimb = 0.32f;
            g.aimWander = 0.14f;
            g.cameraKick = 0.7f;
            g.viewKickBack = 0.035f;
            g.viewKickUp = 3.5f;
            g.viewOffset = new Vector3(0.17f, -0.37f, 0.6f);
            g.viewEuler = new Vector3(0f, -4f, 0f);
            g.drawTime = 0.35f;
            g.fingerHold = new Vector2(0f, 8f);
            g.fingerPull = new Vector2(-2f, -22f);
            g.triggerTravel = 16f;
        }

        private static string Player(GunDefinition[] guns, Material tracer, Material flash, Material dust, Material chips, Material sparks)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DigbyImport.PlayerPrefabPath) == null) return "no Player.prefab";
            GameObject arms = AssetDatabase.LoadAssetAtPath<GameObject>(DigbyImport.Dir + "/Digby.fbx");
            Material body = AssetDatabase.LoadAssetAtPath<Material>(DigbyImport.Dir + "/Digby_Base.mat");
            Material team = AssetDatabase.LoadAssetAtPath<Material>(DigbyImport.Dir + "/Digby_Team.mat");
            GameObject root = PrefabUtility.LoadPrefabContents(DigbyImport.PlayerPrefabPath);
            try
            {
                Loadout lo = Get<Loadout>(root);
                lo.guns = guns;
                PlayerGun pg = Get<PlayerGun>(root);
                pg.tracerMaterial = tracer;
                pg.flashMaterial = flash;
                pg.dustMaterial = dust;
                pg.chipMaterial = chips;
                pg.sparkMaterial = sparks;
                GunViewmodel vm = Get<GunViewmodel>(root);
                vm.armsModel = arms;
                vm.armsMaterials = new[] { body, team };
                Get<CombatHUD>(root);
                Transform bodyT = root.transform.Find("DigbyBody");
                bool hold = false;
                if (bodyT != null)
                {
                    Get<DigbyGunHold>(bodyT.gameObject);
                    hold = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, DigbyImport.PlayerPrefabPath);
                return "Player.prefab: Loadout (" + guns.Length + " guns), PlayerGun, GunViewmodel, CombatHUD" + (hold ? ", DigbyGunHold" : ", NO DigbyBody");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>The Digby standing in the open scene (not the player) gets Health + TargetDummy, and the scene is saved.</summary>
        public static string MakeTestTarget()
        {
            if (EditorApplication.isPlaying) return "not in Play mode";
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (!go.name.StartsWith("Digby") || go.GetComponentInChildren<PlayerMotor>() != null) continue;
                if (go.GetComponent<TargetDummy>() != null) return "test target: " + go.name + " (already)";
                go.AddComponent<TargetDummy>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return "test target: " + go.name + " in " + scene.name + " (saved)";
            }
            return "test target: no Digby in " + scene.name;
        }
    }
}
#endif
