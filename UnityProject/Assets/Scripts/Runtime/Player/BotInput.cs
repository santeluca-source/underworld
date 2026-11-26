using System;
using UnityEngine;

namespace Underworld.Player
{
    /// <summary>Comportamento di un bot.</summary>
    public enum BotMode
    {
        /// <summary>Fermo.</summary>
        Idle,
        /// <summary>Cammina dritto; al bordo della zona torna indietro.</summary>
        Walk,
        /// <summary>Gira a caso, corre e salta ogni tanto. Resta vicino al centro.</summary>
        Wander,
        /// <summary>Lo guida uno scenario di test (SetMove, Stop, Jump).</summary>
        Scripted,
    }

    /// <summary>
    /// Comandi automatici per i bot (-bot idle|walk|wander) e per i test (-autotest).
    /// Il movimento è nel mondo (non dipende dalla camera).
    /// </summary>
    public sealed class BotInput : MonoBehaviour, IPlayerInput
    {
        public BotMode Mode = BotMode.Idle;
        [Tooltip("Distanza massima dal centro dell'area (m) per walk e wander.")]
        public float roamRadius = 60f;

        /// <summary>Direzione nel mondo (x = X, y = Z), da -1 a 1.</summary>
        public Vector2 WorldMove { get; private set; }
        public bool Sprint { get; private set; }

        bool _jump;
        float _nextDecision;
        System.Random _rng;

        /// <summary>Aggiunge (o riusa) un BotInput sul personaggio e imposta il modo.</summary>
        public static BotInput Attach(GameObject player, BotMode mode)
        {
            var bot = player.GetComponent<BotInput>();
            if (bot == null) bot = player.AddComponent<BotInput>();
            bot.SetMode(mode);
            return bot;
        }

        /// <summary>Legge il modo dall'argomento -bot (idle, walk, wander). Falso se sconosciuto.</summary>
        public static bool TryParseMode(string text, out BotMode mode) =>
            Enum.TryParse(text ?? string.Empty, true, out mode) && Enum.IsDefined(typeof(BotMode), mode);

        void Awake()
        {
            _rng = new System.Random(unchecked(Environment.TickCount * 31 + GetInstanceID()));
        }

        public void SetMode(BotMode mode)
        {
            Mode = mode;
            WorldMove = Vector2.zero;
            Sprint = false;
            _nextDecision = 0f;
        }

        /// <summary>Muovi verso una direzione del mondo (modo Scripted).</summary>
        public void SetMove(Vector3 worldDirection, bool sprint)
        {
            Mode = BotMode.Scripted;
            var d = new Vector2(worldDirection.x, worldDirection.z);
            WorldMove = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.zero;
            Sprint = sprint;
        }

        /// <summary>Fermati (modo Scripted).</summary>
        public void Stop()
        {
            Mode = BotMode.Scripted;
            WorldMove = Vector2.zero;
            Sprint = false;
        }

        /// <summary>Salta al prossimo frame.</summary>
        public void Jump() => _jump = true;

        public PlayerInputState Read()
        {
            var s = new PlayerInputState { Move = WorldMove, Sprint = Sprint, Jump = _jump, WorldSpace = true };
            _jump = false;
            return s;
        }

        void Update()
        {
            switch (Mode)
            {
                case BotMode.Idle:
                    WorldMove = Vector2.zero;
                    break;
                case BotMode.Walk:
                    UpdateWalk();
                    break;
                case BotMode.Wander:
                    UpdateWander();
                    break;
            }
        }

        void UpdateWalk()
        {
            if (WorldMove == Vector2.zero)
            {
                Vector3 f = transform.forward;
                WorldMove = new Vector2(f.x, f.z).normalized;
                if (WorldMove == Vector2.zero) WorldMove = Vector2.up;
            }
            SteerHomeIfFar();
        }

        void UpdateWander()
        {
            float now = Time.time;
            if (now >= _nextDecision)
            {
                _nextDecision = now + 2f + (float)_rng.NextDouble() * 2.5f;
                if (_rng.NextDouble() < 0.15)
                {
                    WorldMove = Vector2.zero; // una pausa ogni tanto
                }
                else
                {
                    float a = (float)(_rng.NextDouble() * Math.PI * 2.0);
                    WorldMove = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
                Sprint = _rng.NextDouble() < 0.35;
                if (_rng.NextDouble() < 0.25) _jump = true;
            }
            SteerHomeIfFar();
        }

        void SteerHomeIfFar()
        {
            Vector3 p = transform.position;
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude > roamRadius && Vector2.Dot(WorldMove, flat) > 0f)
                WorldMove = (-flat).normalized; // torna verso il centro
        }
    }
}
