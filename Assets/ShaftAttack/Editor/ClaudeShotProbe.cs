// TEMPORARY - written by Claude to screenshot the rock textures in Play mode. Safe to delete.
// It does nothing unless Assets/ShaftAttack/_ClaudeShots~/request.txt exists. Unity ignores
// folders whose names end in "~", so nothing written there is imported into the project.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShaftAttack.ClaudeProbe
{
    [InitializeOnLoad]
    internal static class ClaudeShotProbe
    {
        private const int W = 1320;
        private const int H = 840;

        private struct View
        {
            public readonly string name;
            public readonly Vector3 eye;
            public readonly Vector3 target;

            public View(string name, Vector3 eye, Vector3 target)
            {
                this.name = name;
                this.eye = eye;
                this.target = target;
            }
        }

        private static readonly View[] Views =
        {
            new View("2_pit_from_rim", new Vector3(9.7f, 32f, 0.8f), new Vector3(9.7f, 15f, 17f)),
            new View("3_wall", new Vector3(10f, 14f, 4f), new Vector3(10f, 15f, 18.6f)),
            new View("4_close", new Vector3(8f, 12.6f, 12f), new Vector3(10.5f, 12f, 19f)),
            new View("5_arena", new Vector3(32f, 9f, 20f), new Vector3(32f, 11f, 44f)),
            new View("6_shadow_side", new Vector3(21f, 31f, 21f), new Vector3(7f, 10f, 7f)),
        };

        private static int step;
        private static double stepAt;
        private static int viewIndex;
        private static int framesAtViews;
        private static double timeAtViews;
        private static Camera shotCam;
        private static RenderTexture shotRT;
        private static Type terrainType;
        private static readonly StringBuilder log = new StringBuilder();

        static ClaudeShotProbe()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            try
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(Path.Combine(Dir, "loaded.txt"), DateTime.Now.ToString("HH:mm:ss") + "\n" + ShaderStatus());
                }
            }
            catch (Exception)
            {
            }
        }

        private static string Dir
        {
            get { return Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~"); }
        }

        private static double Now
        {
            get { return EditorApplication.timeSinceStartup; }
        }

        private static void Log(string s)
        {
            log.AppendLine(s);
        }

        private static void Flush()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "log.txt"), log.ToString());
            }
            catch (Exception)
            {
            }
        }

        private static void Tick()
        {
            try
            {
                string dir = Dir;
                if (File.Exists(Path.Combine(dir, "cleanup.txt")))
                {
                    Directory.Delete(dir, true);
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    if (step > 0 && step < 100)
                    {
                        Log("Play mode ended during step " + step);
                        Flush();
                    }
                    step = 0;
                    return;
                }

                switch (step)
                {
                    case 0:
                        WaitForRequest(dir);
                        break;
                    case 1:
                        WaitForTerrain();
                        break;
                    case 2:
                        if (Now - stepAt > 0.8)
                        {
                            Save("1_player.png");
                            DigPit();
                            step = 3;
                            stepAt = Now;
                        }
                        break;
                    case 3:
                        if (Now - stepAt > 1.0 && Pending() == 0)
                        {
                            viewIndex = 0;
                            framesAtViews = Time.frameCount;
                            timeAtViews = Now;
                            Aim(Views[0]);
                            step = 4;
                            stepAt = Now;
                        }
                        break;
                    case 4:
                        if (Now - stepAt > 0.8)
                        {
                            Save(Views[viewIndex].name + ".png");
                            viewIndex++;
                            if (viewIndex < Views.Length)
                            {
                                Aim(Views[viewIndex]);
                                stepAt = Now;
                            }
                            else
                            {
                                Log("Frames while capturing views: " + (Time.frameCount - framesAtViews) + " in "
                                    + (Now - timeAtViews).ToString("F1") + " s");
                                Describe();
                                Log("DONE");
                                Finish();
                            }
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                EditorApplication.update -= Tick;
                Log("EXCEPTION during step " + step + ": " + e);
                Finish();
            }
        }

        private static void WaitForRequest(string dir)
        {
            string request = Path.Combine(dir, "request.txt");
            if (!File.Exists(request)) return;
            File.Delete(request);
            log.Length = 0;
            Log("Play mode detected at " + DateTime.Now.ToString("HH:mm:ss"));
            step = 1;
            stepAt = Now;
        }

        private static void WaitForTerrain()
        {
            Component terrain = Terrain();
            if (terrain == null)
            {
                if (Now - stepAt > 20.0) throw new Exception("No SmoothTerrain.Instance after 20 s");
                return;
            }
            if (Now - stepAt < 3.0 || Pending() > 0) return;

            Camera main = Camera.main;
            if (main == null)
            {
                foreach (Camera c in Camera.allCameras)
                {
                    if (c.targetTexture == null)
                    {
                        main = c;
                        break;
                    }
                }
            }
            if (main == null) throw new Exception("No camera found");

            Log("Player camera '" + main.name + "' at " + main.transform.position.ToString("F2") + ", euler "
                + main.transform.eulerAngles.ToString("F1") + ", fov " + main.fieldOfView + ", HDR " + main.allowHDR
                + ", near " + main.nearClipPlane + ", far " + main.farClipPlane);

            shotRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            shotRT.antiAliasing = 1;
            shotRT.Create();

            GameObject go = new GameObject("ClaudeShotCam");
            shotCam = go.AddComponent<Camera>();
            shotCam.CopyFrom(main);
            CopyUrpCameraData(main, shotCam);
            shotCam.targetTexture = shotRT;
            shotCam.enabled = true;
            step = 2;
            stepAt = Now;
        }

        private static void CopyUrpCameraData(Camera from, Camera to)
        {
            Component src = from.GetComponent("UniversalAdditionalCameraData");
            if (src == null)
            {
                Log("Player camera has no URP camera data");
                return;
            }
            Type t = src.GetType();
            Component dst = to.GetComponent(t);
            if (dst == null) dst = to.gameObject.AddComponent(t);
            string[] names = { "renderPostProcessing", "antialiasing", "antialiasingQuality", "renderShadows", "dithering", "stopNaN", "volumeLayerMask" };
            foreach (string n in names)
            {
                PropertyInfo p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanRead || !p.CanWrite) continue;
                try
                {
                    p.SetValue(dst, p.GetValue(src, null), null);
                }
                catch (Exception e)
                {
                    Log("Couldn't copy camera setting " + n + ": " + e.Message);
                }
            }
            PropertyInfo post = t.GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);
            Log("Copied URP camera settings (post-processing " + (post != null ? post.GetValue(src, null) : "?") + ")");
        }

        private static void DigPit()
        {
            Component terrain = Terrain();
            if (terrain == null) throw new Exception("Terrain vanished before digging");
            MethodInfo dig = terrain.GetType().GetMethod("Dig", new[] { typeof(Vector3), typeof(float) });
            if (dig == null) throw new Exception("Dig(Vector3, float) not found");

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int blasts = 0;
            long removed = 0;
            float[] levels = { 26.2f, 23.3f, 20.4f, 17.5f, 14.6f, 11.7f, 8.8f, 5.9f, 3.4f };
            foreach (float y in levels)
            {
                for (float x = 5f; x < 15f; x += 3.1f)
                {
                    for (float z = 5f; z < 15f; z += 3.1f)
                    {
                        float jx = Mathf.Sin(x * 1.7f + y) * 0.5f;
                        float jz = Mathf.Cos(z * 1.3f + y) * 0.5f;
                        Vector3 p = new Vector3(x + jx, y + Mathf.Sin(x + z) * 0.3f, z + jz);
                        removed += (int)dig.Invoke(terrain, new object[] { p, 3.6f });
                        blasts++;
                    }
                }
            }

            // Dents blasted into the far (north) wall, plus a pickaxe trench.
            Vector3[] dents = { new Vector3(7f, 20.5f, 17.6f), new Vector3(12.5f, 12.4f, 17.8f), new Vector3(9f, 5.6f, 17.4f) };
            foreach (Vector3 d in dents)
            {
                removed += (int)dig.Invoke(terrain, new object[] { d, 3.6f });
                blasts++;
            }
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = new Vector3(3.5f + i * 0.95f, 12.2f, 17.2f + i * 0.15f);
                removed += (int)dig.Invoke(terrain, new object[] { p, 1.25f });
                blasts++;
            }
            Log("Dug a test pit: " + blasts + " digs, " + removed + " samples removed, " + watch.ElapsedMilliseconds + " ms");
        }

        private static void Aim(View v)
        {
            shotCam.transform.position = v.eye;
            shotCam.transform.rotation = Quaternion.LookRotation(v.target - v.eye, Vector3.up);
            shotCam.fieldOfView = 70f;
            shotCam.nearClipPlane = 0.05f;
            shotCam.farClipPlane = Mathf.Max(shotCam.farClipPlane, 250f);
        }

        private static void Save(string file)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = shotRT;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log("Saved " + file + " from " + shotCam.transform.position.ToString("F1"));
            Flush();
        }

        private static void Describe()
        {
            Log(ShaderStatus());
            Log("Unity " + Application.unityVersion + ", colour space " + QualitySettings.activeColorSpace
                + ", quality level " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Component terrain = Terrain();
            if (terrain == null) return;
            MeshRenderer[] renderers = terrain.GetComponentsInChildren<MeshRenderer>(true);
            long vertices = 0;
            int withMesh = 0;
            HashSet<Material> seen = new HashSet<Material>();
            foreach (MeshRenderer r in renderers)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null)
                {
                    vertices += f.sharedMesh.vertexCount;
                    withMesh++;
                }
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    Log("Material '" + m.name + "': shader " + m.shader.name + " (supported " + m.shader.isSupported + ")"
                        + ", texture " + (tex != null ? tex.name + " " + tex.width + "x" + tex.height : "none")
                        + (m.HasProperty("_TileMeters") ? ", tile " + m.GetFloat("_TileMeters") + " m" : "")
                        + (m.HasProperty("_Sharpness") ? ", sharpness " + m.GetFloat("_Sharpness") : "")
                        + (m.HasProperty("_Strata") ? ", strata " + m.GetFloat("_Strata") : ""));
                }
            }
            Log("Chunk renderers: " + renderers.Length + " (" + withMesh + " with a mesh), " + vertices + " render vertices");
        }

        private static void Finish()
        {
            step = 100;
            try
            {
                if (shotCam != null)
                {
                    shotCam.enabled = false;
                    shotCam.targetTexture = null;
                    Object.Destroy(shotCam.gameObject);
                }
                if (shotRT != null)
                {
                    shotRT.Release();
                    Object.Destroy(shotRT);
                }
            }
            catch (Exception e)
            {
                Log("Cleanup: " + e.Message);
            }
            shotCam = null;
            shotRT = null;
            Flush();
        }

        private static string ShaderStatus()
        {
            try
            {
                Shader rock = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShaftAttack/Shaders/TriplanarRock.shader");
                if (rock == null) return "Rock shader: not found";
                StringBuilder sb = new StringBuilder("Rock shader: supported " + rock.isSupported);
                MethodInfo get = typeof(ShaderUtil).GetMethod("GetShaderMessages", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Shader) }, null);
                Array msgs = get == null ? null : get.Invoke(null, new object[] { rock }) as Array;
                if (msgs == null) return sb.Append(", messages unavailable").ToString();
                sb.Append(", ").Append(msgs.Length).Append(" messages");
                foreach (object m in msgs)
                {
                    Type t = m.GetType();
                    sb.Append("\n  ");
                    foreach (string name in new[] { "severity", "platform", "line", "message", "messageDetails" })
                    {
                        FieldInfo fi = t.GetField(name);
                        PropertyInfo pi = fi == null ? t.GetProperty(name) : null;
                        object v = fi != null ? fi.GetValue(m) : (pi != null ? pi.GetValue(m, null) : null);
                        if (v != null) sb.Append(name).Append('=').Append(v).Append("; ");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "Rock shader status failed: " + ex.Message;
            }
        }

        private static Component Terrain()
        {
            if (terrainType == null) terrainType = FindType("ShaftAttack.SmoothTerrain");
            if (terrainType == null) return null;
            PropertyInfo p = terrainType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (p == null) return null;
            Component c = p.GetValue(null, null) as Component;
            return c != null ? c : null;
        }

        private static int Pending()
        {
            Component t = Terrain();
            if (t == null) return 0;
            PropertyInfo p = t.GetType().GetProperty("PendingRebuilds", BindingFlags.Public | BindingFlags.Instance);
            return p == null ? 0 : (int)p.GetValue(t, null);
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
