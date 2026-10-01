using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Mouse look. Yaw turns the whole body (so movement follows where you're facing),
    /// pitch only tilts the camera pivot.
    ///
    /// Note that PlayerMotor owns the pivot's POSITION (it moves up and down when you
    /// crouch) and this owns its ROTATION. They don't fight over it.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Child transform holding the camera.")]
        public Transform cameraPivot;

        [Header("Feel")]
        public float sensitivity = 2.2f;
        [Tooltip("How far up and down you can look, in degrees.")]
        public float maxPitch = 89f;
        public bool invertY = false;

        [Header("Cursor")]
        public bool lockCursorOnStart = true;

        public bool CursorLocked { get; private set; }
        public float Pitch { get { return pitch; } }

        private float pitch;

        private void Start()
        {
            SetCursorLocked(lockCursorOnStart);
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
        }

        private void Update()
        {
            // Escape to get the mouse back, click in the game view to recapture it.
            if (GameInput.CancelPressedThisFrame) SetCursorLocked(false);
            else if (!CursorLocked && GameInput.ClickPressedThisFrame) SetCursorLocked(true);

            if (!CursorLocked) return;

            Vector2 delta = GameInput.LookDelta * sensitivity;

            transform.Rotate(Vector3.up, delta.x, Space.Self);

            pitch += invertY ? delta.y : -delta.y;
            pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        public void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
