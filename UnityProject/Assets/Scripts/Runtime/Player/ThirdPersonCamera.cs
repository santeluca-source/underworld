using Underworld.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Underworld.Player
{
    /// <summary>
    /// Camera orbitale in terza persona: mouse per guardare, rotella per lo zoom. Non entra nei muri (SphereCast).
    /// Mouse: bloccato al centro quando c'è un personaggio da seguire e nessuna finestra UI (InputGate);
    /// libero negli altri casi. Il tasto Esc non lo gestisce la camera: lo usa il menu di pausa (modulo UI).
    /// Nei test automatici (-autotest) la camera non legge il mouse e non lo blocca.
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
        bool _readUserInput = true;
        bool _cursorStateKnown;
        bool _cursorLocked;

        void Awake()
        {
            _currentDistance = distance;
            _readUserInput = string.IsNullOrEmpty(LaunchArgs.Current.AutoTest);
        }

        void Start()
        {
            if (target != null) _yaw = target.eulerAngles.y;
        }

        /// <summary>Segui un nuovo personaggio (null = nessuno: il mouse torna libero).</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (newTarget != null)
            {
                _yaw = newTarget.eulerAngles.y;
                _currentDistance = distance;
                // Parti subito dietro al personaggio (niente volo dalla vecchia posizione).
                Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
                transform.SetPositionAndRotation(newTarget.position + pivotOffset + rot * Vector3.back * distance, rot);
            }
            UpdateCursor();
        }

        /// <summary>
        /// Collega la camera principale della scena a un personaggio. Se la scena non ha una camera,
        /// la crea (Camera + AudioListener + ThirdPersonCamera).
        /// </summary>
        public static ThirdPersonCamera AttachToMainCamera(Transform newTarget)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                cam.fieldOfView = 60f;
                cam.farClipPlane = 600f;
                go.AddComponent<AudioListener>();
            }
            var tpc = cam.GetComponent<ThirdPersonCamera>();
            if (tpc == null) tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
            tpc.SetTarget(newTarget);
            return tpc;
        }

        void LateUpdate()
        {
            UpdateCursor();
            if (target == null) return;

            Vector2 look = Vector2.zero;
            if (_readUserInput && !InputGate.GameplayBlocked)
            {
                var mouse = Mouse.current;
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                {
                    look = mouse.delta.ReadValue() * mouseSensitivity;
                    distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.005f, minDistance, maxDistance);
                }
                var gp = Gamepad.current;
                if (gp != null) look += gp.rightStick.ReadValue() * gamepadSensitivity * Time.deltaTime;
            }

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

        /// <summary>Blocca o libera il mouse solo quando la regola cambia (non a ogni frame).</summary>
        void UpdateCursor()
        {
            bool wantLocked = _readUserInput && target != null && !InputGate.GameplayBlocked;
            if (_cursorStateKnown && wantLocked == _cursorLocked) return;
            _cursorStateKnown = true;
            _cursorLocked = wantLocked;
            Cursor.lockState = wantLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !wantLocked;
        }

        void OnDisable()
        {
            if (_cursorLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            _cursorStateKnown = false;
            _cursorLocked = false;
        }
    }
}
