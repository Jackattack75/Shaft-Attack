using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Tuning readout. Speed is displayed big because in a momentum game the speed number
    /// IS the score - you want to feel yourself earning it while you test.
    /// Delete once the feel is locked in.
    /// </summary>
    public class MovementDebugHUD : MonoBehaviour
    {
        public PlayerMotor motor;
        public MinerTools tools;
        public bool show = true;

        private GUIStyle speedStyle;
        private GUIStyle label;
        private GUIStyle hint;

        private static readonly Color Fast = new Color(1f, 0.42f, 0.18f);
        private static readonly Color Normal = new Color(0.62f, 0.84f, 1f);
        private static readonly Color BombColor = new Color(1f, 0.78f, 0.25f);

        private void Reset() { motor = GetComponent<PlayerMotor>(); }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (tools == null) tools = GetComponent<MinerTools>();
        }

        private void OnGUI()
        {
            if (!show || motor == null) return;
            EnsureStyles();

            SmoothTerrain terrain = SmoothTerrain.Instance;

            float panelH = 132f;
            if (tools != null) panelH += 20f;
            if (terrain != null) panelH += 18f;

            float panelX = 18f;
            float panelY = Screen.height - panelH - 18f;

            Fill(panelX, panelY, 300f, panelH, new Color(0f, 0f, 0f, 0.5f));

            // --- speed, big, and it changes colour once you're above running pace ---
            bool aboveRun = motor.Speed > motor.runSpeed + 0.2f;
            speedStyle.normal.textColor = aboveRun ? Fast : Normal;
            GUI.Label(new Rect(panelX + 16f, panelY + 6f, 260f, 46f), motor.Speed.ToString("0.0"), speedStyle);
            GUI.Label(new Rect(panelX + 108f, panelY + 26f, 120f, 22f), "m/s", label);

            // --- speed bar: the notch is running pace, anything past it is earned ---
            float barW = 268f;
            Fill(panelX + 16f, panelY + 56f, barW, 8f, new Color(1f, 1f, 1f, 0.12f));
            Fill(panelX + 16f, panelY + 56f, barW * Mathf.Clamp01(motor.Speed / 30f), 8f, aboveRun ? Fast : Normal);
            float notch = barW * Mathf.Clamp01(motor.runSpeed / 30f);
            Fill(panelX + 16f + notch, panelY + 53f, 2f, 14f, new Color(1f, 1f, 1f, 0.8f));

            // --- stamina pips ---
            float y = panelY + 72f;
            GUI.Label(new Rect(panelX + 16f, y - 4f, 120f, 20f), "DASH", label);
            DrawPips(panelX + 80f, y, motor.maxDashCharges, motor.DashCharges, motor.DashRecharge, Fast);

            // --- bomb pips ---
            if (tools != null)
            {
                y += 20f;
                GUI.Label(new Rect(panelX + 16f, y - 4f, 120f, 20f), "BOMB", label);
                DrawPips(panelX + 80f, y, tools.maxBombs, tools.Bombs, tools.BombRecharge, BombColor);
            }

            y += 18f;
            GUI.Label(new Rect(panelX + 16f, y, 280f, 20f),
                motor.State.ToString().ToUpper() + "    walls left " + motor.WallJumpsLeft, label);

            // --- terrain cost, so you can see what digging actually costs ---
            if (terrain != null)
            {
                y += 18f;
                GUI.Label(new Rect(panelX + 16f, y, 290f, 20f),
                    "terrain  " + terrain.AverageChunkMs.ToString("0.00") + " ms/chunk   " +
                    terrain.PendingRebuilds + " queued   " +
                    terrain.FloatingPiecesRemoved + " floaters cleared", hint);
            }

            GUI.Label(new Rect(panelX + 16f, y + 18f, 290f, 20f),
                tools != null ? "LMB pickaxe / RMB or Q bomb / Shift / Ctrl / Space"
                              : "WASD / Shift dash / Ctrl slide / Space", hint);

            DrawCrosshair();
        }

        private static void DrawPips(float x, float y, int max, int filled, float refill, Color color)
        {
            for (int i = 0; i < max; i++)
            {
                float px = x + i * 30f;
                Fill(px, y, 24f, 10f, new Color(1f, 1f, 1f, 0.15f));
                if (i < filled) Fill(px, y, 24f, 10f, color);
                else if (i == filled) Fill(px, y, 24f * refill, 10f, color * 0.6f);
            }
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            Color c = new Color(1f, 1f, 1f, 0.75f);
            Fill(cx - 6, cy - 1, 12, 2, c);
            Fill(cx - 1, cy - 6, 2, 12, c);
        }

        private void EnsureStyles()
        {
            if (label != null) return;

            speedStyle = new GUIStyle(GUI.skin.label);
            speedStyle.fontSize = 40;
            speedStyle.fontStyle = FontStyle.Bold;

            label = new GUIStyle(GUI.skin.label);
            label.fontSize = 14;
            label.normal.textColor = Color.white;

            hint = new GUIStyle(GUI.skin.label);
            hint.fontSize = 12;
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
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
