using UnityEngine;

namespace Underworld.Player
{
    /// <summary>
    /// Animazione provvisoria fatta a codice (finché non c'è un modello con animazioni):
    /// gambe e braccia oscillano, il corpo rimbalza, si inclina quando corre.
    /// Personaggio locale: usa i valori del controller. Personaggi degli altri giocatori
    /// (controller spento): ricava velocità e salto dal movimento che arriva dalla rete.
    /// </summary>
    public class PlayerVisual : MonoBehaviour
    {
        public ThirdPersonController controller;
        public Transform body;
        public Transform leftLeg;
        public Transform rightLeg;
        public Transform leftArm;
        public Transform rightArm;

        float _phase;
        Vector3 _bodyBase;

        // Stima del movimento per i giocatori remoti.
        Vector3 _lastPosition;
        bool _hasLastPosition;
        float _remoteSpeed;
        float _remoteVerticalSpeed;

        void Start()
        {
            if (body != null) _bodyBase = body.localPosition;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed;
            bool grounded;
            if (controller != null && controller.enabled)
            {
                speed = controller.CurrentSpeed;
                grounded = controller.IsGrounded;
                _hasLastPosition = false;
            }
            else
            {
                SampleRemoteMotion(dt);
                speed = _remoteSpeed;
                grounded = Mathf.Abs(_remoteVerticalSpeed) < 1.2f;
            }

            float amount = Mathf.Clamp01(speed / 4f);
            _phase += speed * dt * 2.2f;
            float swing = grounded ? Mathf.Sin(_phase) * amount * 35f : 25f;

            if (leftLeg) leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            if (rightLeg) rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            if (leftArm) leftArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0, 0);
            if (rightArm) rightArm.localRotation = Quaternion.Euler(swing * 0.8f, 0, 0);

            if (body)
            {
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * amount;
                body.localPosition = _bodyBase + Vector3.up * bob;
                float lean = Mathf.Clamp01((speed - 4f) / 3.5f) * 8f;
                body.localRotation = Quaternion.Euler(lean, 0, 0);
            }
        }

        void SampleRemoteMotion(float dt)
        {
            Vector3 p = transform.position;
            if (_hasLastPosition)
            {
                Vector3 d = p - _lastPosition;
                float horizontal = new Vector2(d.x, d.z).magnitude / dt;
                // Un salto di posizione (teletrasporto) non deve far correre le gambe.
                if (horizontal > 40f) horizontal = 0f;
                float k = 1f - Mathf.Exp(-12f * dt);
                _remoteSpeed = Mathf.Lerp(_remoteSpeed, horizontal, k);
                _remoteVerticalSpeed = Mathf.Lerp(_remoteVerticalSpeed, d.y / dt, k);
            }
            _lastPosition = p;
            _hasLastPosition = true;
        }
    }
}
