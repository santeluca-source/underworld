using UnityEngine;
using UnityEngine.InputSystem;

namespace Underworld.Player
{
    /// <summary>
    /// Comandi da tastiera e gamepad: WASD o levetta sinistra, Shift o click della levetta per correre,
    /// Spazio o tasto A per saltare. La camera legge il mouse da sola (ThirdPersonCamera).
    /// </summary>
    public sealed class KeyboardMouseInput : IPlayerInput
    {
        public PlayerInputState Read()
        {
            var s = new PlayerInputState();

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) s.Move.y += 1f;
                if (kb.sKey.isPressed) s.Move.y -= 1f;
                if (kb.dKey.isPressed) s.Move.x += 1f;
                if (kb.aKey.isPressed) s.Move.x -= 1f;
                s.Sprint = kb.leftShiftKey.isPressed;
                s.Jump = kb.spaceKey.wasPressedThisFrame;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                s.Move += gp.leftStick.ReadValue();
                s.Sprint |= gp.leftStickButton.isPressed;
                s.Jump |= gp.buttonSouth.wasPressedThisFrame;
            }

            s.Move = Vector2.ClampMagnitude(s.Move, 1f);
            return s;
        }
    }
}
