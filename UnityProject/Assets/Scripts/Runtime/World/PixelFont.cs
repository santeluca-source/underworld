using System.Collections.Generic;

namespace Underworld.World
{
    /// <summary>
    /// Font 5x7 a pixel per le insegne del mondo (fatte di scatole, quindi unite nelle mesh come il resto).
    /// Lettere maiuscole A-Z, cifre, spazio, frecce (&gt; &lt; ^), trattino, punto, $.
    /// Le lettere minuscole diventano maiuscole; i caratteri sconosciuti diventano spazio.
    /// </summary>
    public static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;
        /// <summary>Passo tra due lettere (5 pixel + 1 di spazio).</summary>
        public const int Advance = 6;
        /// <summary>Passo tra due righe (7 pixel + 2 di spazio).</summary>
        public const int LineHeight = 9;

        /// <summary>Un tratto orizzontale di pixel pieni in una riga della scritta.</summary>
        public readonly struct Run
        {
            public readonly int X, Row, Length;
            public Run(int x, int row, int length) { X = x; Row = row; Length = length; }
        }

        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['>'] = new[] { ".....", "..#..", "...#.", "#####", "...#.", "..#..", "....." },
            ['<'] = new[] { ".....", "..#..", ".#...", "#####", ".#...", "..#..", "....." },
            ['^'] = new[] { "..#..", ".###.", "#.#.#", "..#..", "..#..", "..#..", "..#.." },
            ['-'] = new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
            ['$'] = new[] { "..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.." },
        };

        /// <summary>Larghezza di una riga in pixel.</summary>
        public static int Width(string line) => string.IsNullOrEmpty(line) ? 0 : line.Length * Advance - 1;

        /// <summary>Tratti pieni della riga (unendo i pixel vicini: meno scatole).</summary>
        public static List<Run> Runs(string line)
        {
            var runs = new List<Run>();
            if (string.IsNullOrEmpty(line)) return runs;
            for (int i = 0; i < line.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(line[i]), out var g)) continue;
                int baseX = i * Advance;
                for (int row = 0; row < GlyphHeight; row++)
                {
                    string r = g[row];
                    int start = -1;
                    for (int x = 0; x <= GlyphWidth; x++)
                    {
                        bool on = x < GlyphWidth && r[x] == '#';
                        if (on && start < 0) start = x;
                        else if (!on && start >= 0)
                        {
                            runs.Add(new Run(baseX + start, row, x - start));
                            start = -1;
                        }
                    }
                }
            }
            return runs;
        }
    }
}
