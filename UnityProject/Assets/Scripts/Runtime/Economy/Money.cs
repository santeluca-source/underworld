using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Underworld.Economy
{
    public enum MoneyError
    {
        None,
        /// <summary>Importo zero o negativo.</summary>
        InvalidAmount,
        /// <summary>Saldo insufficiente per un addebito.</summary>
        InsufficientFunds,
        /// <summary>Il saldo supererebbe MaxCash (o il long).</summary>
        CapExceeded,
        /// <summary>Saldo di partenza non valido (negativo o oltre il limite).</summary>
        InvalidBalance,
    }

    /// <summary>
    /// Regole dei soldi (contratto, sezione 8). Dollari interi in long, mai float.
    /// Aritmetica controllata: nessun overflow arriva al saldo. Logica pura (test EditMode).
    /// </summary>
    public static class Money
    {
        /// <summary>Soldi di un profilo nuovo.</summary>
        public const long StartingCash = 5_000;
        /// <summary>Saldo massimo: 1.000.000.000.000 dollari.</summary>
        public const long MaxCash = 1_000_000_000_000;

        /// <summary>Saldo dopo un accredito, o l'errore. Il saldo non cambia mai in caso di errore.</summary>
        public static MoneyError TryCredit(long balance, long amount, out long newBalance)
        {
            newBalance = balance;
            if (balance < 0 || balance > MaxCash) return MoneyError.InvalidBalance;
            if (amount <= 0) return MoneyError.InvalidAmount;
            long sum;
            try { sum = checked(balance + amount); }
            catch (OverflowException) { return MoneyError.CapExceeded; }
            if (sum > MaxCash) return MoneyError.CapExceeded;
            newBalance = sum;
            return MoneyError.None;
        }

        /// <summary>Saldo dopo un addebito, o l'errore. Mai sotto zero.</summary>
        public static MoneyError TryDebit(long balance, long amount, out long newBalance)
        {
            newBalance = balance;
            if (balance < 0 || balance > MaxCash) return MoneyError.InvalidBalance;
            if (amount <= 0) return MoneyError.InvalidAmount;
            if (amount > balance) return MoneyError.InsufficientFunds;
            newBalance = checked(balance - amount);
            return MoneyError.None;
        }
    }

    /// <summary>Una riga del registro dei movimenti (transactions/&lt;aaaa-MM-gg&gt;.jsonl).</summary>
    public sealed class TransactionRecord
    {
        [JsonProperty("utc")] public string Utc;
        [JsonProperty("area")] public string Area;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("delta")] public long Delta;
        [JsonProperty("balance")] public long Balance;
        [JsonProperty("reason")] public string Reason;
    }

    /// <summary>
    /// Registro dei movimenti di denaro: una riga JSON per movimento, un file per giorno (UTC).
    /// Si scrive solo in coda. Più server con la stessa cartella dati: il file si apre in esclusiva con nuovi
    /// tentativi, così le righe non si mescolano. Solo server.
    /// </summary>
    public sealed class TransactionJournal
    {
        public const string Folder = "transactions";

        public string Dir { get; }
        readonly string _area;

        public TransactionJournal(string dataDir, string area)
        {
            Dir = Path.Combine(dataDir, Folder);
            _area = area;
            Directory.CreateDirectory(Dir);
        }

        /// <summary>Nome del file di un giorno: 2026-11-27.jsonl.</summary>
        public static string FileNameFor(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";

        /// <summary>Una riga del registro (senza a capo). Logica pura.</summary>
        public static string FormatLine(DateTime utc, string area, string playerId, string name, long delta, long balance, string reason)
        {
            var record = new TransactionRecord
            {
                Utc = utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Area = area,
                PlayerId = playerId,
                Name = name,
                Delta = delta,
                Balance = balance,
                Reason = reason,
            };
            return JsonConvert.SerializeObject(record, Formatting.None);
        }

        public static TransactionRecord ParseLine(string line) => JsonConvert.DeserializeObject<TransactionRecord>(line);

        /// <summary>Aggiunge una riga. Falso se il disco non risponde (il movimento resta valido: lo dice il log).</summary>
        public bool Append(DateTime utc, string playerId, string name, long delta, long balance, string reason)
        {
            string line = FormatLine(utc, _area, playerId, name, delta, balance, reason) + "\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(line);
            string path = Path.Combine(Dir, FileNameFor(utc));
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        fs.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (IOException) when (attempt < 8)
                {
                    Thread.Sleep(5 * attempt);
                }
                catch (IOException e)
                {
                    UnityEngine.Debug.LogError($"[Economy] Registro non scritto ({path}): {e.Message}. Riga: {line.Trim()}");
                }
            }
            return false;
        }
    }
}
