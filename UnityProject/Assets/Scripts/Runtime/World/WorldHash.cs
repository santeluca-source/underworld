using UnityEngine;

namespace Underworld.World
{
    /// <summary>
    /// Hash FNV-1a a 64 bit per confrontare due mondi (test di determinismo, log).
    /// I numeri decimali si arrotondano al millimetro, così piccoli errori di calcolo non contano.
    /// </summary>
    public struct WorldHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong _h;
        bool _started;

        public ulong Value => _started ? _h : Offset;

        public void Add(int v)
        {
            if (!_started) { _h = Offset; _started = true; }
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    _h ^= (byte)(v >> (i * 8));
                    _h *= Prime;
                }
            }
        }

        public void Add(bool b) => Add(b ? 1 : 0);
        public void Add(float f) => Add(Mathf.RoundToInt(f * 1000f));
        public void Add(Vector2 v) { Add(v.x); Add(v.y); }
        public void Add(Vector3 v) { Add(v.x); Add(v.y); Add(v.z); }
        public void Add(Quaternion q) { Add(q.x); Add(q.y); Add(q.z); Add(q.w); }

        public void Add(string s)
        {
            if (s == null) { Add(-1); return; }
            Add(s.Length);
            foreach (char c in s) Add(c);
        }

        public override string ToString() => Value.ToString("X16");
    }
}
