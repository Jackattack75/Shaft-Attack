using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Test-scene helper for looking at Digby's animations. Press Play and he loops
    /// Idle, Walk, Run and Jump, with the name floating over his head. To watch just one,
    /// select him and pick it under "Show" (works while playing too).
    /// The walk and run play on the spot, like a treadmill.
    /// </summary>
    public class DigbyShowcase : MonoBehaviour
    {
        public enum Clip { CycleAll, Idle, Walk, Run, Jump }

        [Tooltip("CycleAll plays them one after another.")]
        public Clip show = Clip.CycleAll;

        [Tooltip("Seconds on each animation when cycling.")]
        public float secondsEach = 4f;

        [Tooltip("Speed of the Walk / Run clips in m/s (from the Blender export).")]
        public float walkSpeed = 0.499f;
        public float runSpeed = 3.375f;

        public bool showLabel = true;

        private static readonly Clip[] Order = { Clip.Idle, Clip.Walk, Clip.Run, Clip.Jump };
        private const float JumpLength = 3.4f;

        private Animator animator;
        private Clip playing = Clip.CycleAll;
        private Clip lastShow = Clip.CycleAll;
        private bool started;
        private int step;
        private float timer;
        private float speedNow;
        private float speedTarget;
        private GUIStyle style;

        private void Start()
        {
            animator = GetComponentInChildren<Animator>();
            DigbyAnimDriver driver = GetComponent<DigbyAnimDriver>();
            if (driver != null) driver.enabled = false;          // the showcase drives the Animator instead
            lastShow = show;
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (show != lastShow)
            {
                lastShow = show;
                started = false;
                timer = 0f;
                step = 0;
            }

            Clip want = show;
            timer += Time.deltaTime;
            if (show == Clip.CycleAll)
            {
                float length = Order[step] == Clip.Jump ? JumpLength : secondsEach;
                if (timer >= length)
                {
                    timer = 0f;
                    step = (step + 1) % Order.Length;
                    started = false;
                }
                want = Order[step];
            }
            else if (show == Clip.Jump && timer >= JumpLength + 0.4f)
            {
                timer = 0f;
                started = false;                                  // jump again
            }

            if (!started || want != playing)
            {
                Begin(want);
                playing = want;
                started = true;
            }
            speedNow = Mathf.MoveTowards(speedNow, speedTarget, 3f * Time.deltaTime);
            animator.SetFloat("Speed", speedNow);
        }

        private void Begin(Clip c)
        {
            animator.SetFloat("MoveRate", 1f);
            animator.SetBool("Grounded", true);
            if (c == Clip.Jump)
            {
                speedTarget = 0f;
                speedNow = 0f;
                animator.CrossFadeInFixedTime("Jump", 0.25f, 0, 0f);
                return;
            }
            speedTarget = c == Clip.Walk ? walkSpeed : (c == Clip.Run ? runSpeed : 0f);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"))
                animator.CrossFadeInFixedTime("Locomotion", 0.3f);
        }

        private void OnGUI()
        {
            if (!showLabel || playing == Clip.CycleAll) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.15f);
            if (sp.z <= 0f) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box);
                style.fontSize = 22;
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.MiddleCenter;
                style.normal.textColor = Color.white;
            }
            string text = playing.ToString();
            Vector2 size = style.CalcSize(new GUIContent(text)) + new Vector2(18f, 8f);
            GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y), text, style);
        }
    }
}

