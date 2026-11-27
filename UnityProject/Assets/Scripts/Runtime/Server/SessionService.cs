using System;
using System.Collections.Generic;
using System.Diagnostics;
using Underworld.Economy;
using Underworld.Net;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Underworld.Server
{
    /// <summary>
    /// Identità, sessioni e salvataggi dei giocatori di questo server (contratto, sezioni 6 e 7). Solo server.
    ///
    /// All'ingresso (IConnectionValidator, nell'approvazione):
    /// 1. stesso giocatore già su questo server → controlla il token sull'hash in memoria; poi la sessione più
    ///    nuova vince (il vecchio client riceve session_replaced, i dati passano alla sessione nuova);
    /// 2. altrimenti prende il lucchetto locks/&lt;playerId&gt;.lock (vivo su un altro server → already_connected);
    /// 3. carica il salvataggio (o lo crea con $5,000) e controlla il token a tempo costante.
    ///
    /// Salvataggi: 2 s dopo una modifica importante (MarkDirty), ogni 60 s, all'uscita, a ServerStopping
    /// (sincrono) e con SaveNow (es. prima di un trasferimento, Fase 11). Ogni salvataggio scrive una riga [Save].
    /// Lucchetti: battito ogni 5 s per tutte le sessioni; liberati dopo il salvataggio finale.
    /// </summary>
    public sealed class SessionService : IConnectionValidator
    {
        public const float DebounceSeconds = 2f;
        public const float PeriodicSaveSeconds = 60f;
        const string Log = "[Save]";

        readonly ServerContext _ctx;
        readonly IPlayerStore _store;
        readonly SessionLockStore _locks;
        readonly Dictionary<ulong, PlayerSession> _byClient = new Dictionary<ulong, PlayerSession>();
        readonly Dictionary<string, PlayerSession> _byPlayer = new Dictionary<string, PlayerSession>(StringComparer.Ordinal);
        readonly Dictionary<ulong, Prepared> _prepared = new Dictionary<ulong, Prepared>();
        readonly List<(ulong clientId, string reason)> _pendingKicks = new List<(ulong, string)>();
        readonly List<PlayerSession> _scratch = new List<PlayerSession>();
        float _nextPeriodic;
        float _nextHeartbeat;
        bool _stopped;

        /// <summary>Accesso preparato dal validatore e consumato dall'approvazione (stesso frame).</summary>
        sealed class Prepared
        {
            public string PlayerId;
            public PlayerSave Save;
            public bool IsNew;
            /// <summary>Lo stesso giocatore è già su questo server: la sessione esistente passa al nuovo client.</summary>
            public bool Replace;
            public float At;
        }

        /// <summary>Sessione creata (giocatore approvato, personaggio non ancora creato).</summary>
        public event Action<PlayerSession> SessionStarted;
        /// <summary>Il personaggio è in gioco: i moduli mandano il loro stato al client.</summary>
        public event Action<PlayerSession> SessionJoined;
        /// <summary>La sessione sta per chiudersi: ultimo momento per cambiare i dati prima del salvataggio finale.</summary>
        public event Action<PlayerSession> SessionEnding;

        /// <summary>Salvataggi riusciti e falliti in questo processo (test e log).</summary>
        public int SavesOk { get; private set; }
        public int SavesFailed { get; private set; }

        public IReadOnlyCollection<PlayerSession> All => _byClient.Values;
        public int Count => _byClient.Count;

        internal SessionService(ServerContext ctx, IPlayerStore store, SessionLockStore locks)
        {
            _ctx = ctx;
            _store = store;
            _locks = locks;
            float now = Time.realtimeSinceStartup;
            _nextPeriodic = now + PeriodicSaveSeconds;
            _nextHeartbeat = now + SessionLockRules.HeartbeatSeconds;
        }

        public bool TryGet(ulong clientId, out PlayerSession session) => _byClient.TryGetValue(clientId, out session);

        public bool TryGetByPlayerId(string playerId, out PlayerSession session)
        {
            session = null;
            return playerId != null && _byPlayer.TryGetValue(playerId, out session);
        }

        // ---------------- Approvazione ----------------

        public string Validate(in ConnectionCandidate c)
        {
            if (_stopped) return DisconnectReasons.ServerClosing;
            string playerId = c.PlayerId;
            string token = c.Payload?.Token;
            if (!PlayerIdPath.IsValid(playerId) || string.IsNullOrEmpty(token)) return DisconnectReasons.IdentityInvalid;
            _prepared.Remove(c.ClientId);

            // 1. Lo stesso giocatore è già qui (seconda finestra, riconnessione dopo un crash).
            if (_byPlayer.TryGetValue(playerId, out var existing) && !existing.Closed)
            {
                if (!IdentityHash.Verify(playerId, token, existing.Save.TokenHash))
                {
                    Debug.LogWarning($"{Log} Token sbagliato per {playerId} (già in gioco come {existing.Name}): rifiuto.");
                    return DisconnectReasons.IdentityMismatch;
                }
                _prepared[c.ClientId] = new Prepared { PlayerId = playerId, Replace = true, At = Time.realtimeSinceStartup };
                return null;
            }

            // 2. Lucchetto di sessione (un solo server alla volta).
            if (!_locks.TryAcquire(playerId, out var holder))
            {
                Debug.Log($"{Log} {playerId} è in gioco su un altro server " +
                          $"(area {holder?.Area ?? "?"}, pid {holder?.Pid}, porta {holder?.Port}, battito {holder?.HeartbeatUtc ?? "?"}): rifiuto.");
                return DisconnectReasons.AlreadyOnlineElsewhere;
            }

            // 3. Salvataggio.
            var sw = Stopwatch.StartNew();
            PlayerLoadResult load;
            try { load = _store.Load(playerId); }
            catch (Exception e)
            {
                Debug.LogException(e);
                load = new PlayerLoadResult(PlayerLoadStatus.Error, null, e.Message);
            }

            PlayerSave save;
            bool isNew = false;
            switch (load.Status)
            {
                case PlayerLoadStatus.NotFound:
                    save = PlayerSave.CreateNew(playerId, c.Name, IdentityHash.Compute(playerId, token), DateTime.UtcNow);
                    isNew = true;
                    break;
                case PlayerLoadStatus.Ok:
                case PlayerLoadStatus.RecoveredFromBackup:
                    save = load.Save;
                    if (load.Status == PlayerLoadStatus.RecoveredFromBackup)
                        Debug.LogWarning($"{Log} Salvataggio di {playerId} rovinato ({load.Detail}): recuperato dalla copia .bak.");
                    if (!IdentityHash.Verify(playerId, token, save.TokenHash))
                    {
                        ReleaseIfUnused(playerId);
                        Debug.LogWarning($"{Log} Token sbagliato per il profilo {playerId} ({save.Name}): rifiuto.");
                        return DisconnectReasons.IdentityMismatch;
                    }
                    break;
                case PlayerLoadStatus.Corrupt:
                    ReleaseIfUnused(playerId);
                    Debug.LogError($"{Log} Salvataggio di {playerId} illeggibile anche dalla copia .bak ({load.Detail}). " +
                                   "File in players/corrupt/: ingresso rifiutato finché non viene riparato.");
                    return DisconnectReasons.SaveCorrupt;
                default:
                    ReleaseIfUnused(playerId);
                    Debug.LogError($"{Log} Salvataggio di {playerId} non disponibile ({load.Detail}): ingresso rifiutato.");
                    return DisconnectReasons.SaveUnavailable;
            }

            _prepared[c.ClientId] = new Prepared { PlayerId = playerId, Save = save, IsNew = isNew, At = Time.realtimeSinceStartup };
            Debug.Log($"{Log} {(isNew ? "Profilo nuovo" : "Profilo caricato")} per {playerId} in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
            return null;
        }

        /// <summary>ApprovalContext.AllowReplace: sostituzione solo se il token è stato controllato per questo client.</summary>
        internal bool AllowReplace(ConnectionCandidate c) =>
            _prepared.TryGetValue(c.ClientId, out var p) && p.Replace && p.PlayerId == c.PlayerId;

        internal void OnApproved(ServerPlayerEntry entry)
        {
            if (!_prepared.TryGetValue(entry.ClientId, out var p) || p.PlayerId != entry.PlayerId)
            {
                // Approvato senza passare dal validatore: non deve succedere. Meglio fuori che senza salvataggio.
                Debug.LogError($"{Log} {entry} approvato senza sessione preparata: lo scollego.");
                _pendingKicks.Add((entry.ClientId, DisconnectReasons.InternalError));
                return;
            }
            _prepared.Remove(entry.ClientId);

            PlayerSave save = p.Save;
            if (p.Replace)
            {
                if (!_byPlayer.TryGetValue(entry.PlayerId, out var old) || old.Closed)
                {
                    Debug.LogError($"{Log} Sostituzione di {entry.PlayerId} senza sessione vecchia: lo scollego.");
                    _pendingKicks.Add((entry.ClientId, DisconnectReasons.InternalError));
                    return;
                }
                // La sessione più nuova vince: salva quella vecchia, poi i suoi dati passano al nuovo client.
                old.CapturePosition();
                SaveNow(old, "sessione sostituita");
                old.Closed = true;
                _byClient.Remove(old.ClientId);
                _pendingKicks.Add((old.ClientId, DisconnectReasons.SessionReplaced));
                save = old.Save;
                Debug.Log($"{Log} {entry.PlayerId}: il client {entry.ClientId} sostituisce il client {old.ClientId} (session_replaced).");
            }

            var session = new PlayerSession(entry, save, p.IsNew);
            save.Name = entry.Name;
            save.LastSeenUtc = DateTime.UtcNow;
            save.LastArea = _ctx.Area.ToString();
            _byClient[entry.ClientId] = session;
            _byPlayer[entry.PlayerId] = session;

            if (p.IsNew)
            {
                // I soldi iniziali passano da EconomyService: così c'è la riga nel registro.
                _ctx.Economy.Credit(session, Money.StartingCash, "account.start");
                SaveNow(session, "profilo nuovo");
            }
            else
            {
                session.MarkDirty(important: false);
            }

            try { SessionStarted?.Invoke(session); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal void OnJoined(ServerPlayerEntry entry)
        {
            if (!_byClient.TryGetValue(entry.ClientId, out var session) || session.Closed) return;
            try { SessionJoined?.Invoke(session); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal void OnRemoved(ServerPlayerEntry entry)
        {
            _prepared.Remove(entry.ClientId);
            if (!_byClient.TryGetValue(entry.ClientId, out var session)) return; // sostituita o già chiusa
            Close(session, "uscita del giocatore");
        }

        /// <summary>Salvataggio finale, lucchetto liberato, sessione tolta.</summary>
        void Close(PlayerSession session, string why)
        {
            if (session.Closed) return;
            try { SessionEnding?.Invoke(session); }
            catch (Exception e) { Debug.LogException(e); }
            session.CapturePosition();
            SaveNow(session, why);
            session.Closed = true;
            _byClient.Remove(session.ClientId);
            if (_byPlayer.TryGetValue(session.PlayerId, out var cur) && cur == session)
            {
                _byPlayer.Remove(session.PlayerId);
                _locks.Release(session.PlayerId);
            }
        }

        void ReleaseIfUnused(string playerId)
        {
            if (!_byPlayer.ContainsKey(playerId)) _locks.Release(playerId);
        }

        // ---------------- Salvataggi ----------------

        /// <summary>
        /// Salva subito (sincrono). Per i trasferimenti (Fase 11) e per i moduli che non possono aspettare.
        /// Falso se la scrittura non riesce (la sessione resta "da salvare" e si riprova).
        /// </summary>
        public bool SaveNow(PlayerSession session, string reason = "richiesta")
        {
            if (session == null || session.Closed) return false;
            var sw = Stopwatch.StartNew();
            try
            {
                session.Save.LastSeenUtc = DateTime.UtcNow;
                _store.Save(session.Save);
                session.ClearDirty();
                session.LastSaveUtc = DateTime.UtcNow;
                session.SaveCount++;
                SavesOk++;
                Debug.Log($"{Log} Salvato {session.Name} ({session.PlayerId}) in {sw.Elapsed.TotalMilliseconds:0.0} ms ({reason}).");
                return true;
            }
            catch (Exception e)
            {
                SavesFailed++;
                session.MarkDirty();
                session.SaveDueAt = Time.realtimeSinceStartup + 5f;
                Debug.LogError($"{Log} Salvataggio di {session.Name} ({session.PlayerId}) NON riuscito ({reason}): {e.Message}");
                return false;
            }
        }

        /// <summary>Chiusura del server: salva tutti (sincrono) e libera i lucchetti.</summary>
        internal void SaveAllAndRelease(string why)
        {
            if (_stopped) return;
            _stopped = true;
            var sw = Stopwatch.StartNew();
            _scratch.Clear();
            _scratch.AddRange(_byClient.Values);
            foreach (var s in _scratch) Close(s, why);
            foreach (var p in _prepared.Values) if (!p.Replace) ReleaseIfUnused(p.PlayerId);
            _prepared.Clear();
            Debug.Log($"{Log} Chiusura: {_scratch.Count} sessioni salvate in {sw.Elapsed.TotalMilliseconds:0.0} ms.");
            _scratch.Clear();
        }

        // ---------------- Ogni frame (ServerPump) ----------------

        internal void Tick()
        {
            if (_stopped) return;
            float now = Time.realtimeSinceStartup;

            if (_pendingKicks.Count > 0) FlushKicks();

            // Accessi preparati e mai approvati (un altro validatore ha rifiutato): libera il lucchetto.
            if (_prepared.Count > 0)
            {
                List<ulong> expired = null;
                foreach (var kv in _prepared)
                    if (now - kv.Value.At > 2f) (expired ??= new List<ulong>()).Add(kv.Key);
                if (expired != null)
                    foreach (var id in expired)
                    {
                        var p = _prepared[id];
                        _prepared.Remove(id);
                        if (!p.Replace) ReleaseIfUnused(p.PlayerId);
                    }
            }

            bool periodic = now >= _nextPeriodic;
            if (periodic) _nextPeriodic = now + PeriodicSaveSeconds;
            bool heartbeat = now >= _nextHeartbeat;
            if (heartbeat) _nextHeartbeat = now + SessionLockRules.HeartbeatSeconds;

            if (_byClient.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_byClient.Values);
            foreach (var s in _scratch)
            {
                if (s.Closed) continue;
                if (heartbeat)
                {
                    s.CapturePosition();
                    if (!_locks.Heartbeat(s.PlayerId))
                    {
                        // Un altro server ha preso il lucchetto (questo è rimasto fermo più di 15 s):
                        // i dati veri ora sono suoi, quindi niente salvataggio da qui.
                        Debug.LogError($"{Log} Lucchetto di {s.PlayerId} perso: scollego {s.Name} senza salvare.");
                        s.Closed = true;
                        _byClient.Remove(s.ClientId);
                        if (_byPlayer.TryGetValue(s.PlayerId, out var cur) && cur == s) _byPlayer.Remove(s.PlayerId);
                        _pendingKicks.Add((s.ClientId, DisconnectReasons.SessionLost));
                        continue;
                    }
                }
                if (periodic) SaveNow(s, "periodico");
                else if (s.Dirty && now >= s.SaveDueAt) SaveNow(s, "modifica");
            }
            _scratch.Clear();
            if (_pendingKicks.Count > 0) FlushKicks();
        }

        void FlushKicks()
        {
            var nm = _ctx.Network;
            foreach (var (clientId, reason) in _pendingKicks)
            {
                if (nm == null || !nm.IsServer) break;
                if (!nm.ConnectedClients.ContainsKey(clientId)) continue;
                DisconnectReasons.TryParse(reason, out string code, out _);
                Debug.Log($"{NetConstants.ServerLog} Scollego il client {clientId}: {code}.");
                nm.DisconnectClient(clientId, reason);
            }
            _pendingKicks.Clear();
        }
    }
}
