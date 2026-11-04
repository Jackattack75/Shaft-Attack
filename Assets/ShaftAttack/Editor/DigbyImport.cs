#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>
    /// Import settings and setup for Digby, the default miner (Assets/ShaftAttack/Characters/Digby).
    /// Texture types, model options, the two URP Lit materials (Digby_Base, and Digby_Team for the helmet)
    /// and Digby.prefab are (re)built automatically whenever the files are imported.
    /// "Shaft Attack/Rebuild Digby" redoes it by hand.
    /// </summary>
    /// <summary>Runs the Digby build when Assets/ShaftAttack/_ClaudeShots~/digby_build.txt appears
    /// (lets tools outside Unity ask for a rebuild).</summary>
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
            if (!File.Exists(request)) return;
            File.Delete(request);
            DigbyImport.SafeBuild(true);
        }
    }

    public class DigbyImport : AssetPostprocessor
    {
        public const string Dir = "Assets/ShaftAttack/Characters/Digby";
        public static readonly Color DefaultTeam = new Color(0.965f, 0.675f, 0.071f);   // yellow
        private const float EmissionStrength = 6f;                                       // baked as strength / 6

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
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.weldVertices = true;
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
            SafeBuild(false);
        }

        /// <summary>Build, and write what happened to Assets/ShaftAttack/_ClaudeShots~/digby_build_log.txt.</summary>
        public static void SafeBuild(bool verbose)
        {
            string report;
            try
            {
                report = Build(verbose) ? "built" : "files missing";
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
            SafeBuild(true);
        }

        public static bool Build(bool verbose)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Digby.fbx");
            Texture2D baseMap = Tex("Digby_BaseColor");
            Texture2D normal = Tex("Digby_Normal");
            Texture2D metal = Tex("Digby_MetallicSmoothness");
            Texture2D glow = Tex("Digby_Emission");
            if (model == null || baseMap == null || normal == null || metal == null || glow == null)
            {
                if (verbose) Debug.LogWarning("[Digby] files missing in " + Dir);
                return false;
            }

            Material body = MakeMaterial("Digby_Base", Color.white, baseMap, normal, metal, glow);
            Material team = MakeMaterial("Digby_Team", DefaultTeam, baseMap, normal, metal, glow);

            GameObject root = new GameObject("Digby");
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            // the FBX (Blender front = -Y) arrives facing -Z; turn it so the prefab's forward (+Z) is his face
            inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            MeshRenderer r = inst.GetComponentInChildren<MeshRenderer>();
            r.sharedMaterials = new[] { body, team };
            root.AddComponent<MinerTeamColour>();
            root.AddComponent<SnapToGround>();
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "/Digby.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
            Debug.Log("[Digby] prefab built: " + (mf && mf.sharedMesh ? mf.sharedMesh.triangles.Length / 3 + " triangles, " +
                      mf.sharedMesh.subMeshCount + " material slots" : "no mesh?"));
            return true;
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
