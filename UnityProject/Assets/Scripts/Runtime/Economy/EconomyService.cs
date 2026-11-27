using System;
using Underworld.Server;
using UnityEngine;

namespace Underworld.Economy
{
    /// <summary>
    /// L'unico punto che cambia i soldi di un giocatore (contratto, sezione 8). Solo server.
    /// - Importi &gt; 0, aritmetica controllata, saldo tra 0 e Money.MaxCash.
    /// - Ogni movimento: una riga in transactions/&lt;aaaa-MM-gg&gt;.jsonl, sessione da salvare (2 s), saldo al client.
    /// - reason: testo breve e stabile per il registro, es. "shop.burger", "mission.reward", "vehicle.buy.sedan".
    /// </summary>
    public sealed class EconomyService
    {
        /// <summary>Lunghezza massima del motivo nel registro.</summary>
        public const int MaxReasonLength = 64;

        readonly TransactionJournal _journal;

        /// <summary>Saldo cambiato: sessione, variazione, motivo.</summary>
        public event Action<PlayerSession, long, string> CashChanged;

        public EconomyService(TransactionJournal journal) => _journal = journal;

        public long GetCash(PlayerSession session) => session?.Save?.Cash ?? 0;

        public bool CanAfford(PlayerSession session, long amount) => amount > 0 && GetCash(session) >= amount;

        /// <summary>Accredita amount (&gt; 0). Falso se l'importo non è valido o il saldo supererebbe il limite.</summary>
        public bool Credit(PlayerSession session, long amount, string reason) => Credit(session, amount, reason, out _);

        public bool Credit(PlayerSession session, long amount, string reason, out MoneyError error)
        {
            if (!CheckSession(session, out error)) return false;
            error = Money.TryCredit(session.Save.Cash, amount, out long balance);
            if (error != MoneyError.None) return Refuse(session, "accredito", amount, reason, error);
            Apply(session, balance, amount, reason);
            return true;
        }

        /// <summary>Addebita amount (&gt; 0) se il saldo basta. Falso (e saldo invariato) altrimenti.</summary>
        public bool TryDebit(PlayerSession session, long amount, string reason) => TryDebit(session, amount, reason, out _);

        public bool TryDebit(PlayerSession session, long amount, string reason, out MoneyError error)
        {
            if (!CheckSession(session, out error)) return false;
            error = Money.TryDebit(session.Save.Cash, amount, out long balance);
            if (error != MoneyError.None) return Refuse(session, "addebito", amount, reason, error);
            Apply(session, balance, -amount, reason);
            return true;
        }

        /// <summary>Solo per ServerTestHooks (-testmode): porta il saldo a un valore, con la riga nel registro.</summary>
        internal bool SetCashForTests(PlayerSession session, long value, string reason)
        {
            if (!CheckSession(session, out _) || value < 0 || value > Money.MaxCash) return false;
            long delta = value - session.Save.Cash;
            if (delta == 0) return true;
            Apply(session, value, delta, reason);
            return true;
        }

        /// <summary>Manda il saldo al proprietario (PlayerWallet). Lo chiama SessionService quando il personaggio entra.</summary>
        internal void SyncToClient(PlayerSession session)
        {
            var wallet = session?.GetPlayerComponent<PlayerWallet>();
            if (wallet != null) wallet.ServerSetCash(session.Save.Cash);
        }

        // ---------------- Interni ----------------

        static bool CheckSession(PlayerSession session, out MoneyError error)
        {
            error = MoneyError.None;
            if (session == null || session.Closed || session.Save == null)
            {
                Debug.LogWarning("[Economy] Movimento rifiutato: sessione chiusa o mancante.");
                error = MoneyError.InvalidBalance;
                return false;
            }
            return true;
        }

        static bool Refuse(PlayerSession session, string kind, long amount, string reason, MoneyError error)
        {
            if (error != MoneyError.InsufficientFunds)
                Debug.LogWarning($"[Economy] {kind} di {amount} rifiutato per {session.Name}: {error} ({CleanReason(reason)}).");
            return false;
        }

        void Apply(PlayerSession session, long balance, long delta, string reason)
        {
            reason = CleanReason(reason);
            session.Save.Cash = balance;
            session.MarkDirty(important: true);
            _journal?.Append(DateTime.UtcNow, session.PlayerId, session.Name, delta, balance, reason);
            Debug.Log($"[Economy] {session.Name}: {(delta >= 0 ? "+" : "")}{delta} → {balance} ({reason}).");
            SyncToClient(session);
            try { CashChanged?.Invoke(session, delta, reason); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Motivo pulito per il registro: niente a capo, al massimo 64 caratteri, mai vuoto.</summary>
        public static string CleanReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "unspecified";
            var chars = reason.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (char.IsControl(chars[i])) chars[i] = ' ';
            string s = new string(chars);
            return s.Length > MaxReasonLength ? s.Substring(0, MaxReasonLength) : s;
        }
    }
}
