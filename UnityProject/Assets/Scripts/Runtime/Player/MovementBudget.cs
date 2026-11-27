using UnityEngine;

namespace Underworld.Player
{
    /// <summary>Limiti di movimento che il server accetta.</summary>
    public struct MovementLimits
    {
        /// <summary>Velocità orizzontale massima (m/s). A piedi 8,5 (corsa 7,5 + margine).</summary>
        public float HorizontalSpeed;
        /// <summary>Salita massima (m/s): salti, scale, rampe.</summary>
        public float UpSpeed;
        /// <summary>Discesa massima (m/s): cadute.</summary>
        public float DownSpeed;
        /// <summary>Secondi di "credito" orizzontale: assorbe la rete a scatti.</summary>
        public float BurstSeconds;
        /// <summary>Metri extra sempre permessi (piccoli errori, gradini).</summary>
        public float Slack;

        public static MovementLimits OnFoot => new MovementLimits
        {
            HorizontalSpeed = 8.5f,
            UpSpeed = 6f,
            DownSpeed = 55f,
            BurstSeconds = 1.5f,
            Slack = 1.5f,
        };

        /// <summary>Spostamento orizzontale massimo "in un colpo" (m).</summary>
        public float HorizontalCapacity => HorizontalSpeed * BurstSeconds + Slack;
        /// <summary>Salita massima "in un colpo" (m): più stretta dell'orizzontale.</summary>
        public float UpCapacity => UpSpeed * 0.5f + 2f;
        /// <summary>Discesa massima "in un colpo" (m).</summary>
        public float DownCapacity => DownSpeed + Slack;
    }

    public enum MovementViolationKind
    {
        None,
        /// <summary>Troppo veloce per troppo tempo.</summary>
        Speed,
        /// <summary>Un solo spostamento più grande del credito massimo.</summary>
        Teleport,
        /// <summary>Sale troppo (volo).</summary>
        Vertical,
    }

    /// <summary>
    /// Controllo della velocità "a credito" (come un secchio di gettoni, in metri):
    /// il credito cresce con il tempo del server (velocità massima × secondi) fino a un tetto;
    /// ogni spostamento ricevuto lo consuma. Credito sotto zero = violazione.
    /// Il tetto permette i pacchetti che arrivano a gruppi dopo un ritardo della rete.
    /// Logica pura (testata in EditMode).
    /// </summary>
    public sealed class MovementBudget
    {
        public MovementLimits Limits { get; private set; }
        /// <summary>Credito orizzontale (m).</summary>
        public float Horizontal { get; private set; }
        /// <summary>Credito in salita (m).</summary>
        public float Up { get; private set; }
        /// <summary>Credito in discesa (m).</summary>
        public float Down { get; private set; }

        double _lastTime;
        bool _initialized;

        public MovementBudget(MovementLimits limits)
        {
            Limits = limits;
        }

        /// <summary>Cambia i limiti (per esempio un veicolo). Il credito non supera il nuovo tetto.</summary>
        public void SetLimits(MovementLimits limits)
        {
            Limits = limits;
            Horizontal = Mathf.Min(Horizontal, limits.HorizontalCapacity);
            Up = Mathf.Min(Up, limits.UpCapacity);
            Down = Mathf.Min(Down, limits.DownCapacity);
        }

        /// <summary>Credito pieno.</summary>
        public void Reset(double now)
        {
            Horizontal = Limits.HorizontalCapacity;
            Up = Limits.UpCapacity;
            Down = Limits.DownCapacity;
            _lastTime = now;
            _initialized = true;
        }

        /// <summary>Aggiunge il credito del tempo passato. Chiamalo prima di Consume.</summary>
        public void Advance(double now)
        {
            if (!_initialized)
            {
                Reset(now);
                return;
            }
            double dt = now - _lastTime;
            if (dt <= 0) return;
            _lastTime = now;
            if (dt > 10) dt = 10;
            float t = (float)dt;
            Horizontal = Mathf.Min(Limits.HorizontalCapacity, Horizontal + Limits.HorizontalSpeed * t);
            Up = Mathf.Min(Limits.UpCapacity, Up + Limits.UpSpeed * t);
            Down = Mathf.Min(Limits.DownCapacity, Down + Limits.DownSpeed * t);
        }

        /// <summary>Consuma il credito per uno spostamento. Restituisce il tipo di violazione (None = valido).</summary>
        public MovementViolationKind Consume(Vector3 delta)
        {
            float h = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            float up = Mathf.Max(0f, delta.y);
            float down = Mathf.Max(0f, -delta.y);

            bool teleport = h > Limits.HorizontalCapacity || up > Limits.UpCapacity || down > Limits.DownCapacity;
            Horizontal -= h;
            Up -= up;
            Down -= down;

            if (teleport) return MovementViolationKind.Teleport;
            if (Horizontal < 0f || Down < 0f) return MovementViolationKind.Speed;
            if (Up < 0f) return MovementViolationKind.Vertical;
            return MovementViolationKind.None;
        }
    }
}
