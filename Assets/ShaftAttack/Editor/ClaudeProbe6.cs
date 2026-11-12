#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;

namespace ShaftAttack.EditorTools
{
    /// <summary>Temporary (Claude): drives Play mode from _ClaudeShots~/probe.txt and takes pictures / measurements.
    /// Lines: "time action arg...". Delete when done.</summary>
    [InitializeOnLoad]
    internal static class ClaudeProbe6
    {
        private static readonly string Dir = Path.Combine(Application.dataPath, "ShaftAttack/_ClaudeShots~");
        private static List<string[]> steps;
        private static int next;
        private static readonly HashSet<Key> held = new HashSet<Key>();
        private static bool lmb, rmb;
        private static readonly List<Key> tapKeys = new List<Key>();
        private static bool tapL;
        private static int tapFrames;
        private static Camera probeCam;
        private static RenderTexture rt;
        private static StreamWriter log;
        private static int shotNo;
        private static InputSettings.BackgroundBehavior oldBg;
        private static InputSettings.EditorInputBehaviorInPlayMode oldEd;

        static ClaudeProbe6()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= Mode;
            EditorApplication.playModeStateChanged += Mode;
        }

        private static void Mode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingPlayMode) Finish("stopped");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                string info = Path.Combine(Dir, "probe_info.txt");
                if (File.Exists(info)) { File.Delete(info); Info(); }
                return;
            }
            if (steps == null)
            {
                string p = Path.Combine(Dir, "probe.txt");
                if (!File.Exists(p)) return;
                steps = new List<string[]>();
                foreach (string line in File.ReadAllLines(p))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    steps.Add(t.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
                }
                File.Delete(p);
                next = 0;
                shotNo = 0;
                log = new StreamWriter(Path.Combine(Dir, "probe_log.txt"), false);
                oldBg = InputSystem.settings.backgroundBehavior;
                oldEd = InputSystem.settings.editorInputBehaviorInPlayMode;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                Log("start " + steps.Count + " steps");
            }
            PushInput();
            if (next >= steps.Count) { Finish("done"); return; }
            string[] st = steps[next];
            float at;
            if (!float.TryParse(st[0], out at) || Time.timeSinceLevelLoad < at) return;
            next++;
            try { Run(st); }
            catch (System.Exception e) { Log("ERROR " + string.Join(" ", st) + ": " + e); }
        }

        private static void Finish(string why)
        {
            if (steps == null) return;
            steps = null;
            held.Clear();
            lmb = rmb = false;
            try
            {
                InputSystem.settings.backgroundBehavior = oldBg;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEd;
            }
            catch (System.Exception) { }
            Log("end " + why);
            if (log != null) { log.Close(); log = null; }
            File.WriteAllText(Path.Combine(Dir, "probe_done.txt"), why);
        }

        private static void Log(string s)
        {
            string line = Time.timeSinceLevelLoad.ToString("0.000") + " " + s;
            if (log != null) { log.WriteLine(line); log.Flush(); }
        }

        // ------------------------------------------------------------------ input

        private static void PushInput()
        {
            if (Keyboard.current == null || Mouse.current == null) return;
            List<Key> keys = new List<Key>(held);
            bool l = lmb, r = rmb;
            if (tapFrames > 0)
            {
                keys.AddRange(tapKeys);
                l |= tapL;
                tapFrames--;
                if (tapFrames == 0) { tapKeys.Clear(); tapL = false; }
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
            MouseState ms = new MouseState();
            if (l) ms = ms.WithButton(MouseButton.Left);
            if (r) ms = ms.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, ms);
        }

        private static Key KeyOf(string k)
        {
            switch (k)
            {
                case "Ctrl": return Key.LeftCtrl;
                case "Shift": return Key.LeftShift;
                case "Space": return Key.Space;
                case "1": return Key.Digit1;
                case "2": return Key.Digit2;
                case "3": return Key.Digit3;
                default: return (Key)System.Enum.Parse(typeof(Key), k);
            }
        }

        // ------------------------------------------------------------------ actions

        private static void Run(string[] st)
        {
            string a = st[1];
            string arg = st.Length > 2 ? st[2] : "";
            PlayerMotor motor = Object.FindFirstObjectByType<PlayerMotor>();
            switch (a)
            {
                case "keys":
                    held.Clear();
                    lmb = rmb = false;
                    if (arg != "none")
                        foreach (string k in arg.Split(','))
                        {
                            if (k == "LMB") lmb = true;
                            else if (k == "RMB") rmb = true;
                            else held.Add(KeyOf(k));
                        }
                    Log("keys " + arg);
                    break;
                case "tap":
                    foreach (string k in arg.Split(','))
                    {
                        if (k == "LMB") tapL = true;
                        else tapKeys.Add(KeyOf(k));
                    }
                    tapFrames = 2;
                    Log("tap " + arg);
                    break;
                case "timescale":
                    Time.timeScale = float.Parse(arg);
                    Log("timescale " + arg);
                    break;
                case "pitch":
                {
                    PlayerLook look = motor.GetComponent<PlayerLook>();
                    typeof(PlayerLook).GetField("pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(look, float.Parse(arg));
                    look.cameraPivot.localRotation = Quaternion.Euler(float.Parse(arg), 0f, 0f);
                    Log("pitch " + arg);
                    break;
                }
                case "yaw":
                    motor.transform.rotation = Quaternion.Euler(0f, float.Parse(arg), 0f);
                    Log("yaw " + arg);
                    break;
                case "lookat":
                {
                    GameObject t = FindTarget(arg);
                    float h = st.Length > 3 ? float.Parse(st[3]) : 1.1f;
                    Camera cam = motor.cameraPivot.GetComponentInChildren<Camera>();
                    Vector3 to = t.transform.position + Vector3.up * h - cam.transform.position;
                    float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                    motor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    PlayerLook look = motor.GetComponent<PlayerLook>();
                    typeof(PlayerLook).GetField("pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(look, pitch);
                    look.cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Log("lookat " + t.name + " yaw " + yaw.ToString("0.0") + " pitch " + pitch.ToString("0.0") + " dist " + to.magnitude.ToString("0.0"));
                    break;
                }
                case "near":
                {
                    // put the player `dist` metres in front of the target, facing it
                    GameObject t = FindTarget(arg);
                    float dist = st.Length > 3 ? float.Parse(st[3]) : 6f;
                    Vector3 f = t.transform.forward;
                    f.y = 0f;
                    Vector3 p = t.transform.position + f.normalized * dist + Vector3.up * 0.3f;
                    motor.Teleport(p);
                    motor.transform.rotation = Quaternion.LookRotation(-f.normalized, Vector3.up);
                    Log("near " + t.name + " at " + p);
                    break;
                }
                case "tp":
                    motor.Teleport(new Vector3(float.Parse(st[2]), float.Parse(st[3]), float.Parse(st[4])));
                    Log("tp " + motor.transform.position);
                    break;
                case "view":
                {
                    ThirdPersonView v = motor.GetComponent<ThirdPersonView>();
                    v.view = arg == "fp" ? ThirdPersonView.View.FirstPerson : arg == "front" ? ThirdPersonView.View.Front : ThirdPersonView.View.Behind;
                    if (st.Length > 3) v.distance = float.Parse(st[3]);
                    Log("view " + arg);
                    break;
                }
                case "slot":
                    motor.GetComponent<Loadout>().Select(int.Parse(arg));
                    Log("slot " + arg);
                    break;
                case "set":
                {
                    // set <Component> <field> <value>   (public float / bool field on a Player component)
                    Component c = motor.GetComponentInChildren(System.Type.GetType("ShaftAttack." + arg + ", Assembly-CSharp"));
                    FieldInfo fi = c.GetType().GetField(st[3]);
                    object val = fi.FieldType == typeof(bool) ? (object)bool.Parse(st[4])
                               : fi.FieldType == typeof(Vector3) ? (object)new Vector3(float.Parse(st[4]), float.Parse(st[5]), float.Parse(st[6]))
                               : float.Parse(st[4]);
                    fi.SetValue(c, val);
                    Log("set " + arg + "." + st[3] + " = " + st[4]);
                    break;
                }
                case "gunset":
                {
                    // gunset <slot> <field> <x> [y z]   change a GunDefinition value (runtime only, not saved)
                    Loadout lo = motor.GetComponent<Loadout>();
                    GunDefinition g = lo.GunIn(int.Parse(arg));
                    FieldInfo fi = typeof(GunDefinition).GetField(st[3]);
                    object val;
                    if (fi.FieldType == typeof(Vector3)) val = new Vector3(float.Parse(st[4]), float.Parse(st[5]), float.Parse(st[6]));
                    else if (fi.FieldType == typeof(Vector2)) val = new Vector2(float.Parse(st[4]), float.Parse(st[5]));
                    else val = float.Parse(st[4]);
                    fi.SetValue(g, val);
                    Log("gunset " + g.name + "." + st[3]);
                    break;
                }
                case "shot":
                    Shot(motor, arg);
                    break;
                case "info":
                    Log("info " + arg + " " + State(motor));
                    break;
                case "screen":
                    ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "screen_" + arg + ".png"));
                    Log("screen " + arg + " | " + State(motor));
                    break;
                case "ik":
                    Log("ik " + arg + " " + IK(motor));
                    break;
                case "dbgleft":
                {
                    DigbyGunHold h = motor.GetComponentInChildren<DigbyGunHold>();
                    System.Type t = typeof(DigbyGunHold);
                    BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
                    ArmChain left = (ArmChain)t.GetField("left", bf).GetValue(h);
                    GunModel held = (GunModel)t.GetField("held", bf).GetValue(h);
                    float lw = (float)t.GetField("leftWeight", bf).GetValue(h);
                    if (left == null || held == null || held.foregrip == null) { Log("dbgleft nothing"); break; }
                    Vector3 lp;
                    Quaternion lr;
                    left.HandForGrip(held.foregrip.position, GunModel.HandOn(held.foregrip), out lp, out lr);
                    Transform b = h.transform;
                    Log("dbgleft " + arg + " lw " + lw.ToString("0.00") + " shoulder " + b.InverseTransformPoint(left.upper.position).ToString("F2") +
                        " elbow " + b.InverseTransformPoint(left.lower.position).ToString("F2") +
                        " hand " + b.InverseTransformPoint(left.hand.position).ToString("F2") + " target " + b.InverseTransformPoint(lp).ToString("F2") +
                        " foregrip " + b.InverseTransformPoint(held.foregrip.position).ToString("F2") + " gripbone " + b.InverseTransformPoint(left.grip.position).ToString("F2") +
                        " grip local " + left.grip.localPosition.ToString("F3") + " scale " + left.hand.lossyScale.ToString("F3") +
                        " hand->target " + (Vector3.Distance(left.hand.position, lp) * 100f).ToString("0") + "cm");
                    break;
                }
                default:
                    Log("unknown " + a);
                    break;
            }
        }

        private static GameObject FindTarget(string name)
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == name && go.transform.parent == null && go.GetComponentInChildren<PlayerMotor>() == null) return go;
            return GameObject.Find(name);
        }

        private static string State(PlayerMotor motor)
        {
            Loadout lo = motor.GetComponent<Loadout>();
            PlayerGun pg = motor.GetComponent<PlayerGun>();
            GunViewmodel vm = motor.GetComponent<GunViewmodel>();
            string s = "state " + motor.State + " speed " + motor.Speed.ToString("0.0");
            if (lo != null) s += " | slot " + lo.Slot + " " + lo.NameOf(lo.Slot) + " ready " + lo.Ready;
            if (pg != null) s += " | shots " + pg.ShotsFired + " trigger " + pg.TriggerAmount.ToString("0.00") + " bloom " + pg.Bloom.ToString("0.00");
            s += " | bullets " + Bullets.LiveCount;
            if (vm != null) s += " | vm " + vm.Showing;
            foreach (Health h in Object.FindObjectsByType<Health>(FindObjectsSortMode.None))
                s += " | " + h.name + " hp " + h.Current.ToString("0");
            SmoothTerrain t = SmoothTerrain.Instance;
            if (t != null) s += " | terrain queued " + t.PendingRebuilds + " " + t.LastFrameRebuildMs.ToString("0.0") + "ms";
            s += " | dt " + (Time.unscaledDeltaTime * 1000f).ToString("0") + "ms";
            return s;
        }

        private static string IK(PlayerMotor motor)
        {
            string s = "";
            foreach (GunModel g in Object.FindObjectsByType<GunModel>(FindObjectsSortMode.None))
            {
                if (g.Renderers == null || g.Renderers.Length == 0 || !g.Renderers[0].enabled) continue;
                bool vm = g.gameObject.layer == LayerMask.NameToLayer("Viewmodel");
                Transform top = vm ? g.transform.parent : motor.transform;
                Transform rg = ArmChain.Deep(vm ? top : motor.transform.Find("DigbyBody"), "Pickaxe");
                Transform lg = ArmChain.Deep(vm ? top : motor.transform.Find("DigbyBody"), "LeftGrip");
                s += (vm ? "[view] " : "[body] ") + g.name;
                if (rg != null) s += " right hand to Grip " + (Vector3.Distance(rg.position, g.grip.position) * 100f).ToString("0.0") + "cm " +
                                     Quaternion.Angle(rg.rotation, GunModel.HandOn(g.grip)).ToString("0") + "deg";
                if (lg != null && g.foregrip != null) s += " left hand to Foregrip " + (Vector3.Distance(lg.position, g.foregrip.position) * 100f).ToString("0.0") + "cm " +
                                     Quaternion.Angle(lg.rotation, GunModel.HandOn(g.foregrip)).ToString("0") + "deg";
                if (!vm)
                {
                    Transform bodyT = motor.transform.Find("DigbyBody");
                    foreach (string side in new[] { "Right", "Left" })
                    {
                        Transform up = ArmChain.Deep(bodyT, side + "UpperArm"), lo = ArmChain.Deep(bodyT, side + "LowerArm"), ha = ArmChain.Deep(bodyT, side + "Hand");
                        Transform target = side == "Right" ? g.grip : g.foregrip;
                        if (up == null || target == null) continue;
                        float reach = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, ha.position);
                        s += " | " + side + " shoulder->target " + (Vector3.Distance(up.position, target.position) * 100f).ToString("0") +
                             "cm, shoulder->wrist " + (Vector3.Distance(up.position, ha.position) * 100f).ToString("0") + "cm of " + (reach * 100f).ToString("0");
                    }
                }
                if (g.muzzle != null)
                {
                    Camera cam = motor.cameraPivot.GetComponentInChildren<Camera>();
                    s += " muzzle-aim " + Vector3.Angle(g.muzzle.forward, cam.transform.forward).ToString("0") + "deg";
                }
                s += "; ";
            }
            return s;
        }

        // ------------------------------------------------------------------ pictures

        private static void Shot(PlayerMotor motor, string arg)
        {
            string name = arg, where = "";
            int at = arg.IndexOf('@');
            if (at >= 0) { name = arg.Substring(0, at); where = arg.Substring(at + 1); }
            if (rt == null) rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            Camera cam;
            ThirdPersonView v = motor.GetComponent<ThirdPersonView>();
            Camera player = motor.cameraPivot.GetComponentInChildren<Camera>();
            if (where == "")
            {
                cam = v != null ? v.ActiveCamera : player;
            }
            else
            {
                if (probeCam == null)
                {
                    GameObject go = new GameObject("ClaudeProbeCam");
                    probeCam = go.AddComponent<Camera>();
                    probeCam.CopyFrom(player);
                    probeCam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
                    probeCam.enabled = false;
                }
                probeCam.cullingMask = player.cullingMask;
                probeCam.fieldOfView = 45f;
                probeCam.nearClipPlane = 0.05f;
                Transform pt = motor.transform;
                Vector3 c = pt.position + Vector3.up * 1.2f;
                Vector3 off;
                switch (where)
                {
                    case "side": off = pt.right * 3.4f + Vector3.up * 0.3f; break;
                    case "sideL": off = -pt.right * 3.4f + Vector3.up * 0.3f; break;
                    case "front": off = pt.forward * 3.6f + Vector3.up * 0.4f; break;
                    case "back": off = -pt.forward * 3.4f + Vector3.up * 0.8f; break;
                    case "ql": off = (pt.forward - pt.right * 0.8f).normalized * 3.4f + Vector3.up * 0.5f; break;
                    case "top": off = Vector3.up * 3.5f + pt.forward * 0.6f; break;
                    case "hand": c = pt.position + Vector3.up * 1.35f + pt.forward * 0.5f + pt.right * 0.15f; off = (pt.right * 1.2f + pt.forward * 0.9f + Vector3.up * 0.3f); break;
                    default: off = (pt.forward + pt.right * 0.8f).normalized * 3.4f + Vector3.up * 0.5f; break;     // q
                }
                probeCam.transform.position = c + off;
                probeCam.transform.rotation = Quaternion.LookRotation(c - probeCam.transform.position, Vector3.up);
                cam = probeCam;
            }
            RenderTexture keep = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = keep;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            shotNo++;
            string file = "shot_" + shotNo.ToString("00") + "_" + name + (where != "" ? "@" + where : "") + ".png";
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
            Log("shot " + file + " | " + State(motor));
        }

        // ------------------------------------------------------------------ edit mode: how the gun models came in

        private static void Info()
        {
            string s = "";
            foreach (string g in new[] { "Pistol", "Rifle" })
            {
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShaftAttack/Weapons/" + g + ".prefab");
                if (p == null) { s += g + ": no prefab\n"; continue; }
                s += g + " root rot " + p.transform.localRotation.eulerAngles + " scale " + p.transform.localScale + "\n";
                foreach (Transform t in p.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 lp = p.transform.InverseTransformPoint(t.position);
                    Quaternion r = Quaternion.Inverse(p.transform.rotation) * t.rotation;
                    s += "  " + t.name + " pos " + lp.ToString("F3") + " X " + (r * Vector3.right).ToString("F2") + " Y " +
                         (r * Vector3.up).ToString("F2") + " Z " + (r * Vector3.forward).ToString("F2");
                    MeshFilter mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) s += " mesh " + mf.sharedMesh.vertexCount + "v sub " + mf.sharedMesh.subMeshCount + " bounds " + mf.sharedMesh.bounds;
                    s += "\n";
                }
            }
            GameObject digby = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShaftAttack/Characters/Digby/Digby.fbx");
            Transform pick = ArmChain.Deep(digby.transform, "Pickaxe");
            Transform pm = ArmChain.Deep(digby.transform, "PickaxeMesh");
            if (pick != null && pm != null)
            {
                MeshFilter mf = pm.GetComponent<MeshFilter>();
                Vector3[] v = mf.sharedMesh.vertices;
                Vector3 far = Vector3.zero;
                foreach (Vector3 x in v) if (Mathf.Abs(x.z) > Mathf.Abs(far.z)) far = x;
                Vector3 top = Vector3.zero;
                foreach (Vector3 x in v) if (x.y > top.y) top = x;
                s += "PickaxeMesh local rot " + pm.localRotation.eulerAngles + " farthest-z vertex " + far.ToString("F3") + " top vertex " + top.ToString("F3") + "\n";
            }
            File.WriteAllText(Path.Combine(Dir, "probe_info_out.txt"), s);
        }
    }
}
#endif
