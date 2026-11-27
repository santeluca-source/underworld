using System;
using System.Collections.Generic;
using Underworld.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Underworld.Player
{
    /// <summary>Una violazione di movimento vista dal server.</summary>
    public readonly struct MovementViolation
    {
        public readonly MovementViolationKind Kind;
        /// <summary>Ultima posizione valida (dove il giocatore viene rimesso).</summary>
        public readonly Vector3 LastValid;
        /// <summary>Posizione ricevuta che ha fatto scattare il controllo.</summary>
        public readonly Vector3 Received;
        /// <summary>Numero della violazione per questo giocatore (1, 2, 3...).</summary>
        public readonly int Count;

        public MovementViolation(MovementViolationKind kind, Vector3 lastValid, Vector3 received, int count)
        {
            Kind = kind;
            LastValid = lastValid;
            Received = received;
            Count = count;
        }
    }

    /// <summary>
    /// Controllo del movimento sul server (contratto, sezione 4). Il proprietario muove il suo personaggio
    /// (NetworkTransform con autorità del proprietario); il server legge le posizioni ricevute e controlla
    /// velocità orizzontale (a piedi 8,5 m/s), salita, discesa e salti di posizione con un MovementBudget.
    /// Se un controllo fallisce: conta la violazione, scrive il log e chiede al proprietario
    /// ([Rpc(SendTo.Owner)]) di tornare all'ultima posizione valida. Finché il proprietario non è tornato,
    /// le posizioni ricevute non contano. Chi ignora troppe correzioni viene scollegato.
    /// API per gli altri moduli (solo server): SetSpeedAllowance / ClearSpeedAllowance (veicoli),
    /// ServerTeleport (spostamenti decisi dal server).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class MovementValidator : NetworkBehaviour
    {
        [Header("Limiti a piedi")]
        public float maxHorizontalSpeed = 8.5f;
        public float maxUpSpeed = 6f;
        public float maxDownSpeed = 55f;
        [Tooltip("Secondi di credito per la rete a scatti.")]
        public float burstSeconds = 1.5f;

        [Header("Correzioni")]
        [Tooltip("Distanza (m) entro cui il proprietario è considerato tornato al punto corretto.")]
        public float correctionTolerance = 1f;
        [Tooltip("Dopo quanti secondi senza risposta il server ripete la correzione.")]
        public float resendAfterSeconds = 2f;
        [Tooltip("Correzioni ignorate di fila prima di scollegare il giocatore.")]
        public int maxIgnoredCorrections = 5;

        /// <summary>Server: una violazione per qualsiasi giocatore (per log e anti-trucchi dei moduli).</summary>
        public static event Action<MovementValidator, MovementViolation> ViolationDetected;

        /// <summary>Proprietario: il server ci ha rimesso in una posizione (posizione, numero della correzione).</summary>
        public event Action<Vector3, int> CorrectionApplied;

        /// <summary>Server: violazioni contate per questo giocatore.</summary>
        public int ViolationCount { get; private set; }
        /// <summary>Server: correzioni mandate (comprese le ripetizioni).</summary>
        public int CorrectionsSent { get; private set; }
        /// <summary>Server: in attesa che il proprietario torni al punto corretto.</summary>
        public bool AwaitingCorrection => _awaiting;
        /// <summary>Server: ultima posizione accettata.</summary>
        public Vector3 LastValidPosition => _lastValid;

        /// <summary>Proprietario: correzioni ricevute per violazioni (non conta i teletrasporti del server).</summary>
        public int CorrectionsReceived { get; private set; }
        /// <summary>Proprietario: posizione dell'ultima correzione ricevuta.</summary>
        public Vector3 LastCorrectionTarget { get; private set; }

        NetworkTransform _nt;
        CharacterController _cc;
        ThirdPersonController _controller;
        PlayerNetwork _player;

        readonly Dictionary<string, Vector2> _allowances = new Dictionary<string, Vector2>();
        MovementBudget _budget;
        bool _serverActive;
        Vector3 _lastValid;
        Vector3 _lastSeen;
        bool _awaiting;
        Vector3 _awaitTarget;
        float _awaitYaw;
        double _awaitSince;
        int _sequence;
        int _ignoredInARow;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            _nt = GetComponent<NetworkTransform>();
            _cc = GetComponent<CharacterController>();
            _controller = GetComponent<ThirdPersonController>();
            _player = GetComponent<PlayerNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            // Il controllo gira sul server per i personaggi dei client (non per quelli del server stesso).
            _serverActive = IsServer && !IsOwner;
            if (!_serverActive) return;
            _budget = new MovementBudget(CurrentLimits());
            _budget.Reset(Now);
            _lastValid = transform.position;
            _lastSeen = _lastValid;
        }

        public override void OnNetworkDespawn()
        {
            _serverActive = false;
        }

        void Update()
        {
            if (!_serverActive || !IsSpawned) return;

            double now = Now;
            _budget.Advance(now);
            // Ultima posizione mandata dal proprietario (senza l'interpolazione).
            Vector3 pos = _nt.GetSpaceRelativePosition(true);

            if (_awaiting)
            {
                UpdateAwaiting(pos, now);
                return;
            }

            if (pos == _lastSeen) return; // niente di nuovo
            _lastSeen = pos;

            var kind = _budget.Consume(pos - _lastValid);
            if (kind == MovementViolationKind.None)
            {
                _lastValid = pos;
                return;
            }
            RegisterViolation(kind, pos);
        }

        void UpdateAwaiting(Vector3 pos, double now)
        {
            if ((pos - _awaitTarget).sqrMagnitude <= correctionTolerance * correctionTolerance)
            {
                _awaiting = false;
                _ignoredInARow = 0;
                _lastValid = pos;
                _lastSeen = pos;
                _budget.Reset(now);
                return;
            }

            if (now - _awaitSince < resendAfterSeconds) return;

            _ignoredInARow++;
            if (_ignoredInARow >= maxIgnoredCorrections)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} {PlayerLabel} ignora le correzioni di movimento: lo scollego.");
                _serverActive = false;
                NetworkManager.DisconnectClient(OwnerClientId, DisconnectReasons.MovementKick);
                return;
            }
            Debug.LogWarning($"{NetConstants.ServerLog} {PlayerLabel} non è tornato al punto corretto: ripeto la correzione ({_ignoredInARow}/{maxIgnoredCorrections}).");
            SendCorrection(_awaitTarget, _awaitYaw, false);
        }

        void RegisterViolation(MovementViolationKind kind, Vector3 received)
        {
            ViolationCount++;
            float dist = Vector3.Distance(received, _lastValid);
            string what = kind switch
            {
                MovementViolationKind.Teleport => "salto di posizione",
                MovementViolationKind.Vertical => "salita troppo veloce",
                _ => "velocità troppo alta",
            };
            Debug.LogWarning($"{NetConstants.ServerLog} Movimento non valido ({what}): {PlayerLabel} spostamento {dist:0.0} m, " +
                             $"limite {CurrentLimits().HorizontalSpeed:0.0} m/s. Violazioni: {ViolationCount}. Lo rimetto in {_lastValid}.");
            try { ViolationDetected?.Invoke(this, new MovementViolation(kind, _lastValid, received, ViolationCount)); }
            catch (Exception e) { Debug.LogException(e); }
            SendCorrection(_lastValid, transform.eulerAngles.y, false);
        }

        void SendCorrection(Vector3 target, float yaw, bool serverTeleport)
        {
            _sequence++;
            CorrectionsSent++;
            _awaiting = true;
            _awaitTarget = target;
            _awaitYaw = yaw;
            _awaitSince = Now;
            CorrectPositionRpc(target, yaw, _sequence, serverTeleport);
        }

        string PlayerLabel
        {
            get
            {
                if (ServerPlayerRegistry.TryGetByClientId(OwnerClientId, out var e)) return e.ToString();
                return _player != null ? $"{_player.Name} (client {OwnerClientId})" : $"client {OwnerClientId}";
            }
        }

        // ---- API per gli altri moduli (solo server) ----

        /// <summary>Velocità orizzontale permessa adesso (m/s).</summary>
        public float AllowedHorizontalSpeed => CurrentLimits().HorizontalSpeed;

        /// <summary>
        /// Alza i limiti di velocità finché la chiave resta attiva (es. "vehicle" quando il giocatore guida).
        /// Vale il valore più alto tra i limiti a piedi e tutte le chiavi attive. Solo server.
        /// </summary>
        public void SetSpeedAllowance(string key, float horizontalSpeed, float upSpeed = 0f)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[MovementValidator] SetSpeedAllowance vale solo sul server.");
                return;
            }
            _allowances[key ?? string.Empty] = new Vector2(horizontalSpeed, upSpeed);
            _budget?.SetLimits(CurrentLimits());
        }

        /// <summary>Toglie un limite alzato con SetSpeedAllowance. Consiglio: tienilo ancora 1-2 s dopo l'uscita da un veicolo.</summary>
        public void ClearSpeedAllowance(string key)
        {
            if (_allowances.Remove(key ?? string.Empty)) _budget?.SetLimits(CurrentLimits());
        }

        /// <summary>Sposta il giocatore in un punto deciso dal server (non conta come violazione). Solo server.</summary>
        public void ServerTeleport(Vector3 position, float yaw)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[MovementValidator] ServerTeleport vale solo sul server, con il giocatore in gioco.");
                return;
            }
            _lastValid = position;
            _lastSeen = position;
            SendCorrection(position, yaw, true);
        }

        MovementLimits CurrentLimits()
        {
            var l = MovementLimits.OnFoot;
            l.HorizontalSpeed = maxHorizontalSpeed;
            l.UpSpeed = maxUpSpeed;
            l.DownSpeed = maxDownSpeed;
            l.BurstSeconds = burstSeconds;
            foreach (var a in _allowances.Values)
            {
                if (a.x > l.HorizontalSpeed) l.HorizontalSpeed = a.x;
                if (a.y > l.UpSpeed) l.UpSpeed = a.y;
            }
            return l;
        }

        // ---- Proprietario ----

        [Rpc(SendTo.Owner)]
        void CorrectPositionRpc(Vector3 position, float yaw, int sequence, bool serverTeleport)
        {
            if (!IsOwner) return;
            ApplyCorrection(position, yaw);
            if (!serverTeleport)
            {
                CorrectionsReceived++;
                LastCorrectionTarget = position;
                Debug.Log($"{NetConstants.ClientLog} Correzione del server n. {sequence}: torno in {position}.");
            }
            try { CorrectionApplied?.Invoke(position, sequence); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void ApplyCorrection(Vector3 position, float yaw)
        {
            bool ccEnabled = _cc != null && _cc.enabled;
            if (_cc != null) _cc.enabled = false; // il CharacterController non deve "tirare indietro" la posizione
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            if (_nt != null && _nt.CanCommitToTransform) _nt.Teleport(position, rot, transform.localScale);
            else transform.SetPositionAndRotation(position, rot);
            if (_cc != null) _cc.enabled = ccEnabled;
            if (_controller != null) _controller.ResetMotion();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ViolationDetected = null;
    }
}
