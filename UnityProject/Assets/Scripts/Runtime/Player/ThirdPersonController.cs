using UnityEngine;
using UnityEngine.InputSystem;

namespace Underworld.Player
{
    /// <summary>
    /// Movimento in terza persona: cammina (WASD), corre (Shift), salta (Spazio).
    /// La direzione segue la camera. Supporta anche il gamepad.
    /// Nota: per ora è solo locale. In Fase 4 il server diventerà autoritativo.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Velocità")]
        public float walkSpeed = 4f;
        public float sprintSpeed = 7.5f;
        public float acceleration = 14f;
        public float turnSmoothTime = 0.08f;

        [Header("Salto e gravità")]
        public float jumpHeight = 1.3f;
        public float gravity = -22f;

        public Transform cameraTransform;

        public float CurrentSpeed { get; private set; }
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        CharacterController _cc;
        float _velocityY;
        float _turnVelocity;
        Vector3 _moveDir = Vector3.forward;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            ReadInput(out Vector2 input, out bool sprint, out bool jump);

            bool hasInput = input.sqrMagnitude > 0.01f;
            if (hasInput)
            {
                float camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
                float targetYaw = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg + camYaw;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref _turnVelocity, turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _moveDir = Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward;
            }

            float targetSpeed = hasInput ? (sprint ? sprintSpeed : walkSpeed) * input.magnitude : 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, acceleration * Time.deltaTime);

            if (_cc.isGrounded && _velocityY < 0f) _velocityY = -2f;
            if (jump && _cc.isGrounded) _velocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _velocityY += gravity * Time.deltaTime;

            Vector3 motion = _moveDir * CurrentSpeed + Vector3.up * _velocityY;
            _cc.Move(motion * Time.deltaTime);
        }

        static void ReadInput(out Vector2 input, out bool sprint, out bool jump)
        {
            input = Vector2.zero; sprint = false; jump = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                sprint = kb.leftShiftKey.isPressed;
                jump = kb.spaceKey.wasPressedThisFrame;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                input += gp.leftStick.ReadValue();
                sprint |= gp.leftStickButton.isPressed;
                jump |= gp.buttonSouth.wasPressedThisFrame;
            }

            input = Vector2.ClampMagnitude(input, 1f);
        }
    }
}
