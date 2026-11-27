using System;
using Underworld.Net;
using Underworld.Player;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Server
{
    /// <summary>
    /// Un giocatore collegato a questo server, con il suo salvataggio in memoria. Solo server.
    /// La crea SessionService all'approvazione e la chiude all'uscita (salvataggio finale) o quando
    /// una sessione più nuova dello stesso giocatore la sostituisce.
    /// I moduli non cambiano Save.Cash o Save.Inventory a mano: usano EconomyService e InventoryService.
    /// Per i propri dati: Save.GetModule/SetModule, poi MarkDirty().
    /// </summary>
    public sealed class PlayerSession
    {
        public ulong ClientId => Entry.ClientId;
        public string PlayerId => Entry.PlayerId;
        public string Name => Entry.Name;
        public ServerPlayerEntry Entry { get; }
        public PlayerSave Save { get; }
        /// <summary>Profilo creato con questo ingresso.</summary>
        public bool IsNewAccount { get; }
        /// <summary>Ci sono modifiche non ancora salvate.</summary>
        public bool Dirty { get; private set; }
        public DateTime LastSaveUtc { get; internal set; }
        /// <summary>Quanti salvataggi riusciti in questa sessione.</summary>
        public int SaveCount { get; internal set; }
        /// <summary>Sessione chiusa (uscita, sostituita, server in chiusura): non si cambia più.</summary>
        public bool Closed { get; internal set; }
        /// <summary>Il personaggio è in gioco (dopo lo spawn).</summary>
        public bool Joined => Entry.Joined && Entry.PlayerObject != null;
        public NetworkObject PlayerObject => Entry.PlayerObject;

        /// <summary>Tempo (realtimeSinceStartup) entro cui salvare le modifiche segnate con MarkDirty.</summary>
        internal float SaveDueAt = float.PositiveInfinity;

        internal PlayerSession(ServerPlayerEntry entry, PlayerSave save, bool isNew)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Save = save ?? throw new ArgumentNullException(nameof(save));
            IsNewAccount = isNew;
        }

        /// <summary>
        /// Segna una modifica. important = salva entro SessionService.DebounceSeconds (2 s);
        /// altrimenti al prossimo salvataggio periodico (60 s) o all'uscita.
        /// </summary>
        public void MarkDirty(bool important = true)
        {
            Dirty = true;
            if (important)
            {
                float due = Time.realtimeSinceStartup + SessionService.DebounceSeconds;
                if (due < SaveDueAt) SaveDueAt = due;
            }
        }

        internal void ClearDirty()
        {
            Dirty = false;
            SaveDueAt = float.PositiveInfinity;
        }

        /// <summary>Componente del personaggio (null prima dello spawn o dopo l'uscita).</summary>
        public T GetPlayerComponent<T>() where T : Component
        {
            var obj = Entry.PlayerObject;
            return obj != null ? obj.GetComponent<T>() : null;
        }

        /// <summary>Copia nel salvataggio la posizione che conosce il server (MovementValidator), se il personaggio c'è.</summary>
        internal void CapturePosition()
        {
            var obj = Entry.PlayerObject;
            if (obj == null) return;
            var validator = obj.GetComponent<MovementValidator>();
            Vector3 p = validator != null ? validator.AuthoritativePosition : obj.transform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) return;
            Save.LastPosition = new SavedVector(p.x, p.y, p.z);
        }

        public override string ToString() => $"{Name} ({PlayerId}, client {ClientId})";
    }
}
