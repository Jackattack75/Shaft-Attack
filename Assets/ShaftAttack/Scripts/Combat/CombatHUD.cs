using System.Collections.Generic;
using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// What you need to see while shooting (placeholder, OnGUI like the movement HUD):
    ///   - the slots, bottom right: 1 Pickaxe, 2 Pistol, 3 Rifle, the one in your hand lit up
    ///   - a hit marker round the crosshair when your bullet hurts something (bigger and red when it dies)
    ///   - damage numbers that pop up where the bullet hit
    /// Sits on the Player.
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        public bool show = true;
        public Color hitColor = new Color(1f, 1f, 1f, 0.95f);
        public Color killColor = new Color(1f, 0.25f, 0.2f, 1f);
        public Color numberColor = new Color(1f, 0.92f, 0.55f, 1f);

        private struct Pop
        {
            public Vector3 point;
            public float amount;
            public float born;
            public bool kill;
            public Vector2 drift;
        }

        private Loadout loadout;
        private ThirdPersonView view;
        private Camera playerCamera;
        private readonly List<Pop> pops = new List<Pop>();
        private float hitAt = -10f;
        private bool hitKill;
        private GUIStyle numberStyle, slotStyle, keyStyle;

        private void Awake()
        {
            loadout = GetComponent<Loadout>();
            view = GetComponent<ThirdPersonView>();
            playerCamera = GetComponentInChildren<Camera>();
        }

        private void OnEnable()
        {
            GameEvents.Damaged += Damaged;
        }

        private void OnDisable()
        {
            GameEvents.Damaged -= Damaged;
        }

        private void Damaged(Health target, DamageInfo info, bool killed)
        {
            if (info.attacker != gameObject) return;
            hitAt = Time.time;
            hitKill = killed;
            if (pops.Count > 24) pops.RemoveAt(0);
            pops.Add(new Pop { point = info.point, amount = info.amount, born = Time.time, kill = killed,
                               drift = new Vector2(Random.Range(-18f, 18f), 0f) });
        }

        private void OnGUI()
        {
            if (!show || Event.current.type != EventType.Repaint) return;
            Styles();
            Camera cam = view != null ? view.ActiveCamera : playerCamera;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;

            // ---- hit marker: four short slashes round the crosshair
            float age = Time.time - hitAt;
            float life = hitKill ? 0.35f : 0.18f;
            if (age < life)
            {
                float a = 1f - age / life;
                Color c = hitKill ? killColor : hitColor;
                c.a *= a;
                float r0 = hitKill ? 9f : 7f, r1 = hitKill ? 20f : 14f;
                Matrix4x4 keep = GUI.matrix;
                for (int i = 0; i < 4; i++)
                {
                    GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                    Fill(cx + r0, cy - 1f, r1 - r0, hitKill ? 3f : 2f, c);
                    GUI.matrix = keep;
                }
            }

            // ---- damage numbers
            if (cam != null)
            {
                for (int i = pops.Count - 1; i >= 0; i--)
                {
                    Pop p = pops[i];
                    float t = (Time.time - p.born) / 0.75f;
                    if (t >= 1f) { pops.RemoveAt(i); continue; }
                    Vector3 sp = cam.WorldToScreenPoint(p.point);
                    if (sp.z <= 0f) continue;
                    float x = sp.x + p.drift.x * t;
                    float y = Screen.height - sp.y - 18f - 40f * t;
                    Color c = p.kill ? killColor : numberColor;
                    c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                    string text = Mathf.RoundToInt(p.amount).ToString();
                    numberStyle.fontSize = p.kill ? 26 : 20;
                    Rect rr = new Rect(x - 40f, y - 15f, 80f, 30f);
                    numberStyle.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.8f);
                    GUI.Label(new Rect(rr.x + 2f, rr.y + 2f, rr.width, rr.height), text, numberStyle);
                    numberStyle.normal.textColor = c;
                    GUI.Label(rr, text, numberStyle);
                }
            }

            // ---- slots, bottom right
            if (loadout == null) return;
            int n = loadout.SlotCount;
            float w = 104f, h = 40f, gap = 6f;
            float x0 = Screen.width - 18f - n * (w + gap) + gap;
            float y0 = Screen.height - 18f - h;
            for (int i = 0; i < n; i++)
            {
                bool on = i == loadout.Slot;
                float x = x0 + i * (w + gap);
                Fill(x, y0, w, h, on ? new Color(1f, 0.78f, 0.25f, 0.9f) : new Color(0f, 0f, 0f, 0.5f));
                if (on && !loadout.Ready) Fill(x, y0 + h - 4f, w * loadout.DrawProgress, 4f, new Color(1f, 1f, 1f, 0.8f));
                keyStyle.normal.textColor = on ? new Color(0.1f, 0.1f, 0.12f) : new Color(1f, 1f, 1f, 0.6f);
                slotStyle.normal.textColor = on ? new Color(0.1f, 0.1f, 0.12f) : new Color(1f, 1f, 1f, 0.85f);
                GUI.Label(new Rect(x + 8f, y0 + 3f, 20f, 20f), (i + 1).ToString(), keyStyle);
                GUI.Label(new Rect(x + 8f, y0 + 12f, w - 12f, 26f), loadout.NameOf(i).ToUpper(), slotStyle);
            }
        }

        private void Styles()
        {
            if (numberStyle != null) return;
            numberStyle = new GUIStyle(GUI.skin.label);
            numberStyle.alignment = TextAnchor.MiddleCenter;
            numberStyle.fontStyle = FontStyle.Bold;
            slotStyle = new GUIStyle(GUI.skin.label);
            slotStyle.fontSize = 15;
            slotStyle.fontStyle = FontStyle.Bold;
            slotStyle.alignment = TextAnchor.MiddleCenter;
            keyStyle = new GUIStyle(GUI.skin.label);
            keyStyle.fontSize = 11;
            keyStyle.fontStyle = FontStyle.Bold;
        }

        private static void Fill(float x, float y, float w, float h, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
