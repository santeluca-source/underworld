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
    /// Controllo del movimento sul server (contratto, sezione 5). Il proprietario muove il suo personaggio
    /// (NetworkTransform con autorità del proprietario); il server legge le posizioni ricevute e controlla:
    /// posizione finita e dentro il mondo, credito di velocità (MovementBudget: orizzontale 8,5 m/s, salita,
    /// discesa, salti di posizione), volo e caduta (AirborneTracker, con una spherecast del server sotto i piedi).
    /// Se un controllo fallisce: conta la violazione, scrive il log e chiede al proprietario
    /// ([Rpc(SendTo.Owner)], solo il server può chiamarla) di tornare all'ultima posizione valida.
    /// Finché il proprietario non è tornato, le posizioni ricevute non contano: per il gioco del server vale
    /// AuthoritativePosition. Chi non torna entro circa 2 s, o fa troppe violazioni in poco tempo
    /// (finestra scorrevole), viene scollegato.
    /// API per gli altri moduli (solo server): SetSpeedAllowance / ClearSpeedAllowance (veicoli, scale),
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
        [Tooltip("Quota minima del mondo (m): più in basso = fuori mappa.")]
        public float minY = -100f;

        [Header("Correzioni")]
        [Tooltip("Distanza (m) entro cui il proprietario è considerato tornato al punto corretto.")]
        public float correctionTolerance = 1f;
        [Tooltip("Dopo quanti secondi senza risposta il server ripete la correzione.")]
        public float resendAfterSeconds = 0.6f;
        [Tooltip("Correzioni ignorate di fila prima di scollegare il giocatore (0,6 s × 3 = circa 1,8 s).")]
        public int maxIgnoredCorrections = 3;
        [Tooltip("Violazioni nella finestra scorrevole che portano alla disconnessione.")]
        public int maxViolationsInWindow = 4;
        [Tooltip("Durata (s) della finestra scorrevole delle violazioni.")]
        public float violationWindowSeconds = 30f;

        /// <summary>Raggio (m) della spherecast che cerca il suolo sotto i piedi.</summary>
        const float GroundProbeRadius = 0.4f;
        /// <summary>Distanza (m) sotto i piedi entro cui il giocatore è "a terra".</summary>
        const float GroundProbeDepth = 0.45f;

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
        /// <summary>Server: ultima posizione accettata (durante una correzione: il punto corretto).</summary>
        public Vector3 LastValidPosition => _lastValid;
        /// <summary>
        /// Server: posizione da usare per il gioco (distanze, raccolte, trigger). Durante una correzione è il punto
        /// corretto, non quello mandato dal client (che il NetworkTransform continua a mostrare agli altri).
        /// </summary>
        public Vector3 AuthoritativePosition => _serverActive ? (_awaiting ? _awaitTarget : _lastValid) : transform.position;

        /// <summary>Proprietario: correzioni ricevute per violazioni (non conta i teletrasporti del server).</summary>
        public int CorrectionsReceived { get; private set; }
        /// <summary>Proprietario: posizione dell'ultima correzione ricevuta.</summary>
        public Vector3 LastCorrectionTarget { get; private set; }
        /// <summary>Proprietario: correzioni rifiutate perché non mandate dal server.</summary>
        public int CorrectionsRejected { get; private set; }

        /// <summary>Limiti alzati da un modulo (veicolo, scala).</summary>
        struct Allowance
        {
            public float Horizontal, Up, Down;
            public bool Flight;
        }

        NetworkTransform _nt;
        CharacterController _cc;
        ThirdPersonController _controller;
        PlayerNetwork _player;

        readonly Dictionary<string, Allowance> _allowances = new Dictionary<string, Allowance>();
        readonly RaycastHit[] _probeHits = new RaycastHit[8];
        readonly AirborneTracker _air = new AirborneTracker();
        MovementBudget _budget;
        ViolationWindow _window;
        bool _serverActive;
        Vector3 _lastValid;
        Vector3 _lastGrounded;
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
            _window = new ViolationWindow(violationWindowSeconds);
            ResetAfterCorrection(transform.position, Now);
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

            var limits = CurrentLimits();
            if (pos == _lastSeen) // niente di nuovo (con un NaN il confronto è falso: il controllo scatta)
            {
                // Fermo in aria senza mandare posizioni nuove: il controllo della caduta continua lo stesso.
                if (_air.Airborne && !limits.AllowFlight && _air.Step(pos, false, now, limits) != MovementViolationKind.None)
                    RegisterViolation(MovementViolationKind.Vertical, pos, _lastGrounded);
                return;
            }
            _lastSeen = pos;

            // Prima di tutto la posizione stessa: un NaN o un punto fuori dal mondo non diventa mai _lastValid.
            if (!limits.IsSanePosition(pos))
            {
                RegisterViolation(MovementViolationKind.Teleport, pos, _lastValid);
                return;
            }

            var kind = _budget.Consume(pos - _lastValid);
            if (kind != MovementViolationKind.None)
            {
                RegisterViolation(kind, pos, _lastValid);
                return;
            }

            bool grounded = limits.AllowFlight || IsGroundedAt(pos);
            kind = _air.Step(pos, grounded, now, limits);
            if (kind != MovementViolationKind.None)
            {
                // Volo o caduta impossibile: torna all'ultimo punto con i piedi a terra.
                RegisterViolation(kind, pos, _lastGrounded);
                return;
            }
            _lastValid = pos;
            if (grounded) _lastGrounded = pos;
        }

        /// <summary>Spherecast del server sotto i piedi (raggio poco più grande della capsula: regge anche sul bordo di un gradino). Ignora questo giocatore, i trigger e i muri.</summary>
        bool IsGroundedAt(Vector3 feet)
        {
            const float lift = 0.5f;
            Vector3 origin = feet + Vector3.up * (GroundProbeRadius + lift);
            int n = Physics.SphereCastNonAlloc(origin, GroundProbeRadius, Vector3.down, _probeHits,
                lift + GroundProbeDepth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = _probeHits[i];
                var c = hit.collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                // Sfera già dentro qualcosa alla partenza (es. un muro): non è il suolo.
                if (hit.distance <= 0f && hit.point == Vector3.zero) continue;
                // Solo superfici che reggono (non i muri verticali): niente arrampicate sui muri.
                if (hit.normal.y < 0.25f) continue;
                return true;
            }
            return false;
        }

        /// <summary>Server: punto di partenza nuovo (spawn, correzione riuscita): credito pieno, volo da capo.</summary>
        void ResetAfterCorrection(Vector3 pos, double now)
        {
            _lastValid = pos;
            _lastSeen = pos;
            _lastGrounded = pos;
            _air.Reset(pos.y);
            _budget.Reset(now);
        }

        void UpdateAwaiting(Vector3 pos, double now)
        {
            // Con un NaN anche questo confronto è falso: si resta in attesa.
            if ((pos - _awaitTarget).sqrMagnitude <= correctionTolerance * correctionTolerance)
            {
                // Tornato al punto corretto. Le violazioni restano contate nella finestra scorrevole.
                _awaiting = false;
                _ignoredInARow = 0;
                ResetAfterCorrection(pos, now);
                return;
            }

            if (now - _awaitSince < resendAfterSeconds) return;

            _ignoredInARow++;
            if (_ignoredInARow >= maxIgnoredCorrections)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} {PlayerLabel} ignora le correzioni di movimento: lo scollego.");
                Kick();
                return;
            }
            Debug.LogWarning($"{NetConstants.ServerLog} {PlayerLabel} non è tornato al punto corretto: ripeto la correzione ({_ignoredInARow}/{maxIgnoredCorrections}).");
            SendCorrection(_awaitTarget, _awaitYaw, false);
        }

        void RegisterViolation(MovementViolationKind kind, Vector3 received, Vector3 target)
        {
            ViolationCount++;
            int inWindow = _window.Add(Now);
            float dist = Vector3.Distance(received, _lastValid);
            string what = kind switch
            {
                MovementViolationKind.Teleport => "salto di posizione",
                MovementViolationKind.Vertical => "volo o caduta impossibile",
                _ => "velocità troppo alta",
            };
            Debug.LogWarning($"{NetConstants.ServerLog} Movimento non valido ({what}): {PlayerLabel} spostamento {dist:0.0} m, " +
                             $"limite {CurrentLimits().HorizontalSpeed:0.0} m/s. Violazioni: {ViolationCount} " +
                             $"({inWindow} in {violationWindowSeconds:0} s). Lo rimetto in {target}.");
            try { ViolationDetected?.Invoke(this, new MovementViolation(kind, _lastValid, received, ViolationCount)); }
            catch (Exception e) { Debug.LogException(e); }
            if (inWindow >= maxViolationsInWindow)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} {PlayerLabel}: {inWindow} violazioni in {violationWindowSeconds:0} s, lo scollego.");
                Kick();
                return;
            }
            _lastValid = target;
            SendCorrection(target, transform.eulerAngles.y, false);
        }

        void Kick()
        {
            _serverActive = false;
            NetworkManager.DisconnectClient(OwnerClientId, DisconnectReasons.MovementKick);
        }

        void SendCorrection(Vector3 target, float yaw, bool serverTeleport)
        {
            _sequence++;
            CorrectionsSent++;
            _awaiting = true;
            _awaitTarget = target;
            _awaitYaw = yaw;
            _awaitSince = Now;
            CorrectPositionRpc(target, yaw, _sequence, serverTeleport, default);
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
        /// Alza i limiti finché la chiave resta attiva (es. "vehicle" quando il giocatore guida; "ladder" su una scala
        /// con allowFlight = true). Vale il valore più alto tra i limiti a piedi e tutte le chiavi attive;
        /// allowFlight spegne i controlli di volo e caduta. Solo server.
        /// </summary>
        public void SetSpeedAllowance(string key, float horizontalSpeed, float upSpeed = 0f, float downSpeed = 0f, bool allowFlight = false)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[MovementValidator] SetSpeedAllowance vale solo sul server.");
                return;
            }
            _allowances[key ?? string.Empty] = new Allowance { Horizontal = horizontalSpeed, Up = upSpeed, Down = downSpeed, Flight = allowFlight };
            _budget?.SetLimits(CurrentLimits());
        }

        /// <summary>Toglie un limite alzato con SetSpeedAllowance. Consiglio: tienilo ancora 1-2 s dopo l'uscita da un veicolo.</summary>
        public void ClearSpeedAllowance(string key)
        {
            if (!_allowances.Remove(key ?? string.Empty)) return;
            _budget?.SetLimits(CurrentLimits());
            if (_serverActive) _air.Reset(_lastValid.y); // il controllo del volo riparte da qui
        }

        /// <summary>Sposta il giocatore in un punto deciso dal server (non conta come violazione). Solo server.</summary>
        public void ServerTeleport(Vector3 position, float yaw)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[MovementValidator] ServerTeleport vale solo sul server, con il giocatore in gioco.");
                return;
            }
            if (_serverActive) ResetAfterCorrection(position, Now);
            SendCorrection(position, yaw, true);
        }

        MovementLimits CurrentLimits()
        {
            var l = MovementLimits.OnFoot;
            l.HorizontalSpeed = maxHorizontalSpeed;
            l.UpSpeed = maxUpSpeed;
            l.DownSpeed = maxDownSpeed;
            l.BurstSeconds = burstSeconds;
            l.MinY = minY;
            foreach (var a in _allowances.Values)
            {
                if (a.Horizontal > l.HorizontalSpeed) l.HorizontalSpeed = a.Horizontal;
                if (a.Up > l.UpSpeed) l.UpSpeed = a.Up;
                if (a.Down > l.DownSpeed) l.DownSpeed = a.Down;
                if (a.Flight) l.AllowFlight = true;
            }
            return l;
        }

        // ---- Proprietario ----

        /// <summary>Vero se una correzione arriva davvero dal server: un altro client non può spostarci.</summary>
        public static bool IsTrustedCorrectionSender(ulong senderClientId) => senderClientId == Unity.Netcode.NetworkManager.ServerClientId;

        // InvokePermission = Server: NGO scarta la chiamata di un client invece di inoltrarla al proprietario.
        // Il controllo del mittente qui dentro è una seconda difesa.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void CorrectPositionRpc(Vector3 position, float yaw, int sequence, bool serverTeleport, RpcParams rpcParams)
        {
            if (!IsOwner) return;
            if (!IsTrustedCorrectionSender(rpcParams.Receive.SenderClientId))
            {
                CorrectionsRejected++;
                Debug.LogWarning($"{NetConstants.ClientLog} Correzione rifiutata: il mittente {rpcParams.Receive.SenderClientId} non è il server.");
                return;
            }
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) || !float.IsFinite(yaw)) return;
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
