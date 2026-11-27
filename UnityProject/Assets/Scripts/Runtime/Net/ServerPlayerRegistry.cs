using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Un giocatore approvato dal server. Lo crea l'approvazione; resta finché il client è collegato.
    /// Solo server.
    /// </summary>
    public sealed class ServerPlayerEntry
    {
        /// <summary>Id di Netcode della connessione.</summary>
        public ulong ClientId { get; }
        /// <summary>GUID del giocatore (formato "d" minuscolo).</summary>
        public string PlayerId { get; }
        /// <summary>Nome visibile, già controllato.</summary>
        public string Name { get; }
        /// <summary>Biglietto di trasferimento ricevuto (Fase 11), o null.</summary>
        public string Ticket { get; }
        public DateTime ApprovedAtUtc { get; }
        /// <summary>Vero quando il client ha caricato l'area e il personaggio è stato creato.</summary>
        public bool Joined { get; internal set; }
        /// <summary>Il personaggio del giocatore (null prima dello spawn).</summary>
        public NetworkObject PlayerObject { get; internal set; }
        /// <summary>Client con lo stesso playerId che questo ingresso sostituisce (sessione più nuova), o null.</summary>
        public ServerPlayerEntry Replaces { get; internal set; }
        /// <summary>Vero quando un client più nuovo con lo stesso playerId ha preso il posto di questo.</summary>
        public bool Replaced { get; internal set; }

        internal readonly float ApprovedAtRealtime;

        internal ServerPlayerEntry(ulong clientId, string playerId, string name, string ticket)
        {
            ClientId = clientId;
            PlayerId = playerId;
            Name = name;
            Ticket = ticket;
            ApprovedAtUtc = DateTime.UtcNow;
            ApprovedAtRealtime = Time.realtimeSinceStartup;
        }

        public override string ToString() => $"{Name} (client {ClientId}, {PlayerId})";
    }

    /// <summary>
    /// Elenco dei giocatori del server: clientId &lt;-&gt; playerId, nome.
    /// Eventi PlayerJoined / PlayerLeft per i moduli del server (salvataggi, economia...).
    /// Solo server: sul client resta vuoto.
    /// </summary>
    public static class ServerPlayerRegistry
    {
        static readonly Dictionary<ulong, ServerPlayerEntry> ByClient = new Dictionary<ulong, ServerPlayerEntry>();
        static readonly Dictionary<string, ServerPlayerEntry> ByPlayerId = new Dictionary<string, ServerPlayerEntry>();
        static readonly List<ServerPlayerEntry> Entries = new List<ServerPlayerEntry>();

        /// <summary>Il giocatore è entrato nell'area e ha il suo personaggio (PlayerObject non null).</summary>
        public static event Action<ServerPlayerEntry> PlayerJoined;

        /// <summary>Il giocatore è uscito (solo se era entrato). Il personaggio sta per essere distrutto.</summary>
        public static event Action<ServerPlayerEntry> PlayerLeft;

        /// <summary>
        /// Client approvato (subito dopo l'approvazione, prima del caricamento dell'area).
        /// Se Replaces non è null, quel client va scollegato (session_replaced).
        /// </summary>
        public static event Action<ServerPlayerEntry> PlayerApproved;

        /// <summary>Client tolto dal registro (sempre, anche se non era mai entrato). Dopo PlayerLeft.</summary>
        public static event Action<ServerPlayerEntry> PlayerRemoved;

        /// <summary>Tutti i giocatori approvati (anche quelli che stanno caricando).</summary>
        public static IReadOnlyList<ServerPlayerEntry> All => Entries;

        /// <summary>Giocatori approvati (contano per il limite del server).</summary>
        public static int Count => Entries.Count;

        /// <summary>Giocatori entrati nell'area.</summary>
        public static int JoinedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++) if (Entries[i].Joined) n++;
                return n;
            }
        }

        public static bool TryGetByClientId(ulong clientId, out ServerPlayerEntry entry) =>
            ByClient.TryGetValue(clientId, out entry);

        public static bool TryGetByPlayerId(string playerId, out ServerPlayerEntry entry)
        {
            entry = null;
            return playerId != null && ByPlayerId.TryGetValue(playerId, out entry);
        }

        internal static ServerPlayerEntry AddApproved(ulong clientId, string playerId, string name, string ticket,
            ServerPlayerEntry replaces = null)
        {
            if (ByClient.ContainsKey(clientId)) Remove(clientId);
            var e = new ServerPlayerEntry(clientId, playerId, name, ticket);
            if (replaces != null && replaces.PlayerId == playerId && replaces.ClientId != clientId)
            {
                e.Replaces = replaces;
                replaces.Replaced = true;
            }
            ByClient[clientId] = e;
            ByPlayerId[playerId] = e;
            Entries.Add(e);
            try { PlayerApproved?.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
            return e;
        }

        internal static void MarkJoined(ServerPlayerEntry e, NetworkObject playerObject)
        {
            if (e == null || e.Joined) return;
            e.PlayerObject = playerObject;
            e.Joined = true;
            try { PlayerJoined?.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>Toglie il giocatore. Lancia PlayerLeft se era entrato. Restituisce l'elemento tolto.</summary>
        internal static ServerPlayerEntry Remove(ulong clientId)
        {
            if (!ByClient.TryGetValue(clientId, out var e)) return null;
            ByClient.Remove(clientId);
            if (ByPlayerId.TryGetValue(e.PlayerId, out var cur) && cur == e) ByPlayerId.Remove(e.PlayerId);
            Entries.Remove(e);
            if (e.Joined)
            {
                try { PlayerLeft?.Invoke(e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            try { PlayerRemoved?.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
            return e;
        }

        internal static void Clear()
        {
            ByClient.Clear();
            ByPlayerId.Clear();
            Entries.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            PlayerJoined = null;
            PlayerLeft = null;
            PlayerApproved = null;
            PlayerRemoved = null;
        }
    }
}
