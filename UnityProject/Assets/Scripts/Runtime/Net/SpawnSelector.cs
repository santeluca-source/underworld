using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Sceglie il punto di spawn più libero: quello con la distanza più grande dal giocatore più vicino.
    /// Così i giocatori nascono sparsi. A parità di spazio vince il punto meno usato, poi quello con indice
    /// più basso (risultato stabile). Logica pura (testata in EditMode).
    /// </summary>
    public static class SpawnSelector
    {
        /// <summary>
        /// Indice del punto scelto, o -1 se non ci sono punti.
        /// </summary>
        /// <param name="spawnPoints">Posizioni dei punti di spawn.</param>
        /// <param name="occupied">Posizioni dei giocatori già presenti.</param>
        /// <param name="useCount">Quante volte è stato usato ogni punto (può essere null).</param>
        public static int Choose(IReadOnlyList<Vector3> spawnPoints, IReadOnlyList<Vector3> occupied, IReadOnlyList<int> useCount = null)
        {
            if (spawnPoints == null || spawnPoints.Count == 0) return -1;
            int best = -1;
            float bestDist = float.NegativeInfinity;
            int bestUses = int.MaxValue;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                float d = NearestDistance(spawnPoints[i], occupied);
                // Oltre 4 m un punto è "libero": conta solo quante volte è stato usato.
                if (d > FreeRadius) d = FreeRadius;
                int uses = useCount != null && i < useCount.Count ? useCount[i] : 0;
                bool better = d > bestDist + 0.01f || (Mathf.Abs(d - bestDist) <= 0.01f && uses < bestUses);
                if (better)
                {
                    best = i;
                    bestDist = d;
                    bestUses = uses;
                }
            }
            return best;
        }

        /// <summary>Distanza oltre la quale un punto di spawn conta come libero.</summary>
        public const float FreeRadius = 4f;

        static float NearestDistance(Vector3 p, IReadOnlyList<Vector3> others)
        {
            if (others == null || others.Count == 0) return float.PositiveInfinity;
            float min = float.PositiveInfinity;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 d = others[i] - p;
                d.y = 0f;
                float m = d.magnitude;
                if (m < min) min = m;
            }
            return min;
        }

        /// <summary>
        /// Piccolo spostamento a spirale per non mettere due giocatori nello stesso punto
        /// quando tutti i punti sono occupati. n = 0 non sposta.
        /// </summary>
        public static Vector3 CrowdOffset(int n)
        {
            if (n <= 0) return Vector3.zero;
            float angle = n * 2.39996f; // angolo aureo in radianti
            float radius = 1.2f * Mathf.Sqrt(n);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
