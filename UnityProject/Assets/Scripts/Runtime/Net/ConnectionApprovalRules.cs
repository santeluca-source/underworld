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

        public ConnectionCandidate(ulong clientId, ConnectionPayload payload, string playerId, string name)
        {
            ClientId = clientId;
            Payload = payload;
            PlayerId = playerId;
            Name = name;
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
        /// DisconnectReasons.Format(codice, testo in italiano).
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

        ApprovalResult(bool approved, string reason, string playerId, string name, string ticket)
        {
            Approved = approved;
            Reason = reason;
            PlayerId = playerId;
            Name = name;
            Ticket = ticket;
        }

        public static ApprovalResult Accept(string playerId, string name, string ticket) =>
            new ApprovalResult(true, null, playerId, name, ticket);

        public static ApprovalResult Reject(string reason) => new ApprovalResult(false, reason, null, null, null);
    }

    /// <summary>
    /// Decide se un client può entrare. Logica pura, senza Netcode: si prova in EditMode.
    /// Ordine dei controlli: dimensione, JSON, versione, identità, nome, server pieno, doppio ingresso, validatori.
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

            if (ctx.CurrentPlayers >= ctx.MaxPlayers)
                return ApprovalResult.Reject(DisconnectReasons.ServerFull(ctx.CurrentPlayers, ctx.MaxPlayers));

            if (ctx.IsPlayerIdInUse != null && ctx.IsPlayerIdInUse(playerId))
                return ApprovalResult.Reject(DisconnectReasons.AlreadyConnected);

            if (ctx.Validators != null)
            {
                var candidate = new ConnectionCandidate(clientId, payload, playerId, name);
                for (int i = 0; i < ctx.Validators.Count; i++)
                {
                    var v = ctx.Validators[i];
                    if (v == null) continue;
                    string reason = v.Validate(candidate);
                    if (!string.IsNullOrEmpty(reason)) return ApprovalResult.Reject(reason);
                }
            }

            return ApprovalResult.Accept(playerId, name, payload.Ticket);
        }
    }
}
