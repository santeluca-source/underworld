using System;
using System.Collections.Generic;

namespace Underworld.Net
{
    /// <summary>
    /// Un client che chiede di entrare, dopo i controlli di base (payload letto, nome normalizzato).
    /// Lo ricevono gli IConnectionValidator.
    /// </summary>
    public readonly struct ConnectionCandidate
    {
        public readonly ulong ClientId;
        public readonly ConnectionPayload Payload;
        /// <summary>GUID normalizzato ("d" minuscolo).</summary>
        public readonly string PlayerId;
        /// <summary>Nome normalizzato e valido.</summary>
        public readonly string Name;
        /// <summary>Vero se questo playerId è già su questo server (riconnessione o seconda finestra).</summary>
        public readonly bool PlayerIdInUse;

        public ConnectionCandidate(ulong clientId, ConnectionPayload payload, string playerId, string name, bool playerIdInUse = false)
        {
            ClientId = clientId;
            Payload = payload;
            PlayerId = playerId;
            Name = name;
            PlayerIdInUse = playerIdInUse;
        }
    }

    /// <summary>
    /// Controllo aggiuntivo sull'ingresso di un giocatore (punto di estensione).
    /// La Fase 5 aggiunge qui: hash del token, lucchetto di sessione, ban.
    /// Registrazione: ServerApproval.AddValidator(...) all'avvio del server (per esempio in un IServerModule).
    /// </summary>
    public interface IConnectionValidator
    {
        /// <summary>
        /// Null se il giocatore può entrare. Altrimenti il motivo del rifiuto nel formato
        /// DisconnectReasons.Format(codice, testo in inglese).
        /// </summary>
        string Validate(in ConnectionCandidate candidate);
    }

    /// <summary>Dati del server che servono alla decisione.</summary>
    public sealed class ApprovalContext
    {
        public int MaxPlayers = NetConstants.DefaultMaxPlayers;
        /// <summary>Giocatori già approvati (in gioco o in caricamento).</summary>
        public int CurrentPlayers;
        /// <summary>True se questo playerId è già sul server.</summary>
        public Func<string, bool> IsPlayerIdInUse;
        /// <summary>Controlli aggiuntivi (può essere null).</summary>
        public IReadOnlyList<IConnectionValidator> Validators;
        /// <summary>
        /// Stesso playerId già sul server: true = il nuovo client sostituisce il vecchio (la sessione più nuova vince).
        /// Null = rifiuto already_connected. Si chiama solo dopo i validatori (token già controllato).
        /// </summary>
        public Func<ConnectionCandidate, bool> AllowReplace;
    }

    /// <summary>Esito della decisione.</summary>
    public readonly struct ApprovalResult
    {
        public readonly bool Approved;
        /// <summary>Motivo del rifiuto ("codice|testo"); null se approvato.</summary>
        public readonly string Reason;
        public readonly string PlayerId;
        public readonly string Name;
        public readonly string Ticket;
        /// <summary>Approvato al posto di un client con lo stesso playerId (che va scollegato con session_replaced).</summary>
        public readonly bool ReplacesExisting;

        ApprovalResult(bool approved, string reason, string playerId, string name, string ticket, bool replaces)
        {
            Approved = approved;
            Reason = reason;
            PlayerId = playerId;
            Name = name;
            Ticket = ticket;
            ReplacesExisting = replaces;
        }

        public static ApprovalResult Accept(string playerId, string name, string ticket, bool replacesExisting = false) =>
            new ApprovalResult(true, null, playerId, name, ticket, replacesExisting);

        public static ApprovalResult Reject(string reason) => new ApprovalResult(false, reason, null, null, null, false);
    }

    /// <summary>
    /// Decide se un client può entrare. Logica pura, senza Netcode: si prova in EditMode.
    /// Ordine dei controlli: dimensione, JSON, versione, identità, nome, server pieno (solo per un playerId nuovo),
    /// validatori (Fase 5: token e lucchetto), doppio ingresso (sostituzione se AllowReplace dice sì, altrimenti rifiuto).
    /// Il doppio ingresso viene dopo i validatori: così la sostituzione avviene solo con il token giusto.
    /// </summary>
    public static class ConnectionApprovalRules
    {
        public static ApprovalResult Evaluate(byte[] payloadBytes, ulong clientId, ApprovalContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            int size = payloadBytes?.Length ?? 0;
            if (size > NetConstants.MaxPayloadBytes)
                return ApprovalResult.Reject(DisconnectReasons.PayloadTooLarge(size, NetConstants.MaxPayloadBytes));

            var payload = ConnectionPayload.TryParse(payloadBytes);
            if (payload == null) return ApprovalResult.Reject(DisconnectReasons.PayloadInvalid);

            if (payload.Version != NetConstants.ProtocolVersion)
                return ApprovalResult.Reject(DisconnectReasons.ProtocolVersion(payload.Version, NetConstants.ProtocolVersion));

            if (!ConnectionPayload.TryNormalizePlayerId(payload.PlayerId, out string playerId) ||
                !ConnectionPayload.IsValidToken(payload.Token))
                return ApprovalResult.Reject(DisconnectReasons.IdentityInvalid);

            if (payload.Ticket != null && payload.Ticket.Length > NetConstants.MaxTicketLength)
                return ApprovalResult.Reject(DisconnectReasons.PayloadInvalid);

            if (!PlayerNameRules.TryNormalize(payload.Name, out string name))
                return ApprovalResult.Reject(DisconnectReasons.NameInvalid);

            bool inUse = ctx.IsPlayerIdInUse != null && ctx.IsPlayerIdInUse(playerId);

            // Chi sostituisce la sua sessione non occupa un posto in più.
            if (!inUse && ctx.CurrentPlayers >= ctx.MaxPlayers)
                return ApprovalResult.Reject(DisconnectReasons.ServerFull(ctx.CurrentPlayers, ctx.MaxPlayers));

            var candidate = new ConnectionCandidate(clientId, payload, playerId, name, inUse);
            if (ctx.Validators != null)
            {
                for (int i = 0; i < ctx.Validators.Count; i++)
                {
                    var v = ctx.Validators[i];
                    if (v == null) continue;
                    string reason = v.Validate(candidate);
                    if (!string.IsNullOrEmpty(reason)) return ApprovalResult.Reject(reason);
                }
            }

            if (inUse)
            {
                if (ctx.AllowReplace == null || !ctx.AllowReplace(candidate))
                    return ApprovalResult.Reject(DisconnectReasons.AlreadyConnected);
                return ApprovalResult.Accept(playerId, name, payload.Ticket, replacesExisting: true);
            }

            return ApprovalResult.Accept(playerId, name, payload.Ticket);
        }
    }
}
