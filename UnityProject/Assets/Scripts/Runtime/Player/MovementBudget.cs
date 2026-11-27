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

        /// <summary>Salita massima (m) sopra l'ultimo punto con i piedi a terra: salto 1,3 m + margine.</summary>
        public float MaxAscent;
        /// <summary>Secondi in aria senza controllo della caduta (salto + rete a scatti).</summary>
        public float FallGrace;
        /// <summary>Gravità minima (m/s²) che il server pretende dopo FallGrace (il gioco usa 22: qui molto meno, per la rete).</summary>
        public float MinFallGravity;
        /// <summary>Gravità massima (m/s²) di una caduta vera (più veloce = discesa impossibile, es. sotto la mappa).</summary>
        public float MaxFallGravity;
        /// <summary>Quota minima del mondo (m): più in basso = fuori mappa.</summary>
        public float MinY;
        /// <summary>Distanza massima dall'origine su ogni asse (m): oltre = posizione impossibile.</summary>
        public float WorldBound;
        /// <summary>Vero = niente controlli di volo e caduta (scale, veicoli che volano: lo decide un modulo).</summary>
        public bool AllowFlight;

        public static MovementLimits OnFoot => new MovementLimits
        {
            HorizontalSpeed = 8.5f,
            UpSpeed = 6f,
            DownSpeed = 55f,
            BurstSeconds = 1.5f,
            Slack = 1.5f,
            MaxAscent = 2.5f,
            FallGrace = 1f,
            MinFallGravity = 8f,
            MaxFallGravity = 30f,
            MinY = -100f,
            WorldBound = 10000f,
            AllowFlight = false,
        };

        /// <summary>Spostamento orizzontale massimo "in un colpo" (m).</summary>
        public float HorizontalCapacity => HorizontalSpeed * BurstSeconds + Slack;
        /// <summary>Salita massima "in un colpo" (m): più stretta dell'orizzontale.</summary>
        public float UpCapacity => UpSpeed * 0.5f + 2f;
        /// <summary>Discesa massima "in un colpo" (m): circa 0,3 s di caduta veloce.</summary>
        public float DownCapacity => DownSpeed * 0.3f + Slack;

        /// <summary>Posizione possibile: numeri finiti (niente NaN o infinito) e dentro i confini del mondo.</summary>
        public bool IsSanePosition(Vector3 p)
        {
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) return false;
            return Mathf.Abs(p.x) <= WorldBound && Mathf.Abs(p.y) <= WorldBound && Mathf.Abs(p.z) <= WorldBound;
        }
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
            // NaN o infinito: ogni confronto sarebbe falso e il credito diventerebbe NaN per sempre.
            if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y) || !float.IsFinite(delta.z))
                return MovementViolationKind.Teleport;
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

    /// <summary>
    /// Controllo del volo sul server (logica pura, testata in EditMode): ricorda la quota dell'ultimo punto
    /// con i piedi a terra e da quanto tempo il giocatore è in aria. Violazione (Vertical) se:
    /// sale più di MaxAscent sopra quel punto; resta in aria senza cadere (dopo FallGrace deve scendere
    /// almeno come una gravità MinFallGravity); scende più veloce di una caduta vera (MaxFallGravity);
    /// va sotto MinY.
    /// </summary>
    public sealed class AirborneTracker
    {
        float _groundY;
        double _airSince;

        /// <summary>Vero se l'ultimo campione era in aria.</summary>
        public bool Airborne { get; private set; }
        /// <summary>Quota dell'ultimo punto con i piedi a terra.</summary>
        public float GroundY => _groundY;

        /// <summary>Ricomincia come se il giocatore fosse a terra a questa quota (spawn, correzione).</summary>
        public void Reset(float groundY)
        {
            _groundY = groundY;
            Airborne = false;
        }

        /// <summary>Controlla un campione accettato dal credito. grounded = il server trova il suolo sotto i piedi.</summary>
        public MovementViolationKind Step(Vector3 pos, bool grounded, double now, in MovementLimits limits)
        {
            if (pos.y < limits.MinY) return MovementViolationKind.Vertical;
            if (limits.AllowFlight || grounded)
            {
                Reset(pos.y);
                return MovementViolationKind.None;
            }
            if (!Airborne)
            {
                Airborne = true;
                _airSince = now;
            }
            float t = (float)(now - _airSince);

            // Troppo in alto sopra l'ultimo suolo (volo, super salto).
            if (pos.y - _groundY > limits.MaxAscent) return MovementViolationKind.Vertical;

            // Sospeso in aria: dopo FallGrace la quota massima scende come una caduta lenta.
            float late = t - limits.FallGrace;
            if (late > 0f && pos.y > _groundY + limits.MaxAscent - 0.5f * limits.MinFallGravity * late * late)
                return MovementViolationKind.Vertical;

            // Giù più veloce di qualsiasi caduta vera (es. un tuffo istantaneo sotto la mappa).
            float fallTime = t + 0.5f; // margine per la rete a scatti
            if (_groundY - pos.y > limits.Slack + 0.5f * limits.MaxFallGravity * fallTime * fallTime)
                return MovementViolationKind.Vertical;

            return MovementViolationKind.None;
        }
    }

    /// <summary>Conta le violazioni negli ultimi N secondi (finestra scorrevole). Logica pura.</summary>
    public sealed class ViolationWindow
    {
        readonly System.Collections.Generic.Queue<double> _times = new System.Collections.Generic.Queue<double>();
        public float WindowSeconds { get; }

        public ViolationWindow(float windowSeconds)
        {
            WindowSeconds = windowSeconds;
        }

        /// <summary>Aggiunge una violazione e restituisce quante ce ne sono nella finestra (compresa questa).</summary>
        public int Add(double now)
        {
            _times.Enqueue(now);
            return Count(now);
        }

        /// <summary>Violazioni negli ultimi WindowSeconds.</summary>
        public int Count(double now)
        {
            while (_times.Count > 0 && now - _times.Peek() > WindowSeconds) _times.Dequeue();
            return _times.Count;
        }
    }
}
