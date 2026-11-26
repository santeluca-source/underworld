using System;
using System.Collections;
using System.Collections.Generic;
using Underworld.Core;
using Underworld.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Player
{
    /// <summary>
    /// Parte di rete del personaggio (prefab Player).
    /// - Nome visibile: NetworkVariable scritta solo dal server (dal ServerPlayerRegistry).
    /// - Proprietario: accende il ThirdPersonController, sceglie i comandi (tastiera o bot)
    ///   e collega la camera principale della scena.
    /// - Altri giocatori: controller spento; PlayerVisual anima dal movimento ricevuto; targhetta col nome.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNetwork : NetworkBehaviour
    {
        /// <summary>Altezza della targhetta col nome sopra i piedi (m).</summary>
        public float nameplateHeight = 2.25f;

        readonly NetworkVariable<FixedString128Bytes> _displayName = new NetworkVariable<FixedString128Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Punto di spawn deciso dal server (vedi EnsureSpawnPosition).
        readonly NetworkVariable<Vector3> _spawnPosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<float> _spawnYaw = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        static readonly List<PlayerNetwork> Spawned = new List<PlayerNetwork>();

        /// <summary>Tutti i personaggi in gioco visti da questo processo (client o server).</summary>
        public static IReadOnlyList<PlayerNetwork> All => Spawned;

        /// <summary>Il personaggio del giocatore locale (null sul server o prima dello spawn).</summary>
        public static PlayerNetwork Local { get; private set; }

        /// <summary>Il personaggio locale è pronto (camera collegata, comandi attivi).</summary>
        public static event Action<PlayerNetwork> LocalPlayerSpawned;

        /// <summary>Il personaggio locale è stato tolto (uscita, disconnessione).</summary>
        public static event Action LocalPlayerDespawned;

        /// <summary>Nome visibile del giocatore.</summary>
        public string Name => _displayName.Value.ToString();

        public ThirdPersonController Controller { get; private set; }
        public MovementValidator Validator { get; private set; }
        public BotInput Bot { get; private set; }

        Nameplate _nameplate;
        CharacterController _cc;
        CapsuleCollider _remoteCollider;

        void Awake()
        {
            Controller = GetComponent<ThirdPersonController>();
            Validator = GetComponent<MovementValidator>();
            _cc = GetComponent<CharacterController>();
            _remoteCollider = GetComponent<CapsuleCollider>();
        }

        public override void OnNetworkSpawn()
        {
            if (!Spawned.Contains(this)) Spawned.Add(this);

            if (IsServer)
            {
                // Il nome viene dal registro (già controllato dall'approvazione): mai dal client.
                if (ServerPlayerRegistry.TryGetByClientId(OwnerClientId, out var entry))
                    _displayName.Value = new FixedString128Bytes(entry.Name);
                gameObject.name = $"Player_{OwnerClientId}_{Name}";
                _spawnPosition.Value = transform.position;
                _spawnYaw.Value = transform.eulerAngles.y;
            }

            // Il controller parte spento: lo accende EnsureSpawnPosition sul proprietario.
            if (Controller != null) Controller.enabled = false;

            // Solo il proprietario muove il CharacterController. Le copie degli altri giocatori (e quelle
            // del server) usano un collider semplice: due CharacterController sovrapposti bloccano la fisica.
            if (_cc != null) _cc.enabled = IsOwner;
            if (_remoteCollider != null) _remoteCollider.enabled = !IsOwner;

            if (IsOwner)
            {
                StartCoroutine(EnsureSpawnPosition());
            }
            else if (!Application.isBatchMode)
            {
                // Targhetta solo per gli altri giocatori, solo dove c'è una grafica.
                _nameplate = Nameplate.Create(transform, nameplateHeight);
                _nameplate.SetText(Name);
            }

            _displayName.OnValueChanged += OnNameChanged;
        }

        public override void OnNetworkDespawn()
        {
            _displayName.OnValueChanged -= OnNameChanged;
            Spawned.Remove(this);
            if (Local == this)
            {
                Local = null;
                try { LocalPlayerDespawned?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void OnNameChanged(FixedString128Bytes previous, FixedString128Bytes current)
        {
            if (_nameplate != null) _nameplate.SetText(current.ToString());
            if (IsServer) gameObject.name = $"Player_{OwnerClientId}_{current}";
        }

        /// <summary>
        /// Proprietario: nei primi frame Netcode può rimettere il personaggio in (0,0,0) se il suo stato
        /// iniziale arriva vuoto (ingresso di più client insieme). Qui controllo la posizione contro il punto
        /// di spawn deciso dal server e, se serve, la correggo con un teletrasporto del proprietario.
        /// Poi accendo comandi e camera.
        /// </summary>
        IEnumerator EnsureSpawnPosition()
        {
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            Vector3 spawn = _spawnPosition.Value;
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
                if (!IsSpawned) yield break;
                Vector3 d = transform.position - spawn;
                if (d.sqrMagnitude > 0.25f)
                {
                    Debug.LogWarning($"{NetConstants.ClientLog} Posizione iniziale sbagliata {transform.position}: torno allo spawn {spawn}.");
                    bool ccOn = _cc != null && _cc.enabled;
                    if (_cc != null) _cc.enabled = false;
                    Quaternion rot = Quaternion.Euler(0f, _spawnYaw.Value, 0f);
                    if (nt != null && nt.CanCommitToTransform) nt.Teleport(spawn, rot, transform.localScale);
                    else transform.SetPositionAndRotation(spawn, rot);
                    if (_cc != null) _cc.enabled = ccOn;
                }
            }
            if (Controller != null)
            {
                Controller.ResetMotion();
                Controller.enabled = true;
            }
            SetupLocalPlayer();
        }

        void SetupLocalPlayer()
        {
            Local = this;
            gameObject.name = "LocalPlayer";

            // Comandi: bot (-bot o test automatici) oppure tastiera e mouse.
            var args = LaunchArgs.Current;
            if (!string.IsNullOrEmpty(args.Bot) || !string.IsNullOrEmpty(args.AutoTest))
            {
                BotMode mode = BotMode.Idle;
                if (!string.IsNullOrEmpty(args.Bot) && !BotInput.TryParseMode(args.Bot, out mode))
                {
                    Debug.LogWarning($"{NetConstants.ClientLog} Bot sconosciuto '{args.Bot}': uso idle.");
                    mode = BotMode.Idle;
                }
                Bot = BotInput.Attach(gameObject, mode);
                if (Controller != null) Controller.InputSource = Bot;
            }
            else if (Controller != null)
            {
                Controller.InputSource = new KeyboardMouseInput();
            }

            var cam = ThirdPersonCamera.AttachToMainCamera(transform);
            if (Controller != null) Controller.cameraTransform = cam.transform;

            Debug.Log($"{NetConstants.ClientLog} Personaggio locale pronto in {transform.position} (client {OwnerClientId}).");
            try { LocalPlayerSpawned?.Invoke(this); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Trova un personaggio per nome (per i test e i moduli). Null se non c'è.</summary>
        public static PlayerNetwork FindByName(string name)
        {
            for (int i = 0; i < Spawned.Count; i++)
                if (Spawned[i] != null && string.Equals(Spawned[i].Name, name, StringComparison.Ordinal)) return Spawned[i];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Spawned.Clear();
            Local = null;
            LocalPlayerSpawned = null;
            LocalPlayerDespawned = null;
        }
    }
}
