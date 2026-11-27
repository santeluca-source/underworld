using System;
using Underworld.Core;
using Unity.Netcode;

namespace Underworld.Economy
{
    /// <summary>
    /// Soldi del giocatore sul suo personaggio (prefab Player, aggiunto da EconomyPlayerContributor).
    /// NetworkVariable leggibile solo dal proprietario, scritta solo dal server (da EconomyService).
    /// Il proprietario manda il valore a HudBus.SetCash: l'HUD mostra i soldi veri.
    /// Il client non può cambiare il saldo: può solo leggerlo.
    /// </summary>
    public sealed class PlayerWallet : NetworkBehaviour
    {
        readonly NetworkVariable<long> _cash = new NetworkVariable<long>(
            0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        /// <summary>Il portafoglio del giocatore locale (null sul server o prima dello spawn).</summary>
        public static PlayerWallet Local { get; private set; }

        /// <summary>Saldo del giocatore locale cambiato (nuovo valore).</summary>
        public static event Action<long> LocalCashChanged;

        /// <summary>Saldo conosciuto da questo processo (proprietario o server).</summary>
        public long Cash => _cash.Value;

        /// <summary>Vero dopo il primo valore mandato all'HUD.</summary>
        public bool HasValue { get; private set; }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner || IsServer) return;
            Local = this;
            _cash.OnValueChanged += OnCashChanged;
            // Il valore della creazione arriva già con lo spawn; i cambi dopo con OnValueChanged.
            Publish(_cash.Value);
        }

        public override void OnNetworkDespawn()
        {
            _cash.OnValueChanged -= OnCashChanged;
            if (Local == this) Local = null;
        }

        void OnCashChanged(long previous, long current) => Publish(current);

        void Publish(long value)
        {
            HasValue = true;
            HudBus.SetCash(value);
            LocalCashChanged?.Invoke(value);
        }

        /// <summary>Solo server (EconomyService). Sul client non fa niente.</summary>
        internal void ServerSetCash(long value)
        {
            if (!IsServer || !IsSpawned) return;
            _cash.Value = value;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
            LocalCashChanged = null;
        }
    }
}
