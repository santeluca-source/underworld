using System;
using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Limite di frequenza "a secchio di gettoni", uno per ogni client e chiave.
    /// Esempio: new RateLimiter(5, 10) = 5 richieste al secondo, con picchi fino a 10.
    /// Uso tipico in una RPC del server:
    ///   if (!Limiter.TryConsume(rpcParams.Receive.SenderClientId, "interact")) return;
    /// Chiama RemoveClient quando il giocatore esce (ServerPlayerRegistry.PlayerLeft).
    /// Logica pura; il tempo si può sostituire nei test.
    /// </summary>
    public sealed class RateLimiter
    {
        struct Bucket
        {
            public float Tokens;
            public double LastTime;
        }

        readonly Dictionary<(ulong client, string key), Bucket> _buckets = new Dictionary<(ulong, string), Bucket>();
        readonly Func<double> _clock;

        /// <summary>Gettoni che tornano ogni secondo.</summary>
        public float TokensPerSecond { get; }
        /// <summary>Gettoni massimi (picco permesso).</summary>
        public float Burst { get; }

        /// <param name="tokensPerSecond">Richieste al secondo in media.</param>
        /// <param name="burst">Richieste massime di fila.</param>
        /// <param name="clock">Orologio in secondi (null = Time.realtimeSinceStartupAsDouble).</param>
        public RateLimiter(float tokensPerSecond, float burst, Func<double> clock = null)
        {
            if (tokensPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
            if (burst < 1f) throw new ArgumentOutOfRangeException(nameof(burst));
            TokensPerSecond = tokensPerSecond;
            Burst = burst;
            _clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Prova a usare "cost" gettoni. Falso = richiesta da scartare.</summary>
        public bool TryConsume(ulong clientId, string key, float cost = 1f)
        {
            key ??= string.Empty;
            double now = _clock();
            var id = (clientId, key);
            if (!_buckets.TryGetValue(id, out var b))
            {
                b = new Bucket { Tokens = Burst, LastTime = now };
            }
            else
            {
                double dt = now - b.LastTime;
                if (dt > 0) b.Tokens = Mathf.Min(Burst, b.Tokens + (float)(dt * TokensPerSecond));
                b.LastTime = now;
            }

            bool ok = b.Tokens >= cost;
            if (ok) b.Tokens -= cost;
            _buckets[id] = b;
            return ok;
        }

        /// <summary>Gettoni disponibili adesso (per i test e il debug).</summary>
        public float Available(ulong clientId, string key)
        {
            key ??= string.Empty;
            if (!_buckets.TryGetValue((clientId, key), out var b)) return Burst;
            double dt = _clock() - b.LastTime;
            return Mathf.Min(Burst, b.Tokens + (float)(Math.Max(0, dt) * TokensPerSecond));
        }

        /// <summary>Dimentica tutti i secchi di un client (quando esce).</summary>
        public void RemoveClient(ulong clientId)
        {
            List<(ulong, string)> toRemove = null;
            foreach (var k in _buckets.Keys)
                if (k.client == clientId) (toRemove ??= new List<(ulong, string)>()).Add(k);
            if (toRemove == null) return;
            foreach (var k in toRemove) _buckets.Remove(k);
        }

        public void Clear() => _buckets.Clear();

        /// <summary>Numero di secchi in memoria (per i test).</summary>
        public int BucketCount => _buckets.Count;
    }
}
