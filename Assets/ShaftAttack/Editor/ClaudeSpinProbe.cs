// TEMPORARY - written by Claude to film a thrown bomb's spin in Play mode. Safe to delete.
// It does nothing unless Assets/ShaftAttack/_ClaudeShots~/request.txt exists. Unity ignores
// folders whose names end in "~", so nothing written there is imported into the project.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShaftAttack.ClaudeProbe
{
    [InitializeOnLoad]
    internal static class ClaudeSpinProbe
    {
        private const int Size = 360;
        private const int Frames = 20;
        private const float FrameStep = 0.025f;     // game seconds between frames
        private const float SlowMotion = 0.2f;

        private static int step;
        private static double stepAt;
        private static float nextShotTime;
        private static int frame;
        private static int renderFrameAfter = -1;
        private static Transform bomb;
        private static Rigidbody bombBody;
        private static Camera cam;
        private static RenderTexture rt;
        private static readonly StringBuilder log = new StringBuilder();

        static ClaudeSpinProbe()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            try
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Directory.CreateDirectory(Dir);
                    Type tools = FindType("ShaftAttack.MinerTools");
                    FieldInfo spin = tools == null ? null : tools.GetField("bombSpin");
                    File.WriteAllText(Path.Combine(Dir, "loaded.txt"), DateTime.Now.ToString("HH:mm:ss")
                        + "\nMinerTools.bombSpin field: " + (spin != null));
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

                double now = EditorApplication.timeSinceStartup;
                switch (step)
                {
                    case 0:
                        string request = Path.Combine(dir, "request.txt");
                        if (!File.Exists(request)) return;
                        File.Delete(request);
                        log.Length = 0;
                        Log("Play mode detected at " + DateTime.Now.ToString("HH:mm:ss"));
                        step = 1;
                        stepAt = now;
                        break;

                    case 1:
                        if (now - stepAt < 3.0) return;
                        ThrowTestBomb();
                        step = 2;
                        break;

                    case 2:
                        if (bomb == null)
                        {
                            Log("Bomb gone after " + frame + " frames");
                            Finish();
                            return;
                        }
                        if (renderFrameAfter >= 0)
                        {
                            if (Time.frameCount <= renderFrameAfter) return;
                            Save();
                            renderFrameAfter = -1;
                            frame++;
                            if (frame >= Frames)
                            {
                                Log("DONE");
                                Finish();
                            }
                            return;
                        }
                        if (Time.time >= nextShotTime)
                        {
                            Vector3 p = bombBody != null ? bombBody.worldCenterOfMass : bomb.position;
                            cam.transform.position = p + new Vector3(7f, 0.15f, 0f);
                            cam.transform.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);
                            renderFrameAfter = Time.frameCount + 1;
                            nextShotTime += FrameStep;
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

        private static void ThrowTestBomb()
        {
            Type toolsType = FindType("ShaftAttack.MinerTools");
            Type bombType = FindType("ShaftAttack.Bomb");
            if (toolsType == null || bombType == null) throw new Exception("MinerTools/Bomb type not found");
            Object[] all = Object.FindObjectsByType(toolsType, FindObjectsSortMode.None);
            if (all.Length == 0) throw new Exception("No MinerTools in the scene");
            Component tools = (Component)all[0];

            FieldInfo spinField = toolsType.GetField("bombSpin");
            FieldInfo aimField = toolsType.GetField("aim");
            FieldInfo prefabField = toolsType.GetField("bombPrefab");
            Transform aim = aimField != null ? aimField.GetValue(tools) as Transform : null;
            Object prefab = prefabField != null ? prefabField.GetValue(tools) as Object : null;
            Log("bombSpin " + (spinField != null ? spinField.GetValue(tools) : "missing")
                + ", aim right " + (aim != null ? aim.right.ToString("F2") : "none")
                + ", prefab " + (prefab != null ? prefab.name : "placeholder"));

            Time.timeScale = SlowMotion;
            MethodInfo spawn = bombType.GetMethod("Spawn", BindingFlags.Public | BindingFlags.Static);
            Vector3 start = new Vector3(32f, 31f, 18f);
            Vector3 velocity = new Vector3(0f, 7f, 12f);
            Component b = spawn.Invoke(null, new object[] { start, velocity, tools, null }) as Component;
            if (b == null) throw new Exception("Spawn returned nothing");
            bomb = b.transform;
            bombBody = b.GetComponent<Rigidbody>();
            Log("Spawned at " + start + " velocity " + velocity + "; angular velocity "
                + (bombBody != null ? bombBody.angularVelocity.ToString("F2") : "?")
                + ", max " + (bombBody != null ? bombBody.maxAngularVelocity.ToString("F1") : "?")
                + ", damping " + (bombBody != null ? bombBody.angularDamping.ToString("F2") : "?")
                + ", start up " + bomb.up.ToString("F2") + ", forward " + bomb.forward.ToString("F2"));

            Renderer[] rends = bomb.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds bb = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
                Log("Pivot " + bomb.position.ToString("F3") + ", centre of mass "
                    + (bombBody != null ? bombBody.worldCenterOfMass.ToString("F3") : "?")
                    + ", visual centre " + bb.center.ToString("F3") + ", visual size " + bb.size.ToString("F3")
                    + ", scale " + bomb.lossyScale.ToString("F3") + ", renderers " + rends.Length);
                foreach (Renderer r in rends)
                    Log("  renderer " + r.name + " bounds " + r.bounds.center.ToString("F3") + " size " + r.bounds.size.ToString("F3"));
            }
            foreach (Collider c in bomb.GetComponentsInChildren<Collider>())
                Log("  collider " + c.GetType().Name + " on " + c.name + " bounds " + c.bounds.center.ToString("F3") + " size " + c.bounds.size.ToString("F3"));

            rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            GameObject go = new GameObject("ClaudeSpinCam");
            cam = go.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.05f;
            cam.targetTexture = rt;
            cam.enabled = true;

            frame = 0;
            nextShotTime = Time.time;
        }

        private static void Save()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            string name = "spin_" + frame.ToString("00") + ".png";
            File.WriteAllBytes(Path.Combine(Dir, name), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log(name + " t=" + Time.time.ToString("F3") + " pos " + bomb.position.ToString("F2")
                + " up " + bomb.up.ToString("F2")
                + " angVel " + (bombBody != null ? bombBody.angularVelocity.ToString("F2") : "?"));
            Flush();
        }

        private static void Finish()
        {
            step = 100;
            Time.timeScale = 1f;
            try
            {
                if (cam != null)
                {
                    cam.enabled = false;
                    cam.targetTexture = null;
                    Object.Destroy(cam.gameObject);
                }
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
            catch (Exception e)
            {
                Log("Cleanup: " + e.Message);
            }
            cam = null;
            rt = null;
            Flush();
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
