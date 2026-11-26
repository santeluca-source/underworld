using UnityEngine;

namespace Underworld.Player
{
    /// <summary>
    /// Animazione provvisoria fatta a codice (finché non c'è un modello con animazioni):
    /// gambe e braccia oscillano, il corpo rimbalza, si inclina quando corre.
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

        void Start()
        {
            if (body != null) _bodyBase = body.localPosition;
        }

        void Update()
        {
            if (controller == null) return;
            float speed = controller.CurrentSpeed;
            float amount = Mathf.Clamp01(speed / 4f);
            _phase += speed * Time.deltaTime * 2.2f;
            float swing = controller.IsGrounded ? Mathf.Sin(_phase) * amount * 35f : 25f;

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
    }
}
