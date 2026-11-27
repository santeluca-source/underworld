using System.Collections.Generic;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>Tipo di superficie di un colore della tavolozza (un materiale per tipo).</summary>
    public enum PalSurface : byte
    {
        /// <summary>Opaco (muri, asfalto, marciapiedi).</summary>
        Matte,
        /// <summary>Lucido (vetri, acqua, metallo lucido).</summary>
        Gloss,
        /// <summary>Luminoso (lampioni, insegne, finestre accese).</summary>
        Glow,
        /// <summary>Trasparente (vetrine dei negozi e dello showroom: si vede dentro).</summary>
        Clear,
    }

    /// <summary>
    /// Colori della tavolozza per le forme fatte a codice.
    /// Ogni colore è un pixel di una piccola texture 16x16: così tutte le forme di un pezzo di mondo
    /// usano lo stesso materiale e si disegnano con poche draw call.
    /// </summary>
    public enum Pal : byte
    {
        Asphalt, AsphaltPatch, Sidewalk, Curb, Concrete, ConcreteDark, Grass, GrassDark, Soil, Gravel,
        Marking, MarkingYellow,
        Glass, GlassDark, GlassBlue, GlassTeal, GlassShop,
        Roof, RoofDark, RoofLight,
        Facade0, Facade1, Facade2, Facade3, Facade4, Facade5, Facade6, Facade7, Facade8, Facade9, Facade10, Facade11,
        Brick, BrickDark, Stone, StoneDark, Marble,
        Metal, MetalDark, MetalLight, Chrome,
        Wood, WoodDark,
        Water,
        White, Black, Red, Blue, Yellow, Green, Orange, Teal,
        PoliceBlue, PoliceWhite, BankGold, VaultSteel,
        AwningRed, AwningGreen, AwningBlue, AwningYellow, AwningStripe,
        FloorTile, FloorDark, Carpet, Rubber, DarkVoid, Plaster,
        LampGlow, WindowGlow, SignRed, SignBlue, SignWhite, SignYellow, SignGreen, BeaconRed, ScreenBlue,
        GlassClear, Hedge, Leaf,
        Count,
    }

    /// <summary>Tavolozza del mondo: colori, tipi di superficie e texture generata a runtime.</summary>
    public static class WorldPalette
    {
        /// <summary>Lato della texture in pixel (16 x 16 = 256 colori al massimo).</summary>
        public const int TextureSize = 16;
        public const int MaxColors = TextureSize * TextureSize;

        static readonly Color[] Colors = new Color[(int)Pal.Count];
        static readonly PalSurface[] Surfaces = new PalSurface[(int)Pal.Count];

        static WorldPalette()
        {
            // Strade e terreno (toni della Fase 2).
            Def(Pal.Asphalt, "2E3036"); Def(Pal.AsphaltPatch, "383B42"); Def(Pal.Sidewalk, "9A968E");
            Def(Pal.Curb, "B3AEA4"); Def(Pal.Concrete, "A7A49C"); Def(Pal.ConcreteDark, "6E6C67");
            Def(Pal.Grass, "6FA35A"); Def(Pal.GrassDark, "4E8A3E"); Def(Pal.Soil, "5B4636"); Def(Pal.Gravel, "8C877D");
            Def(Pal.Marking, "E8E4D8"); Def(Pal.MarkingYellow, "E8C14A");
            // Vetri (lucidi).
            Def(Pal.Glass, "33475E", PalSurface.Gloss); Def(Pal.GlassDark, "1F2A38", PalSurface.Gloss);
            Def(Pal.GlassBlue, "3D6A8E", PalSurface.Gloss); Def(Pal.GlassTeal, "3E7A80", PalSurface.Gloss);
            Def(Pal.GlassShop, "4A6272", PalSurface.Gloss);
            // Tetti.
            Def(Pal.Roof, "4A4A50"); Def(Pal.RoofDark, "34343A"); Def(Pal.RoofLight, "8A8D92");
            // Facciate (le otto della Fase 2 più quattro nuove).
            Def(Pal.Facade0, "C9B79C"); Def(Pal.Facade1, "B5654A"); Def(Pal.Facade2, "8C8F96"); Def(Pal.Facade3, "D8D2C4");
            Def(Pal.Facade4, "6E7F8D"); Def(Pal.Facade5, "A88B6A"); Def(Pal.Facade6, "C4A35A"); Def(Pal.Facade7, "7D5A50");
            Def(Pal.Facade8, "E3D9C6"); Def(Pal.Facade9, "9FB4BF"); Def(Pal.Facade10, "B9A48E"); Def(Pal.Facade11, "5F6B78");
            // Materiali.
            Def(Pal.Brick, "9C4A32"); Def(Pal.BrickDark, "7A3A28"); Def(Pal.Stone, "D6CFC2"); Def(Pal.StoneDark, "B5AB9B");
            Def(Pal.Marble, "EDE8DF", PalSurface.Gloss);
            Def(Pal.Metal, "8A8D92"); Def(Pal.MetalDark, "2F3237"); Def(Pal.MetalLight, "C4C7CC"); Def(Pal.Chrome, "D9DCE0", PalSurface.Gloss);
            Def(Pal.Wood, "8B5A3C"); Def(Pal.WoodDark, "5E3B27");
            Def(Pal.Water, "4FA3C7", PalSurface.Gloss);
            // Colori pieni.
            Def(Pal.White, "F2F2F0"); Def(Pal.Black, "151515"); Def(Pal.Red, "C0392B"); Def(Pal.Blue, "2C6FBB");
            Def(Pal.Yellow, "F1C40F"); Def(Pal.Green, "27AE60"); Def(Pal.Orange, "E67E22"); Def(Pal.Teal, "1A9C8C");
            // Edifici speciali.
            Def(Pal.PoliceBlue, "1F4E8C"); Def(Pal.PoliceWhite, "EEF1F5"); Def(Pal.BankGold, "C9A54B", PalSurface.Gloss);
            Def(Pal.VaultSteel, "7E8790", PalSurface.Gloss);
            Def(Pal.AwningRed, "B8403A"); Def(Pal.AwningGreen, "2E7D5B"); Def(Pal.AwningBlue, "2F5D8C");
            Def(Pal.AwningYellow, "D9A43A"); Def(Pal.AwningStripe, "EDE6D6");
            // Interni.
            Def(Pal.FloorTile, "D9D4CA"); Def(Pal.FloorDark, "5A5650"); Def(Pal.Carpet, "7A2E2E"); Def(Pal.Rubber, "222326");
            Def(Pal.DarkVoid, "0B0C0F"); Def(Pal.Plaster, "EAE4D8");
            // Luci (emissive).
            Def(Pal.LampGlow, "FFF1C1", PalSurface.Glow); Def(Pal.WindowGlow, "FFE2A0", PalSurface.Glow);
            Def(Pal.SignRed, "FF5A4E", PalSurface.Glow); Def(Pal.SignBlue, "5AB4FF", PalSurface.Glow);
            Def(Pal.SignWhite, "FFFFFF", PalSurface.Glow); Def(Pal.SignYellow, "FFD25A", PalSurface.Glow);
            Def(Pal.SignGreen, "5AF09A", PalSurface.Glow); Def(Pal.BeaconRed, "FF2A2A", PalSurface.Glow);
            Def(Pal.ScreenBlue, "4AA8FF", PalSurface.Glow);
            // Vetro trasparente (l'alfa sta nel materiale).
            Def(Pal.GlassClear, "B8D4E6", PalSurface.Clear);
            Def(Pal.Hedge, "3F7A3A"); Def(Pal.Leaf, "5C9A48");
        }

        static void Def(Pal p, string hex, PalSurface s = PalSurface.Matte)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            Colors[(int)p] = c;
            Surfaces[(int)p] = s;
        }

        public static Color ColorOf(Pal p) => Colors[(int)p];
        public static PalSurface SurfaceOf(Pal p) => Surfaces[(int)p];

        /// <summary>Coordinate UV del centro del pixel di un colore (indice nella texture).</summary>
        public static Vector2 Uv(int index)
        {
            int x = index % TextureSize, y = index / TextureSize;
            return new Vector2((x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize);
        }

        /// <summary>
        /// Crea la texture della tavolozza: prima i colori di <see cref="Pal"/>, poi i colori extra
        /// (colori pieni dei modelli Kenney, in ordine fisso). Texture sRGB, filtro "point", senza mipmap.
        /// </summary>
        public static Texture2D CreateTexture(IReadOnlyList<Color> extra)
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, false)
            {
                name = "WorldPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[MaxColors];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255); // magenta = errore visibile
            for (int i = 0; i < (int)Pal.Count; i++) pixels[i] = Colors[i];
            if (extra != null)
                for (int i = 0; i < extra.Count && (int)Pal.Count + i < MaxColors; i++)
                {
                    var c = extra[i];
                    c.a = 1f;
                    pixels[(int)Pal.Count + i] = c;
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
