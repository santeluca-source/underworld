using System.Collections.Generic;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>Lato di un edificio (verso cui guarda la faccia).</summary>
    public enum Side : byte { South, North, West, East }

    /// <summary>
    /// Apertura in un muro (porta o finestra). <see cref="Offset"/> = centro dell'apertura lungo il muro,
    /// misurato dal centro del muro (asse x per i muri South/North, asse z per West/East).
    /// </summary>
    public readonly struct Opening
    {
        public readonly Side Side;
        public readonly float Offset, Width, Height, Sill;
        /// <summary>Se non null: lastra di vetro (con collider) dentro l'apertura.</summary>
        public readonly Pal? Glass;

        public Opening(Side side, float offset, float width, float height, float sill = 0f, Pal? glass = null)
        {
            Side = side;
            Offset = offset;
            Width = width;
            Height = height;
            Sill = sill;
            Glass = glass;
        }

        /// <summary>Porta aperta (vano libero), alta 3 m.</summary>
        public static Opening Door(Side side, float offset, float width = 2.4f, float height = 3f) => new Opening(side, offset, width, height);

        /// <summary>Vetrina o finestra con vetro trasparente.</summary>
        public static Opening Window(Side side, float offset, float width, float height = 2.4f, float sill = 0.6f, Pal glass = Pal.GlassClear)
            => new Opening(side, offset, width, height, sill, glass);
    }

    /// <summary>Stile delle facciate degli edifici pieni.</summary>
    public enum FacadeStyle : byte
    {
        /// <summary>Fasce di vetro continue a ogni piano (look della Fase 2).</summary>
        Bands,
        /// <summary>Fasce di vetro con lesene verticali sporgenti.</summary>
        Pilasters,
        /// <summary>Torre di vetro con solette del colore della facciata.</summary>
        GlassTower,
    }

    /// <summary>Descrizione di un edificio pieno (non si entra).</summary>
    public struct BuildingSpec
    {
        public float Width, Depth, Height;
        public Pal Facade, Glass, Roof;
        public FacadeStyle Style;
        public float FloorHeight;
        /// <summary>Piano terra con vetrine scure e cornice.</summary>
        public bool ShopFront;
        /// <summary>Numero di macchine sul tetto (0..3).</summary>
        public int RoofUnits;
        public bool WaterTank;
        /// <summary>Antenna con luce rossa (torri alte).</summary>
        public bool Antenna;

        public static BuildingSpec Make(float w, float d, float h, Pal facade, Pal glass) => new BuildingSpec
        {
            Width = w, Depth = d, Height = h, Facade = facade, Glass = glass, Roof = Pal.Roof,
            Style = FacadeStyle.Bands, FloorHeight = 3.2f, ShopFront = true, RoofUnits = 1,
        };
    }

    /// <summary>
    /// Pezzi di città riusabili (edifici, stanze con porte, lampioni, alberi, arredo, portali di uscita, confini).
    /// Sono funzioni sopra <see cref="BuildContext"/>: ogni area (Downtown, Industrial, ...) le può usare.
    /// Regola contro lo z-fighting: due superfici non stanno mai sullo stesso piano (scarto minimo 2 cm),
    /// le facce nascoste dentro un'altra forma non si disegnano.
    /// </summary>
    public static class CityKit
    {
        public const float SidewalkTop = 0.15f;

        /// <summary>Direzione verso l'esterno di un lato.</summary>
        public static Vector3 Outward(Side s) => s switch
        {
            Side.South => Vector3.back,
            Side.North => Vector3.forward,
            Side.West => Vector3.left,
            _ => Vector3.right,
        };

        /// <summary>Rotazione (gradi) di chi guarda verso l'esterno dal lato.</summary>
        public static float Yaw(Side s) => s switch
        {
            Side.South => 180f,
            Side.North => 0f,
            Side.West => 270f,
            _ => 90f,
        };

        // ------------------------------------------------------------------ stanze con porte

        /// <summary>
        /// Edificio cavo in cui si entra: quattro muri con aperture, pavimento, tetto (il fondo del tetto è il soffitto).
        /// <paramref name="min"/>/<paramref name="max"/>: angoli della pianta (y ignorata); <paramref name="baseY"/>: dove appoggia.
        /// Il pavimento interno sta 5 cm sopra la base. Ritorna l'altezza del pavimento.
        /// </summary>
        public static float Room(this BuildContext ctx, Vector3 min, Vector3 max, float baseY, float height, float wall,
            Pal wallColor, Pal floorColor, Pal roofColor, IReadOnlyList<Opening> openings, bool roof = true)
        {
            float top = baseY + height;
            float floorY = baseY + 0.05f;
            // Pavimento (dentro i muri).
            ctx.BoxMinMax(new Vector3(min.x + wall, baseY, min.z + wall), new Vector3(max.x - wall, floorY, max.z - wall), floorColor);

            Wall(ctx, Side.South, min.x, max.x, min.z, min.z + wall, baseY, top, wallColor, openings, roof);
            Wall(ctx, Side.North, min.x, max.x, max.z - wall, max.z, baseY, top, wallColor, openings, roof);
            Wall(ctx, Side.West, min.z + wall, max.z - wall, min.x, min.x + wall, baseY, top, wallColor, openings, roof);
            Wall(ctx, Side.East, min.z + wall, max.z - wall, max.x - wall, max.x, baseY, top, wallColor, openings, roof);

            if (roof)
                ctx.BoxMinMax(new Vector3(min.x, top, min.z), new Vector3(max.x, top + 0.35f, max.z), roofColor, true, BoxFaces.All);
            return floorY;
        }

        /// <summary>Un muro diviso dalle aperture. a0..a1 = lungo il muro; t0..t1 = spessore.</summary>
        static void Wall(BuildContext ctx, Side side, float a0, float a1, float t0, float t1, float y0, float y1,
            Pal color, IReadOnlyList<Opening> openings, bool roofed)
        {
            bool alongX = side == Side.South || side == Side.North;
            float mid = (a0 + a1) * 0.5f;
            var faces = roofed ? BoxFaces.Sides : BoxFaces.NoBottom;

            // Aperture di questo lato, ordinate lungo il muro.
            var mine = new List<Opening>();
            if (openings != null)
                foreach (var o in openings) if (o.Side == side) mine.Add(o);
            mine.Sort((p, q) => p.Offset.CompareTo(q.Offset));

            float cursor = a0;
            foreach (var o in mine)
            {
                float o0 = Mathf.Max(a0, mid + o.Offset - o.Width * 0.5f);
                float o1 = Mathf.Min(a1, mid + o.Offset + o.Width * 0.5f);
                if (o0 > cursor + 0.01f) Seg(cursor, o0, y0, y1, faces);
                float openTop = Mathf.Min(y1, y0 + o.Sill + o.Height);
                if (y1 - openTop > 0.01f) Seg(o0, o1, openTop, y1, roofed ? BoxFaces.Sides | BoxFaces.NegY : BoxFaces.All);
                if (o.Sill > 0.01f) Seg(o0, o1, y0, y0 + o.Sill, BoxFaces.NoBottom);
                if (o.Glass.HasValue)
                {
                    // Lastra al centro del muro, più sottile del muro (niente facce sullo stesso piano).
                    float tc = (t0 + t1) * 0.5f;
                    var c = alongX
                        ? new Vector3((o0 + o1) * 0.5f, (y0 + o.Sill + openTop) * 0.5f, tc)
                        : new Vector3(tc, (y0 + o.Sill + openTop) * 0.5f, (o0 + o1) * 0.5f);
                    float len = o1 - o0 - 0.02f, hgt = openTop - (y0 + o.Sill) - 0.02f;
                    var size = alongX ? new Vector3(len, hgt, 0.06f) : new Vector3(0.06f, hgt, len);
                    ctx.Box(c, size, o.Glass.Value, 0f, true, BoxFaces.All);
                }
                cursor = Mathf.Max(cursor, o1);
            }
            if (a1 > cursor + 0.01f) Seg(cursor, a1, y0, y1, faces);

            void Seg(float s0, float s1, float yy0, float yy1, BoxFaces f)
            {
                if (alongX) ctx.BoxMinMax(new Vector3(s0, yy0, t0), new Vector3(s1, yy1, t1), color, true, f);
                else ctx.BoxMinMax(new Vector3(t0, yy0, s0), new Vector3(t1, yy1, s1), color, true, f);
            }
        }

        /// <summary>
        /// Fascia sottile sporgente sui quattro lati esterni di un edificio cavo (finestre finte, cornici).
        /// È incassata 10 cm nel muro, così la faccia interna non si vede mai.
        /// </summary>
        public static void WrapBand(this BuildContext ctx, Vector3 min, Vector3 max, float y0, float y1, float outset, Pal color,
            Side? skip = null)
        {
            float t = 0.1f + outset;
            if (skip != Side.South) ctx.BoxMinMax(new Vector3(min.x - outset, y0, min.z - outset), new Vector3(max.x + outset, y1, min.z - outset + t), color, false, BoxFaces.All);
            if (skip != Side.North) ctx.BoxMinMax(new Vector3(min.x - outset, y0, max.z + outset - t), new Vector3(max.x + outset, y1, max.z + outset), color, false, BoxFaces.All);
            // I lati West/East stanno tra le fasce South/North (niente volumi sovrapposti agli angoli).
            if (skip != Side.West) ctx.BoxMinMax(new Vector3(min.x - outset, y0, min.z - outset + t), new Vector3(min.x - outset + t, y1, max.z + outset - t), color, false, BoxFaces.All);
            if (skip != Side.East) ctx.BoxMinMax(new Vector3(max.x + outset - t, y0, min.z - outset + t), new Vector3(max.x + outset, y1, max.z + outset - t), color, false, BoxFaces.All);
        }

        // ------------------------------------------------------------------ edifici pieni

        /// <summary>
        /// Edificio pieno (un solo collider): corpo, fasce di vetro per piano, piano terra con vetrine, parapetto, macchine sul tetto.
        /// <paramref name="basePos"/> = centro del fondo. Ritorna l'altezza del tetto.
        /// </summary>
        public static float Building(this BuildContext ctx, Vector3 basePos, in BuildingSpec s)
        {
            float w = s.Width, d = s.Depth, h = s.Height;
            float fh = s.FloorHeight > 0.5f ? s.FloorHeight : 3.2f;
            float y0 = basePos.y, top = y0 + h;
            var c = new Vector3(basePos.x, y0 + h * 0.5f, basePos.z);
            int floors = Mathf.Max(1, Mathf.FloorToInt(h / fh));

            if (s.Style == FacadeStyle.GlassTower)
            {
                // Corpo di vetro, solette del colore della facciata ogni piano.
                ctx.Box(c, new Vector3(w, h, d), s.Glass);
                for (int f = 1; f < floors; f++)
                {
                    float y = y0 + f * fh;
                    ctx.Box(new Vector3(c.x, y, c.z), new Vector3(w + 0.16f, 0.35f, d + 0.16f), s.Facade, 0f, false, BoxFaces.All);
                }
                // Spigoli pieni.
                float k = 0.7f;
                foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    ctx.Box(new Vector3(c.x + sx * (w * 0.5f - k * 0.5f + 0.1f), c.y, c.z + sz * (d * 0.5f - k * 0.5f + 0.1f)),
                        new Vector3(k, h, k), s.Facade, 0f, false, BoxFaces.NoBottom);
            }
            else
            {
                ctx.Box(c, new Vector3(w, h, d), s.Facade);
                int first = s.ShopFront ? 1 : 0;
                for (int f = first; f < floors; f++)
                {
                    float yb = y0 + f * fh + 0.95f;
                    if (yb + 1.35f > top - 0.5f) break;
                    ctx.Box(new Vector3(c.x, yb + 0.675f, c.z), new Vector3(w + 0.12f, 1.35f, d + 0.12f), s.Glass, 0f, false, BoxFaces.All);
                }
                if (s.Style == FacadeStyle.Pilasters && h > 6f)
                {
                    float y1 = s.ShopFront ? y0 + 3.5f : y0;
                    float ph = top - y1;
                    int nx = Mathf.Max(2, Mathf.RoundToInt(w / 3.6f));
                    int nz = Mathf.Max(2, Mathf.RoundToInt(d / 3.6f));
                    // Le lesene stanno 5 cm dentro gli spigoli: nessuna faccia sullo stesso piano dei lati del corpo.
                    for (int i = 0; i <= nx; i++)
                    {
                        float x = c.x - w * 0.5f + 0.3f + i * (w - 0.6f) / nx;
                        ctx.Box(new Vector3(x, y1 + ph * 0.5f, c.z - d * 0.5f - 0.05f), new Vector3(0.5f, ph, 0.3f), s.Facade, 0f, false, BoxFaces.NoBottom);
                        ctx.Box(new Vector3(x, y1 + ph * 0.5f, c.z + d * 0.5f + 0.05f), new Vector3(0.5f, ph, 0.3f), s.Facade, 0f, false, BoxFaces.NoBottom);
                    }
                    for (int i = 1; i < nz; i++)
                    {
                        float z = c.z - d * 0.5f + 0.3f + i * (d - 0.6f) / nz;
                        ctx.Box(new Vector3(c.x - w * 0.5f - 0.05f, y1 + ph * 0.5f, z), new Vector3(0.3f, ph, 0.5f), s.Facade, 0f, false, BoxFaces.NoBottom);
                        ctx.Box(new Vector3(c.x + w * 0.5f + 0.05f, y1 + ph * 0.5f, z), new Vector3(0.3f, ph, 0.5f), s.Facade, 0f, false, BoxFaces.NoBottom);
                    }
                }
            }

            if (s.ShopFront && s.Style != FacadeStyle.GlassTower)
            {
                // Piano terra: vetrine scure e cornice sporgente.
                ctx.Box(new Vector3(c.x, y0 + 1.75f, c.z), new Vector3(w + 0.16f, 2.6f, d + 0.16f), Pal.GlassShop, 0f, false, BoxFaces.Sides);
                ctx.Box(new Vector3(c.x, y0 + 3.3f, c.z), new Vector3(w + 0.4f, 0.4f, d + 0.4f), Pal.ConcreteDark, 0f, false, BoxFaces.All);
            }

            Roof(ctx, new Vector3(c.x, top, c.z), w, d, s);
            return top;
        }

        static void Roof(BuildContext ctx, Vector3 roofCenter, float w, float d, in BuildingSpec s)
        {
            float top = roofCenter.y;
            // Superficie del tetto (2 cm sopra il corpo) e parapetto.
            ctx.Box(roofCenter + Vector3.up * 0.01f, new Vector3(w - 0.5f, 0.02f, d - 0.5f), s.Roof, 0f, false, BoxFaces.PosY);
            const float pt = 0.3f, ph = 0.8f;
            var pc = s.Facade;
            ctx.Box(new Vector3(roofCenter.x, top + ph * 0.5f, roofCenter.z - d * 0.5f + pt * 0.5f), new Vector3(w, ph, pt), pc, 0f, false);
            ctx.Box(new Vector3(roofCenter.x, top + ph * 0.5f, roofCenter.z + d * 0.5f - pt * 0.5f), new Vector3(w, ph, pt), pc, 0f, false);
            ctx.Box(new Vector3(roofCenter.x - w * 0.5f + pt * 0.5f, top + ph * 0.5f, roofCenter.z), new Vector3(pt, ph, d - pt * 2f), pc, 0f, false);
            ctx.Box(new Vector3(roofCenter.x + w * 0.5f - pt * 0.5f, top + ph * 0.5f, roofCenter.z), new Vector3(pt, ph, d - pt * 2f), pc, 0f, false);

            // Macchine del tetto (posizioni dal seme dell'area).
            for (int i = 0; i < s.RoofUnits; i++)
            {
                float uw = ctx.Range(1.6f, Mathf.Max(1.8f, w * 0.3f));
                float ud = ctx.Range(1.4f, Mathf.Max(1.6f, d * 0.3f));
                float uh = ctx.Range(1.0f, 2.2f);
                float ux = ctx.Range(-w * 0.5f + uw * 0.5f + 0.8f, w * 0.5f - uw * 0.5f - 0.8f);
                float uz = ctx.Range(-d * 0.5f + ud * 0.5f + 0.8f, d * 0.5f - ud * 0.5f - 0.8f);
                if (w < uw + 2f || d < ud + 2f) break;
                var p = new Vector3(roofCenter.x + ux, top + 0.02f + uh * 0.5f, roofCenter.z + uz);
                ctx.Box(p, new Vector3(uw, uh, ud), i % 2 == 0 ? Pal.MetalLight : Pal.Metal, 0f, false);
                ctx.Box(p + new Vector3(0f, uh * 0.5f + 0.06f, 0f), new Vector3(uw * 0.6f, 0.12f, ud * 0.6f), Pal.MetalDark, 0f, false);
            }
            if (s.WaterTank && w > 7f && d > 7f)
            {
                var tp = new Vector3(roofCenter.x - w * 0.25f, top + 0.02f, roofCenter.z + d * 0.25f);
                for (int i = 0; i < 4; i++)
                {
                    var leg = tp + new Vector3(i % 2 == 0 ? -0.8f : 0.8f, 1.0f, i < 2 ? -0.8f : 0.8f);
                    ctx.Box(leg, new Vector3(0.15f, 2f, 0.15f), Pal.WoodDark, 0f, false);
                }
                ctx.Prism(tp + Vector3.up * 2f, 1.3f, 2.4f, 8, Pal.Wood, false);
                ctx.Prism(tp + Vector3.up * 4.4f, 1.4f, 0.5f, 8, Pal.RoofDark, false);
            }
            if (s.Antenna)
            {
                ctx.Prism(roofCenter + Vector3.up * 0.02f, 0.18f, 12f, 6, Pal.MetalDark, false);
                ctx.Box(roofCenter + Vector3.up * 12.2f, new Vector3(0.45f, 0.45f, 0.45f), Pal.BeaconRed, 0f, false, BoxFaces.All);
            }
        }

        // ------------------------------------------------------------------ strada e arredo

        /// <summary>Lampione: palo, braccio verso <paramref name="armYaw"/>, lampada luminosa.</summary>
        public static void Lamp(this BuildContext ctx, Vector3 foot, float armYaw, float height = 6f)
        {
            ctx.Prism(foot, 0.11f, height, 6, Pal.MetalDark, false);
            ctx.Collider(foot + Vector3.up * (height * 0.5f), new Vector3(0.22f, height, 0.22f));
            var dir = Quaternion.Euler(0f, armYaw, 0f) * Vector3.forward;
            ctx.Box(foot + Vector3.up * (height - 0.1f) + dir * 0.7f, new Vector3(0.12f, 0.12f, 1.4f), Pal.MetalDark, armYaw, false, BoxFaces.All);
            ctx.Box(foot + Vector3.up * (height - 0.3f) + dir * 1.3f, new Vector3(0.45f, 0.18f, 0.7f), Pal.LampGlow, armYaw, false, BoxFaces.All);
        }

        /// <summary>Panchina di legno (collider sulla seduta).</summary>
        public static void Bench(this BuildContext ctx, Vector3 foot, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            ctx.Box(foot + Vector3.up * 0.45f, new Vector3(1.9f, 0.08f, 0.5f), Pal.Wood, rot, true, BoxFaces.All);
            ctx.Box(foot + Vector3.up * 0.8f + rot * new Vector3(0f, 0f, -0.24f), new Vector3(1.9f, 0.4f, 0.06f), Pal.Wood, rot, false, BoxFaces.All);
            foreach (float sx in new[] { -0.8f, 0.8f })
                ctx.Box(foot + rot * new Vector3(sx, 0.2f, 0f) , new Vector3(0.08f, 0.4f, 0.45f), Pal.MetalDark, rot, false);
        }

        /// <summary>Cestino dei rifiuti.</summary>
        public static void Bin(this BuildContext ctx, Vector3 foot)
        {
            ctx.Prism(foot, 0.28f, 0.85f, 8, Pal.MetalDark);
            ctx.Prism(foot + Vector3.up * 0.85f, 0.31f, 0.08f, 8, Pal.Metal, false);
        }

        /// <summary>Idrante rosso.</summary>
        public static void Hydrant(this BuildContext ctx, Vector3 foot)
        {
            ctx.Prism(foot, 0.14f, 0.6f, 8, Pal.Red);
            ctx.Prism(foot + Vector3.up * 0.6f, 0.1f, 0.15f, 8, Pal.Red, false);
            ctx.Box(foot + Vector3.up * 0.4f, new Vector3(0.42f, 0.1f, 0.1f), Pal.Red, 0f, false, BoxFaces.All);
        }

        /// <summary>Semaforo: palo e lanterna con le tre luci verso <paramref name="yaw"/>.</summary>
        public static void TrafficLight(this BuildContext ctx, Vector3 foot, float yaw, int lit)
        {
            ctx.Prism(foot, 0.1f, 3.4f, 6, Pal.MetalDark, false);
            ctx.Collider(foot + Vector3.up * 1.7f, new Vector3(0.2f, 3.4f, 0.2f));
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var head = foot + Vector3.up * 3.9f;
            ctx.Box(head, new Vector3(0.36f, 1.05f, 0.3f), Pal.Black, rot, false, BoxFaces.All);
            var f = rot * Vector3.forward;
            Pal[] on = { Pal.BeaconRed, Pal.SignYellow, Pal.SignGreen };
            for (int i = 0; i < 3; i++)
            {
                var p = head + Vector3.up * (0.32f - i * 0.32f) + f * 0.17f;
                ctx.Box(p, new Vector3(0.2f, 0.2f, 0.04f), i == lit ? on[i] : Pal.MetalDark, rot, false, BoxFaces.All & ~BoxFaces.NegZ);
            }
        }

        /// <summary>Albero del Nature Kit con una piccola aiuola di terra sotto.</summary>
        public static void StreetTree(this BuildContext ctx, Vector3 foot, string modelId, float scale = 1f)
        {
            ctx.Box(foot + Vector3.up * 0.015f, new Vector3(1.4f, 0.03f, 1.4f), Pal.Soil, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            ctx.Model(modelId, foot + Vector3.up * 0.03f, ctx.Range(0f, 360f), -1, true, scale);
        }

        /// <summary>Insegna: pannello con la scritta a pixel sopra, rivolta verso <paramref name="yaw"/>.</summary>
        public static void Sign(this BuildContext ctx, string text, Vector3 center, float yaw, float pixel, Pal panel, Pal letters,
            float margin = 0.35f)
        {
            float w = PixelFont.Width(text) * pixel;
            int lines = text.Split('\n').Length;
            float h = (lines * PixelFont.LineHeight - (PixelFont.LineHeight - PixelFont.GlyphHeight)) * pixel;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float maxW = 0f;
            foreach (var line in text.Split('\n')) maxW = Mathf.Max(maxW, PixelFont.Width(line) * pixel);
            ctx.Box(center, new Vector3(maxW + margin * 2f, h + margin * 2f, 0.2f), panel, rot, false, BoxFaces.All);
            ctx.Text(text, center + rot * Vector3.forward * 0.1f, yaw, pixel, letters, 0.06f);
        }

        /// <summary>
        /// Portale di uscita verso un'altra area: arco con insegna luminosa sul bordo della mappa e tunnel buio dietro.
        /// <paramref name="mouth"/> = centro della strada sul bordo; <paramref name="outward"/> = direzione verso fuori.
        /// Il tunnel è chiuso in fondo (collider). Ritorna il punto centrale dentro l'arco (per il punto speciale di uscita).
        /// </summary>
        public static Vector3 Gateway(this BuildContext ctx, Vector3 mouth, Side outward, float roadWidth, string label)
        {
            float yaw = Yaw(outward);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 f = rot * Vector3.forward, r = rot * Vector3.right;
            float half = roadWidth * 0.5f + 1.5f;
            const float depth = 18f, inner = 7.5f;

            // Pilastri e trave dell'arco (lato città).
            foreach (float s in new[] { -1f, 1f })
                ctx.Box(mouth + r * (s * (half + 1.1f)) + Vector3.up * 6f, new Vector3(2.2f, 12f, 2.6f), Pal.StoneDark, rot);
            // Trave un po' più corta e sottile dei pilastri: nessuna faccia sullo stesso piano.
            ctx.Box(mouth + Vector3.up * 10.5f, new Vector3(half * 2f + 4.2f, 3f, 2.4f), Pal.StoneDark, rot, false, BoxFaces.All);
            ctx.Box(mouth + Vector3.up * 12.3f, new Vector3(half * 2f + 5f, 0.6f, 3f), Pal.Stone, rot, false, BoxFaces.All);
            // Insegna verso la città (si legge da dentro la mappa).
            // Scritte lunghe (es. INDUSTRIAL) si rimpiccioliscono: l'insegna resta dentro i pilastri.
            float px = Mathf.Min(0.32f, (half * 2f + 1.6f) / Mathf.Max(1, PixelFont.Width(label)));
            ctx.Sign(label, mouth + Vector3.up * 10.5f - f * 1.32f, yaw + 180f, px, Pal.PoliceBlue, Pal.SignWhite, 0.2f);
            // Fasce luminose sui pilastri (l'uscita è più luminosa dei lati).
            foreach (float s in new[] { -1f, 1f })
                ctx.Box(mouth + r * (s * (half + 1.1f)) + Vector3.up * 4.5f - f * 1.32f, new Vector3(0.5f, 6f, 0.06f), Pal.SignYellow, rot, false, BoxFaces.All);

            // Tunnel: muri, tetto, fondo buio.
            var tunnelCenter = mouth + f * (1.3f + depth * 0.5f);
            foreach (float s in new[] { -1f, 1f })
                ctx.Box(tunnelCenter + r * (s * (half + 0.5f)) + Vector3.up * (inner * 0.5f), new Vector3(1f, inner, depth), Pal.ConcreteDark, rot);
            ctx.Box(tunnelCenter + Vector3.up * (inner + 0.5f), new Vector3(half * 2f + 2f, 1f, depth), Pal.ConcreteDark, rot, false, BoxFaces.All);
            ctx.Box(mouth + f * (1.3f + depth + 0.5f) + Vector3.up * (inner * 0.5f), new Vector3(half * 2f, inner, 1f), Pal.DarkVoid, rot);
            // Strisce luminose sul soffitto del tunnel.
            for (int i = 0; i < 4; i++)
                ctx.Box(mouth + f * (3f + i * 4f) + Vector3.up * (inner - 0.03f), new Vector3(half * 1.2f, 0.06f, 0.4f), Pal.LampGlow, rot, false, BoxFaces.NegY | BoxFaces.Sides);
            return mouth + f * 2.5f;
        }
    }
}
