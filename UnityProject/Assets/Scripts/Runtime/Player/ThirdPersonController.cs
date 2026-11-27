using Underworld.Core;
using UnityEngine;

namespace Underworld.Player
{
    /// <summary>
    /// Movimento in terza persona: cammina, corre, salta. La direzione segue la camera
    /// (o il mondo, se la sorgente dei comandi lo chiede: bot).
    /// In rete è attivo solo sul proprietario del personaggio (lo accende PlayerNetwork).
    /// I comandi arrivano da un IPlayerInput; con una finestra UI aperta (InputGate) li ignora,
    /// ma la gravità continua.
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
        [Tooltip("Velocità massima di caduta (m/s).")]
        public float terminalVelocity = 50f;

        [Tooltip("Camera per la direzione del movimento. Se vuota usa Camera.main.")]
        public Transform cameraTransform;

        /// <summary>Passo massimo di simulazione: un frame lento non fa "saltare" il personaggio.</summary>
        public const float MaxStepTime = 0.1f;

        /// <summary>Sorgente dei comandi. Null = nessun comando (il personaggio resta fermo).</summary>
        public IPlayerInput InputSource { get; set; }

        /// <summary>Velocità orizzontale attuale (m/s).</summary>
        public float CurrentSpeed { get; private set; }
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        /// <summary>Velocità dell'ultimo frame (m/s).</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// Solo per i test automatici (scenario speed_hack_corrected): moltiplica la velocità per simulare un trucco.
        /// Interno all'assembly: il gioco normale non lo tocca mai.
        /// </summary>
        internal float DebugSpeedMultiplier = 1f;

        CharacterController _cc;
        float _velocityY;
        float _turnVelocity;
        Vector3 _moveDir = Vector3.forward;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        void Start()
        {
            // Scena senza rete (vecchia scena di prova): comandi da tastiera.
            if (InputSource == null && GetComponent<Unity.Netcode.NetworkObject>() == null)
                InputSource = new KeyboardMouseInput();
        }

        Transform CameraTransform
        {
            get
            {
                if (cameraTransform == null)
                {
                    var cam = Camera.main;
                    if (cam != null) cameraTransform = cam.transform;
                }
                return cameraTransform;
            }
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, MaxStepTime);
            if (dt <= 0f) return;

            PlayerInputState input = default;
            if (InputSource != null && !InputGate.GameplayBlocked) input = InputSource.Read();

            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            bool hasInput = move.sqrMagnitude > 0.01f;
            if (hasInput)
            {
                float baseYaw = 0f;
                if (!input.WorldSpace)
                {
                    var cam = CameraTransform;
                    if (cam != null) baseYaw = cam.eulerAngles.y;
                }
                float targetYaw = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg + baseYaw;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref _turnVelocity, turnSmoothTime, Mathf.Infinity, dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _moveDir = Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward;
            }

            float multiplier = Mathf.Max(0.01f, DebugSpeedMultiplier);
            float targetSpeed = hasInput ? (input.Sprint ? sprintSpeed : walkSpeed) * move.magnitude * multiplier : 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, acceleration * multiplier * dt);

            if (_cc.isGrounded && _velocityY < 0f) _velocityY = -2f;
            if (input.Jump && _cc.isGrounded) _velocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _velocityY = Mathf.Max(_velocityY + gravity * dt, -terminalVelocity);

            Vector3 motion = _moveDir * CurrentSpeed + Vector3.up * _velocityY;
            _cc.Move(motion * dt);
            Velocity = motion;
        }

        /// <summary>
        /// Rimette la capsula del CharacterController sulla posizione attuale del transform.
        /// Serve dopo ogni spostamento diretto del transform (spawn di Netcode, teletrasporto):
        /// il progetto ha Physics.autoSyncTransforms spento, e senza questo il primo Move partirebbe
        /// dalla posizione vecchia della capsula (per lo spawn: (0,0,0) del prefab).
        /// Spegnere e riaccendere ricrea la capsula nel punto giusto. Un controller spento resta spento.
        /// </summary>
        public static void SyncCharacterController(CharacterController cc)
        {
            if (cc == null || !cc.enabled) return;
            cc.enabled = false;
            cc.enabled = true;
        }

        /// <summary>Azzera velocità e caduta (dopo un teletrasporto o una correzione del server).</summary>
        public void ResetMotion()
        {
            CurrentSpeed = 0f;
            _velocityY = 0f;
            _turnVelocity = 0f;
            Velocity = Vector3.zero;
            _moveDir = transform.forward;
        }
    }
}
