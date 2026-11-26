using UnityEngine;
using UnityEngine.InputSystem;

namespace Underworld.Player
{
    /// <summary>
    /// Camera orbitale in terza persona: mouse per guardare, rotella per lo zoom.
    /// Non entra nei muri (SphereCast). Esc libera il mouse, click lo blocca di nuovo.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
        public float distance = 4.5f;
        public float minDistance = 1.5f;
        public float maxDistance = 9f;
        public float mouseSensitivity = 0.12f;
        public float gamepadSensitivity = 140f;
        public float minPitch = -30f;
        public float maxPitch = 70f;
        public float collisionRadius = 0.25f;
        [Tooltip("Il layer del giocatore (Ignore Raycast) va escluso.")]
        public LayerMask collisionMask = ~(1 << 2);

        float _yaw;
        float _pitch = 12f;
        float _currentDistance;

        void Start()
        {
            _currentDistance = distance;
            if (target != null) _yaw = target.eulerAngles.y;
            SetCursorLocked(true);
        }

        void LateUpdate()
        {
            if (target == null) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);

            Vector2 look = Vector2.zero;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                look = mouse.delta.ReadValue() * mouseSensitivity;
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.005f, minDistance, maxDistance);
            }
            var gp = Gamepad.current;
            if (gp != null) look += gp.rightStick.ReadValue() * gamepadSensitivity * Time.deltaTime;

            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + pivotOffset;
            Vector3 back = rot * Vector3.back;

            float wanted = distance;
            if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                wanted = Mathf.Max(hit.distance, 0.4f);

            // Avvicina subito (niente muri attraversati), allontana dolcemente.
            _currentDistance = wanted < _currentDistance ? wanted : Mathf.Lerp(_currentDistance, wanted, 8f * Time.deltaTime);

            transform.SetPositionAndRotation(pivot + back * _currentDistance, rot);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
