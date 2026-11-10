using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ShaftAttack
{
    /// <summary>
    /// A test tool: lets you see your own Digby from outside.
    ///
    ///   V            first person -> camera behind him -> camera in front of him -> first person
    ///   Mouse wheel  zoom in and out (third person only)
    ///
    /// Nothing about the game changes in third person: you still move, aim and dig from the first-person
    /// camera, it just stops drawing. A second camera (made the first time you switch) orbits him instead: it
    /// follows where you look, sits a little above him, and comes closer when rock is in the way.
    /// FirstPersonBody shows the whole body in third person, head included, standing where other players see it.
    /// Sits on the Player, next to PlayerMotor.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]            // after CameraFeel and FirstPersonBody have run
    public class ThirdPersonView : MonoBehaviour
    {
        public enum View { FirstPerson, Behind, Front }

        [Tooltip("The view the game starts in.")]
        public View view = View.FirstPerson;
        [Tooltip("Let V switch the view. Turn off to lock the view.")]
        public bool allowToggle = true;

        [Header("Third-person camera")]
        [Tooltip("How far the camera is from him (metres). The mouse wheel changes it.")]
        public float distance = 4.2f;
        public float minDistance = 1.6f;
        public float maxDistance = 10f;
        [Tooltip("Metres per notch of the mouse wheel.")]
        public float zoomStep = 0.6f;
        [Range(0f, 1f)]
        [Tooltip("The camera looks at a point this far up his body (0 = his feet, 1 = the top of his head).")]
        public float lookAtHeight = 0.72f;
        [Tooltip("Degrees the camera sits above him when you look straight ahead.")]
        public float tilt = 12f;
        [Range(0f, 1f)]
        [Tooltip("How much the camera rises and drops when you look down and up (1 = as far as you look).")]
        public float pitchFollow = 0.6f;
        public float fieldOfView = 60f;
        [Tooltip("Keep the camera out of the rock: it comes closer when something is in the way.")]
        public bool avoidWalls = true;
        public float cameraRadius = 0.25f;
        [Tooltip("Show the name of the view for a moment after switching.")]
        public bool showHint = true;

        public bool IsThirdPerson { get { return view != View.FirstPerson; } }
        /// <summary>The camera that is drawing right now.</summary>
        public Camera ActiveCamera { get { return IsThirdPerson && thirdCamera != null ? thirdCamera : playerCamera; } }

        private PlayerMotor motor;
        private PlayerLook look;
        private CharacterController controller;
        private FirstPersonBody body;
        private Camera playerCamera;
        private Camera thirdCamera;
        private View applied = View.FirstPerson;
        private float lookAtY = -1f;
        private float currentDistance;
        private float hintUntil;
        private GUIStyle hintStyle;

        private void Start()
        {
            motor = GetComponent<PlayerMotor>();
            look = GetComponent<PlayerLook>();
            controller = GetComponent<CharacterController>();
            body = GetComponentInChildren<FirstPersonBody>();
            Transform pivot = motor != null ? motor.cameraPivot : null;
            playerCamera = pivot != null ? pivot.GetComponentInChildren<Camera>() : GetComponentInChildren<Camera>();
            currentDistance = distance;
            if (view != View.FirstPerson) Apply(false);
        }

        private void OnDisable()
        {
            if (applied == View.FirstPerson) return;
            View keep = view;
            view = View.FirstPerson;
            Apply(false);
            view = keep;
        }

        private void OnEnable()
        {
            if (playerCamera != null && view != applied) Apply(false);
        }

        private void OnDestroy()
        {
            if (thirdCamera != null) Destroy(thirdCamera.gameObject);
        }

        private void Update()
        {
            if (allowToggle && GameInput.ViewTogglePressedThisFrame)
            {
                view = view == View.FirstPerson ? View.Behind : view == View.Behind ? View.Front : View.FirstPerson;
                Apply(true);
            }
            else if (view != applied)                 // changed in the Inspector
            {
                Apply(true);
            }

            if (IsThirdPerson)
            {
                float wheel = Mathf.Clamp(GameInput.Scroll, -3f, 3f);
                if (wheel != 0f) distance = Mathf.Clamp(distance - wheel * zoomStep, minDistance, maxDistance);
            }
        }

        private void Apply(bool hint)
        {
            applied = view;
            bool third = view != View.FirstPerson;
            if (third) MakeCamera();
            if (playerCamera != null) playerCamera.enabled = !third;
            if (thirdCamera != null) thirdCamera.enabled = third;
            if (body != null) body.SetThirdPerson(third);
            if (third)
            {
                currentDistance = distance;
                lookAtY = -1f;
                PlaceCamera(0f);
            }
            if (hint) hintUntil = Time.unscaledTime + 2.5f;
        }

        private void MakeCamera()
        {
            if (thirdCamera != null) return;
            GameObject go = new GameObject("ThirdPersonCamera (test view)");
            thirdCamera = go.AddComponent<Camera>();
            if (playerCamera != null)
            {
                thirdCamera.CopyFrom(playerCamera);
                UniversalAdditionalCameraData from = playerCamera.GetUniversalAdditionalCameraData();
                UniversalAdditionalCameraData to = thirdCamera.GetUniversalAdditionalCameraData();
                if (from != null && to != null)
                {
                    to.renderPostProcessing = from.renderPostProcessing;
                    to.antialiasing = from.antialiasing;
                    to.antialiasingQuality = from.antialiasingQuality;
                    to.renderShadows = from.renderShadows;
                    to.volumeLayerMask = from.volumeLayerMask;
                    to.dithering = from.dithering;
                    to.stopNaN = from.stopNaN;
                }
            }
            thirdCamera.fieldOfView = fieldOfView;
            thirdCamera.nearClipPlane = 0.1f;
            thirdCamera.targetTexture = null;
            go.tag = "MainCamera";                   // Camera.main keeps working while the player's camera is off
            thirdCamera.enabled = false;
        }

        private void LateUpdate()
        {
            if (IsThirdPerson) PlaceCamera(Time.deltaTime);
        }

        private void PlaceCamera(float dt)
        {
            if (thirdCamera == null) return;
            thirdCamera.fieldOfView = fieldOfView;

            // what the camera looks at: a point up his body, lower while he slides
            float tall = controller != null ? controller.height : 1.8f;
            float wantY = tall * lookAtHeight;
            lookAtY = lookAtY < 0f || dt <= 0f ? wantY : Mathf.Lerp(lookAtY, wantY, 1f - Mathf.Exp(-10f * dt));
            Vector3 target = transform.position + Vector3.up * lookAtY;

            // orbit: follows where you look; the front view is the same orbit from the other side
            float pitch = Mathf.Clamp((look != null ? look.Pitch : 0f) * pitchFollow + tilt, -35f, 80f);
            float yaw = transform.eulerAngles.y + (view == View.Front ? 180f : 0f);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rotation * Vector3.back;

            float want = distance;
            if (avoidWalls)
            {
                int mask = ~((1 << gameObject.layer) | (1 << 2));         // not the player, not Ignore Raycast
                RaycastHit hit;
                if (Physics.SphereCast(target, cameraRadius, back, out hit, distance, mask, QueryTriggerInteraction.Ignore))
                    want = Mathf.Max(hit.distance, 0.5f);
            }
            // straight in when something blocks the view, eases back out afterwards
            currentDistance = want < currentDistance || dt <= 0f ? want : Mathf.MoveTowards(currentDistance, want, 9f * dt);
            thirdCamera.transform.SetPositionAndRotation(target + back * currentDistance, rotation);
        }

        private void OnGUI()
        {
            if (!showHint || Time.unscaledTime > hintUntil) return;
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label);
                hintStyle.alignment = TextAnchor.MiddleCenter;
                hintStyle.fontSize = 16;
                hintStyle.fontStyle = FontStyle.Bold;
            }
            string text = view == View.FirstPerson ? "First person"
                        : view == View.Behind ? "Third person: behind"
                        : "Third person: front";
            text += "   (V to switch" + (IsThirdPerson ? ", mouse wheel to zoom)" : ")");
            Rect r = new Rect(Screen.width * 0.5f - 260f, 28f, 520f, 30f);
            Color keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, hintStyle);
            GUI.color = Color.white;
            GUI.Label(r, text, hintStyle);
            GUI.color = keep;
        }
    }
}
