using System.Collections.Generic;
using Underworld.Core;
using UnityEngine;

namespace Underworld.World.Areas
{
    /// <summary>
    /// Downtown (circa 300 x 300 m), costruito a runtime con il seme fisso.
    /// Pianta: griglia 7 x 7 di isolati da 28 m, strade da 12 m (passo 40 m). Asse x = est, asse z = nord.
    /// Al centro la piazza con la fontana (spawn). Punto di riferimento: la torre UNDERWORLD a nord della piazza,
    /// dietro la banca, visibile da quasi ogni strada. Seconda torre (ottagonale) a est, gru del cantiere a sud-ovest.
    /// Uscite: est (Industrial, strada z = 20), nord (Suburbs, strada x = -20), sud (Outskirts, strada x = 20).
    ///
    /// Isolati speciali (gx, gz con centro = -120 + 40 * g):
    ///   (3,3) piazza · (3,4) banca · (3,5) torre · (1,3) polizia · (4,3) concessionaria · (2,2) negozio
    ///   (3,2) abbigliamento · (4,4) parcheggio pubblico · (2,4) appartamenti · (5,2) vicolo e covo
    ///   (5,3) torre ottagonale · (5,5) parco · (1,1) cantiere
    /// </summary>
    [AreaBuilderFor(AreaId.Downtown, Seed = 1337)]
    public sealed class DowntownBuilder : IAreaBuilder
    {
        // ------------------------------------------------------------------ misure della pianta
        const int Grid = 7;
        const float Pitch = 40f, Block = 28f, Road = 12f;
        const float HalfBlock = Block * 0.5f;          // 14
        const float Inner = HalfBlock - 3f;             // 11: lotti dentro il marciapiede da 3 m
        const float Edge = 150f;                        // confine giocabile
        const float RingOuter = 146f;                   // bordo esterno della strada di circonvallazione
        const float Sw = CityKit.SidewalkTop;           // 0,15

        // Strade delle uscite.
        const float ExitNorthX = -20f, ExitEastZ = 20f, ExitSouthX = 20f;

        static float BlockCenter(int g) => -120f + Pitch * g;
        static float RoadCenter(int i) => -140f + Pitch * i;

        static readonly Pal[] Facades =
        {
            Pal.Facade0, Pal.Facade1, Pal.Facade2, Pal.Facade3, Pal.Facade4, Pal.Facade5,
            Pal.Facade6, Pal.Facade7, Pal.Facade8, Pal.Facade9, Pal.Facade10, Pal.Facade11,
        };
        static readonly Pal[] Glasses = { Pal.Glass, Pal.GlassDark, Pal.GlassBlue, Pal.GlassTeal };
        static readonly string[] CivilCars = { "car.sedan", "car.sedan_sports", "car.hatchback", "car.suv", "car.suv_luxury", "car.van", "car.sedan", "car.hatchback" };
        static readonly string[] StreetTrees = { "nature.tree_default", "nature.tree_oak", "nature.tree_detailed", "nature.tree_default_dark" };
        static readonly string[] ParkTrees =
        {
            "nature.tree_oak", "nature.tree_default", "nature.tree_detailed", "nature.tree_fat", "nature.tree_tall",
            "nature.tree_oak_dark", "nature.tree_cone", "nature.tree_plateau", "nature.tree_oak_fall",
        };

        BuildContext _ctx;
        /// <summary>Zone dove non mettere auto parcheggiate e alberi di strada (ingressi, spawn, uscite).</summary>
        readonly List<Rect> _keepClear = new List<Rect>();
        readonly bool[,] _special = new bool[Grid, Grid];

        public void Build(BuildContext ctx)
        {
            _ctx = ctx;
            MarkSpecialBlocks();
            KeepClearZones();

            Ground();
            Roads();

            Plaza(new Vector3(0f, 0f, 0f));
            Bank(new Vector3(0f, 0f, 40f));
            LandmarkTower(new Vector3(0f, 0f, 80f));
            PoliceStation(new Vector3(-80f, 0f, 0f));
            Dealership(new Vector3(40f, 0f, 0f));
            GeneralStore(new Vector3(-40f, 0f, -40f));
            ClothingStore(new Vector3(0f, 0f, -40f));
            PublicGarage(new Vector3(40f, 0f, 40f));
            Apartments(new Vector3(-40f, 0f, 40f));
            AlleyAndHideout(new Vector3(80f, 0f, -40f));
            OctagonTower(new Vector3(80f, 0f, 0f));
            Park(new Vector3(80f, 0f, 80f));
            ConstructionSite(new Vector3(-80f, 0f, -80f));

            for (int gx = 0; gx < Grid; gx++)
            for (int gz = 0; gz < Grid; gz++)
                if (!_special[gx, gz]) RegularBlock(gx, gz);

            StreetFurniture();
            ParkedCars();
            Exits();
            Perimeter();
            PreviewShots();
        }

        // ------------------------------------------------------------------ pianta

        void MarkSpecialBlocks()
        {
            int[,] special = { { 3, 3 }, { 3, 4 }, { 3, 5 }, { 1, 3 }, { 4, 3 }, { 2, 2 }, { 3, 2 }, { 4, 4 }, { 2, 4 }, { 5, 2 }, { 5, 3 }, { 5, 5 }, { 1, 1 } };
            for (int i = 0; i < special.GetLength(0); i++) _special[special[i, 0], special[i, 1]] = true;
        }

        void KeepClearZones()
        {
            void Clear(float x, float z, float hw, float hd) => _keepClear.Add(new Rect(x - hw, z - hd, hw * 2f, hd * 2f));
            // Ingressi e punti speciali sulla strada.
            Clear(0f, 26f, 9f, 4f);            // scalinata della banca
            Clear(23f, 0f, 4f, 9f);             // uscita della concessionaria
            Clear(26f, 0f, 3f, 12f);            // vetrina della concessionaria
            Clear(-64f, 0f, 3f, 4f);            // ingresso della polizia (le auto della polizia stanno ai lati)
            Clear(40f, 23f, 6f, 5f);            // ingresso del parcheggio
            Clear(64f, -39f, 6f, 4f);           // vicolo, lato ovest
            Clear(96f, -39f, 6f, 4f);           // vicolo, lato est
            Clear(-42f, -27f, 9f, 3f);          // vetrina del negozio
            Clear(0f, -27f, 9f, 3f);            // vetrina dell'abbigliamento
            Clear(-40f, 28f, 11f, 2.5f);        // porte degli appartamenti
            Clear(BusStopPos.x, BusStopPos.z, 2.5f, 3.5f); // fermata del bus
            // Uscite e arrivi (strade libere per chi arriva).
            Clear(ExitNorthX, 130f, 8f, 24f);
            Clear(130f, ExitEastZ, 24f, 8f);
            Clear(ExitSouthX, -130f, 8f, 24f);
        }

        bool IsClear(Vector3 p, float margin = 0f)
        {
            foreach (var r in _keepClear)
                if (p.x > r.xMin - margin && p.x < r.xMax + margin && p.z > r.yMin - margin && p.z < r.yMax + margin) return false;
            return true;
        }

        void Ground()
        {
            // Asfalto unico (anche oltre il confine, sotto gli edifici di sfondo).
            _ctx.BoxMinMax(new Vector3(-215f, -0.5f, -215f), new Vector3(215f, 0f, 215f), Pal.Asphalt, true, BoxFaces.PosY);

            // Marciapiedi degli isolati (la piazza e il parcheggio hanno il loro fondo).
            for (int gx = 0; gx < Grid; gx++)
            for (int gz = 0; gz < Grid; gz++)
            {
                if ((gx == 3 && gz == 3)) continue;
                var c = new Vector3(BlockCenter(gx), 0f, BlockCenter(gz));
                _ctx.Box(c + Vector3.up * (Sw * 0.5f), new Vector3(Block, Sw, Block), Pal.Sidewalk);
            }

            // Marciapiede lungo il confine (146..150), aperto dove passano le strade delle uscite.
            PerimeterStrip(Side.North, ExitNorthX);
            PerimeterStrip(Side.South, ExitSouthX);
            PerimeterStrip(Side.East, ExitEastZ);
            PerimeterStrip(Side.West, float.NaN);
        }

        void PerimeterStrip(Side side, float gapAt)
        {
            const float w = Edge - RingOuter;
            float hy = Sw * 0.5f;
            bool alongX = side == Side.North || side == Side.South;
            float t = side == Side.North || side == Side.East ? RingOuter + w * 0.5f : -(RingOuter + w * 0.5f);
            float a0 = alongX ? -Edge : -RingOuter, a1 = alongX ? Edge : RingOuter; // i lati est/ovest stanno tra nord e sud
            var spans = new List<(float, float)>();
            if (float.IsNaN(gapAt)) spans.Add((a0, a1));
            else { spans.Add((a0, gapAt - Road * 0.5f)); spans.Add((gapAt + Road * 0.5f, a1)); }
            foreach (var (s0, s1) in spans)
            {
                float mid = (s0 + s1) * 0.5f, len = s1 - s0;
                if (alongX) _ctx.Box(new Vector3(mid, hy, t), new Vector3(len, Sw, w), Pal.Sidewalk);
                else _ctx.Box(new Vector3(t, hy, mid), new Vector3(w, Sw, len), Pal.Sidewalk);
            }
        }

        void Roads()
        {
            const float y = 0.01f;
            for (int i = 0; i <= Grid; i++)
            {
                float rc = RoadCenter(i);
                for (int j = 0; j < Grid; j++)
                {
                    float s0 = RoadCenter(j) + Road * 0.5f; // inizio del tratto tra due incroci
                    // Linea di mezzeria tratteggiata (3 tratti), lontana dalle strisce pedonali.
                    foreach (float off in new[] { 7f, 14f, 21f })
                    {
                        _ctx.Box(new Vector3(rc, y, s0 + off), new Vector3(0.22f, 0.02f, 3f), Pal.Marking, 0f, false, BoxFaces.PosY);
                        _ctx.Box(new Vector3(s0 + off, y, rc), new Vector3(3f, 0.02f, 0.22f), Pal.Marking, 0f, false, BoxFaces.PosY);
                    }
                }
            }
            // Strisce pedonali e linee di arresto agli incroci intorno alla piazza e sulle strade principali.
            float[] cross = { -60f, -20f, 20f, 60f };
            foreach (float ix in cross)
            foreach (float iz in cross)
            {
                if (Mathf.Abs(ix) > 30f && Mathf.Abs(iz) > 30f) continue; // solo la croce centrale
                Crosswalks(new Vector3(ix, 0f, iz));
            }
        }

        void Crosswalks(Vector3 c)
        {
            const float y = 0.01f;
            for (int dir = 0; dir < 4; dir++)
            {
                var rot = Quaternion.Euler(0f, dir * 90f, 0f);
                Vector3 f = rot * Vector3.forward, r = rot * Vector3.right;
                var band = c + f * (Road * 0.5f + 2f);
                for (int k = 0; k < 6; k++)
                {
                    float x = -4.5f + k * 1.8f;
                    _ctx.Box(band + r * x + Vector3.up * y, new Vector3(0.7f, 0.02f, 3f), Pal.Marking, dir * 90f, false, BoxFaces.PosY);
                }
                // Linea di arresto sulla corsia che entra nell'incrocio.
                _ctx.Box(c + f * (Road * 0.5f + 4.1f) - r * 3f + Vector3.up * y, new Vector3(5.6f, 0.02f, 0.4f), Pal.Marking, dir * 90f, false, BoxFaces.PosY);
            }
        }

        // ------------------------------------------------------------------ isolati normali

        void RegularBlock(int gx, int gz)
        {
            var c = new Vector3(BlockCenter(gx), Sw, BlockCenter(gz));
            int dist = Mathf.Max(Mathf.Abs(gx - 3), Mathf.Abs(gz - 3));
            int pattern = _ctx.Range(0, 4);
            const float lot = Inner * 2f; // 22
            const float gap = 0.6f;
            switch (pattern)
            {
                case 0: // un edificio grande (a volte torre a gradoni)
                    Tall(c, lot - 1f, lot - 1f, dist, true);
                    break;
                case 1: // due edifici affiancati lungo x
                    {
                        float w = (lot - gap) * 0.5f;
                        Tall(c + new Vector3(-(w + gap) * 0.5f, 0f, 0f), w - 0.4f, lot - 1f, dist, false);
                        Tall(c + new Vector3((w + gap) * 0.5f, 0f, 0f), w - 0.4f, lot - 1f, dist, false);
                    }
                    break;
                case 2: // due edifici lungo z
                    {
                        float d = (lot - gap) * 0.5f;
                        Tall(c + new Vector3(0f, 0f, -(d + gap) * 0.5f), lot - 1f, d - 0.4f, dist, false);
                        Tall(c + new Vector3(0f, 0f, (d + gap) * 0.5f), lot - 1f, d - 0.4f, dist, false);
                    }
                    break;
                default: // quattro edifici
                    {
                        float s = (lot - gap) * 0.5f;
                        for (int i = 0; i < 4; i++)
                        {
                            float ox = (i % 2 == 0 ? -1f : 1f) * (s + gap) * 0.5f;
                            float oz = (i < 2 ? -1f : 1f) * (s + gap) * 0.5f;
                            Tall(c + new Vector3(ox, 0f, oz), s - 0.4f - _ctx.Range(0f, 1.5f), s - 0.4f - _ctx.Range(0f, 1.5f), dist, false);
                        }
                    }
                    break;
            }
        }

        /// <summary>Edificio normale: altezza in base alla distanza dalla piazza (più alto al centro).</summary>
        void Tall(Vector3 basePos, float w, float d, int dist, bool allowSetback)
        {
            float maxH = dist <= 1 ? 52f : dist == 2 ? 34f : 20f;
            float minH = dist <= 1 ? 18f : dist == 2 ? 12f : 8f;
            float h = Mathf.Round(_ctx.Range(minH, maxH) / 3.2f) * 3.2f + 0.4f;
            var spec = BuildingSpec.Make(w, d, h, _ctx.Pick(Facades), _ctx.Pick(Glasses));
            int style = _ctx.Range(0, 10);
            spec.Style = style < 5 ? FacadeStyle.Bands : style < 8 ? FacadeStyle.Pilasters : FacadeStyle.GlassTower;
            if (spec.Style == FacadeStyle.GlassTower && h < 20f) spec.Style = FacadeStyle.Bands;
            spec.ShopFront = _ctx.Chance(0.7f);
            spec.RoofUnits = _ctx.Range(0, 4);
            spec.WaterTank = h < 26f && _ctx.Chance(0.35f);
            spec.Antenna = h > 40f && _ctx.Chance(0.6f);
            spec.Roof = _ctx.Chance(0.5f) ? Pal.Roof : Pal.RoofDark;

            if (allowSetback && h > 30f && _ctx.Chance(0.6f))
            {
                // Torre a gradoni: base larga e corpo più stretto sopra.
                float baseH = Mathf.Round(h * 0.35f / 3.2f) * 3.2f + 0.4f;
                var podium = spec;
                podium.Height = baseH;
                podium.Antenna = false;
                podium.WaterTank = false;
                podium.RoofUnits = 1;
                float top = _ctx.Building(basePos, podium);
                var upper = spec;
                upper.Width = w * 0.68f;
                upper.Depth = d * 0.68f;
                upper.Height = h - baseH + 6f;
                upper.ShopFront = false;
                _ctx.Building(new Vector3(basePos.x, top, basePos.z), upper);
            }
            else
            {
                _ctx.Building(basePos, spec);
            }
        }

        // ------------------------------------------------------------------ piazza

        void Plaza(Vector3 c)
        {
            // Pavimento della piazza (pietra chiara), anello scuro e aiuole agli angoli.
            _ctx.Box(c + Vector3.up * (Sw * 0.5f), new Vector3(Block, Sw, Block), Pal.Stone);
            _ctx.Prism(c + Vector3.up * Sw, 10.5f, 0.02f, 16, Pal.StoneDark, false, 11.25f);
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                var gc = c + new Vector3(sx * 10.5f, 0f, sz * 10.5f);
                _ctx.Box(gc + Vector3.up * (Sw + 0.02f), new Vector3(6.4f, 0.04f, 6.4f), Pal.Grass, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
                _ctx.Model(_ctx.Pick(ParkTrees), gc + new Vector3(sx * 0.4f, Sw + 0.04f, sz * 0.4f), _ctx.Range(0f, 360f), -1, true, _ctx.Range(0.85f, 1.05f));
                _ctx.Model("nature.bush", gc + new Vector3(-sx * 2f, Sw + 0.04f, sz * 1.6f), _ctx.Range(0f, 360f));
                _ctx.Model(i % 2 == 0 ? "nature.flower_red" : "nature.flower_yellow", gc + new Vector3(sx * 1.8f, Sw + 0.04f, -sz * 2.2f), 0f);
                _ctx.Model("nature.flower_purple", gc + new Vector3(-sx * 1.2f, Sw + 0.04f, -sz * 2.4f), 45f);
                _ctx.Lamp(c + new Vector3(sx * 7.2f, Sw, sz * 7.2f), Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg, 5f);
            }

            // Fontana: vasca, acqua, bordo a otto lati, colonna e coppa con getti d'acqua.
            var fb = c + Vector3.up * Sw;
            _ctx.Prism(fb, 4.2f, 0.45f, 8, Pal.StoneDark, true, 22.5f);
            _ctx.Prism(fb + Vector3.up * 0.45f, 3.7f, 0.02f, 8, Pal.Water, false, 22.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                _ctx.Box(fb + dir * 3.82f + Vector3.up * 0.65f, new Vector3(3.3f, 0.4f, 0.55f), Pal.Stone, a, false, BoxFaces.NoBottom);
            }
            _ctx.Prism(fb + Vector3.up * 0.47f, 0.55f, 2.2f, 8, Pal.Stone, false);
            _ctx.Prism(fb + Vector3.up * 2.67f, 1.4f, 0.3f, 8, Pal.Stone, false, 22.5f);
            _ctx.Prism(fb + Vector3.up * 2.97f, 1.2f, 0.02f, 8, Pal.Water, false, 22.5f);
            _ctx.Prism(fb + Vector3.up * 2.99f, 0.18f, 1.4f, 6, Pal.Water, false);
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward;
                _ctx.Box(fb + dir * 1.9f + Vector3.up * 1.6f, new Vector3(0.12f, 2.2f, 0.12f), Pal.Water, i * 90f + 45f, false, BoxFaces.All);
            }

            // Panchine rivolte verso la fontana.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                _ctx.Bench(fb + dir * 6.4f, a + 180f);
            }

            // Spawn: otto punti sull'anello intorno alla fontana, rivolti verso l'esterno.
            for (int i = 0; i < 8; i++)
            {
                float a = 22.5f + i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                _ctx.Anchor("spawn." + i, AnchorKind.Spawn, fb + dir * 8.2f, a, 1.5f);
            }

            // Bacheca delle missioni sul lato sud della piazza, rivolta verso la fontana.
            var mb = c + new Vector3(0f, Sw, -12.2f);
            foreach (float sx in new[] { -1.6f, 1.6f })
                _ctx.Box(mb + new Vector3(sx, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), Pal.MetalDark);
            _ctx.Box(mb + new Vector3(0f, 1.75f, 0f), new Vector3(3.4f, 1.7f, 0.18f), Pal.WoodDark, 0f, false, BoxFaces.All);
            _ctx.Box(mb + new Vector3(0f, 1.7f, 0.1f), new Vector3(3.0f, 1.3f, 0.04f), Pal.ScreenBlue, 0f, false, BoxFaces.All & ~BoxFaces.NegZ);
            _ctx.Box(mb + new Vector3(0f, 2.75f, 0f), new Vector3(3.6f, 0.3f, 0.5f), Pal.Black, 0f, false, BoxFaces.All);
            _ctx.Text("MISSIONS", mb + new Vector3(0f, 1.95f, 0.12f), 0f, 0.058f, Pal.SignWhite, 0.03f);
            _ctx.Text("$", mb + new Vector3(0f, 1.38f, 0.12f), 0f, 0.07f, Pal.SignYellow, 0.03f);
            _ctx.Anchor("mission.board", AnchorKind.MissionBoard, mb + new Vector3(0f, 0f, 1.6f), 180f, 2.5f);
        }

        // ------------------------------------------------------------------ banca

        void Bank(Vector3 c)
        {
            float bx = c.x, bz = c.z;
            const float plinth = 0.45f;
            // Basamento e scalinata (gradini da 15 cm).
            _ctx.BoxMinMax(new Vector3(bx - 10f, Sw, bz - 10f), new Vector3(bx + 10f, plinth, bz + 9f), Pal.StoneDark);
            _ctx.BoxMinMax(new Vector3(bx - 8f, Sw, bz - 11.4f), new Vector3(bx + 8f, 0.3f, bz - 10f), Pal.StoneDark);
            _ctx.BoxMinMax(new Vector3(bx - 8f, 0.3f, bz - 10.7f), new Vector3(bx + 8f, plinth, bz - 10f), Pal.StoneDark);

            // Salone (cavo): muri di pietra, porta grande verso la piazza.
            var min = new Vector3(bx - 9f, 0f, bz - 7f);
            var max = new Vector3(bx + 9f, 0f, bz + 9f);
            const float wallH = 7.5f;
            var openings = new[]
            {
                Opening.Door(Side.South, 0f, 3.2f, 3.8f),
                Opening.Window(Side.West, -2f, 6f, 2.6f, 2.2f, Pal.GlassBlue),
                Opening.Window(Side.East, -2f, 6f, 2.6f, 2.2f, Pal.GlassBlue),
            };
            float floor = _ctx.Room(min, max, plinth, wallH, 0.4f, Pal.Stone, Pal.Marble, Pal.RoofLight, openings);
            float roofTop = plinth + wallH + 0.35f;
            _ctx.WrapBand(min, max, plinth + 6.2f, plinth + 6.7f, 0.15f, Pal.StoneDark);

            // Portico: colonne, trabeazione con la scritta BANCA.
            float[] cols = { -8f, -5.2f, -2.4f, 2.4f, 5.2f, 8f };
            foreach (float x in cols)
            {
                _ctx.Prism(new Vector3(bx + x, plinth, bz - 9f), 0.45f, 7.6f, 10, Pal.Marble);
                _ctx.Box(new Vector3(bx + x, plinth + 0.15f, bz - 9f), new Vector3(1.2f, 0.3f, 1.2f), Pal.StoneDark, 0f, false);
            }
            _ctx.BoxMinMax(new Vector3(bx - 9.6f, plinth + 7.6f, bz - 10f), new Vector3(bx + 9.6f, plinth + 9.1f, bz - 7f), Pal.Stone, false, BoxFaces.All);
            _ctx.BoxMinMax(new Vector3(bx - 10f, plinth + 9.1f, bz - 10.3f), new Vector3(bx + 10f, plinth + 9.5f, bz - 6.9f), Pal.StoneDark, false, BoxFaces.All);
            // Frontone a gradini.
            for (int i = 0; i < 4; i++)
            {
                float hw = 8.5f - i * 2.1f;
                _ctx.BoxMinMax(new Vector3(bx - hw, plinth + 9.5f + i * 0.55f, bz - 10.1f), new Vector3(bx + hw, plinth + 10.05f + i * 0.55f, bz - 7.2f), Pal.Stone, false);
            }
            _ctx.Text("BANK", new Vector3(bx, plinth + 8.35f, bz - 10f), 180f, 0.16f, Pal.BankGold, 0.08f);
            // Uffici sopra il salone (pieni).
            var upper = BuildingSpec.Make(17f, 12f, 12.4f, Pal.Stone, Pal.GlassBlue);
            upper.Style = FacadeStyle.Pilasters;
            upper.ShopFront = false;
            upper.RoofUnits = 2;
            _ctx.Building(new Vector3(bx, roofTop, bz + 2.5f), upper);

            // Interno: tappeto, bancone degli sportelli con varco, separé, caveau.
            _ctx.BoxMinMax(new Vector3(bx - 1.2f, floor, bz - 6.6f), new Vector3(bx + 1.2f, floor + 0.02f, bz + 0.5f), Pal.Carpet, false, BoxFaces.PosY | BoxFaces.Sides);
            foreach (float s in new[] { -1f, 1f })
            {
                float x0 = s < 0 ? bx - 8.6f : bx + 1.4f, x1 = s < 0 ? bx - 1.4f : bx + 8.6f;
                _ctx.BoxMinMax(new Vector3(x0, floor, bz + 1f), new Vector3(x1, floor + 1.1f, bz + 1.8f), Pal.WoodDark);
                _ctx.BoxMinMax(new Vector3(x0, floor + 1.1f, bz + 0.95f), new Vector3(x1, floor + 1.18f, bz + 1.85f), Pal.Marble, false, BoxFaces.All);
                _ctx.BoxMinMax(new Vector3(x0, floor + 1.18f, bz + 1.35f), new Vector3(x1, floor + 2.3f, bz + 1.42f), Pal.GlassClear, false, BoxFaces.All);
                for (int k = 0; k < 3; k++)
                {
                    float x = Mathf.Lerp(x0 + 1.2f, x1 - 1.2f, k / 2f);
                    _ctx.Box(new Vector3(x, floor + 1.4f, bz + 1.6f), new Vector3(0.5f, 0.35f, 0.08f), Pal.ScreenBlue, 0f, false, BoxFaces.All);
                }
            }
            

            // Caveau: muro d'acciaio con la porta tonda aperta, lingotti e cassette.
            const float vz0 = 3.4f, vz1 = 4.2f;
            _ctx.BoxMinMax(new Vector3(bx - 8.6f, floor, bz + vz0), new Vector3(bx - 1.3f, plinth + wallH, bz + vz1), Pal.VaultSteel, true, BoxFaces.Sides);
            _ctx.BoxMinMax(new Vector3(bx + 1.3f, floor, bz + vz0), new Vector3(bx + 8.6f, plinth + wallH, bz + vz1), Pal.VaultSteel, true, BoxFaces.Sides);
            _ctx.BoxMinMax(new Vector3(bx - 1.3f, floor + 2.6f, bz + vz0), new Vector3(bx + 1.3f, plinth + wallH, bz + vz1), Pal.VaultSteel, true, BoxFaces.Sides | BoxFaces.NegY);
            // Porta del caveau aperta (girata di 100 gradi, appoggiata al muro).
            var door = new Vector3(bx - 2.6f, floor + 1.3f, bz + vz0 - 0.55f);
            _ctx.Box(door, new Vector3(2.6f, 2.6f, 0.5f), Pal.VaultSteel, 10f, true, BoxFaces.All);
            _ctx.Box(door + new Vector3(0f, 0f, -0.27f), new Vector3(1.2f, 1.2f, 0.06f), Pal.Chrome, 10f, false, BoxFaces.All);
            _ctx.Text("VAULT", new Vector3(bx, floor + 3.2f, bz + vz0), 180f, 0.08f, Pal.BeaconRed, 0.04f);
            _ctx.Text("TELLERS", new Vector3(bx, floor + 5f, bz + vz0), 180f, 0.09f, Pal.BankGold, 0.04f);
            for (int k = 0; k < 4; k++)
            {
                float x = bx - 7.6f + k * 0.9f;
                _ctx.Box(new Vector3(x, floor + 0.4f, bz + 7.6f), new Vector3(0.7f, 0.8f, 0.7f), Pal.MetalDark);
                _ctx.Box(new Vector3(x, floor + 0.92f, bz + 7.6f), new Vector3(0.5f, 0.24f, 0.3f), Pal.BankGold, 0f, false);
            }
            _ctx.BoxMinMax(new Vector3(bx + 3f, floor, bz + 8.2f), new Vector3(bx + 8.6f, floor + 3f, bz + 8.6f), Pal.MetalLight, true, BoxFaces.Sides | BoxFaces.PosY);
            for (int r = 0; r < 4; r++)
            for (int k = 0; k < 6; k++)
                _ctx.Box(new Vector3(bx + 3.5f + k * 0.9f, floor + 0.5f + r * 0.7f, bz + 8.18f), new Vector3(0.7f, 0.5f, 0.04f), Pal.Metal, 0f, false, BoxFaces.All & ~BoxFaces.PosZ);

            _ctx.Light(new Vector3(bx, floor + 5.5f, bz - 3f), new Color(1f, 0.93f, 0.8f), 14f, 2.2f);
            _ctx.Light(new Vector3(bx, floor + 2.8f, bz + 6.5f), new Color(0.85f, 0.9f, 1f), 8f, 1.6f);

            _ctx.Anchor("bank.lobby", AnchorKind.Bank, new Vector3(bx, floor, bz - 2.5f), 0f, 3f);
            _ctx.Anchor("bank.vault", AnchorKind.Bank, new Vector3(bx, floor, bz + 6.2f), 0f, 2.5f, "vault");
        }

        // ------------------------------------------------------------------ torre (punto di riferimento)

        void LandmarkTower(Vector3 c)
        {
            var b = c + Vector3.up * Sw;
            var podium = BuildingSpec.Make(21f, 21f, 10.2f, Pal.Facade3, Pal.GlassDark);
            podium.Style = FacadeStyle.Pilasters;
            podium.RoofUnits = 0;
            float t1 = _ctx.Building(b, podium);

            var mid = BuildingSpec.Make(16f, 16f, 60f, Pal.Facade11, Pal.GlassBlue);
            mid.Style = FacadeStyle.GlassTower;
            mid.ShopFront = false;
            mid.RoofUnits = 2;
            float t2 = _ctx.Building(new Vector3(b.x, t1, b.z), mid);

            var top = BuildingSpec.Make(12f, 12f, 24f, Pal.Facade11, Pal.GlassTeal);
            top.Style = FacadeStyle.GlassTower;
            top.ShopFront = false;
            top.RoofUnits = 0;
            float t3 = _ctx.Building(new Vector3(b.x, t2, b.z), top);

            // Corona luminosa, scritta e guglia con luce rossa.
            _ctx.Box(new Vector3(b.x, t3 - 2.2f, b.z), new Vector3(12.3f, 0.7f, 12.3f), Pal.SignBlue, 0f, false, BoxFaces.Sides);
            _ctx.Box(new Vector3(b.x, t3 + 3f, b.z), new Vector3(7f, 6f, 7f), Pal.Facade11, 0f, false);
            _ctx.Prism(new Vector3(b.x, t3 + 6f, b.z), 0.5f, 22f, 6, Pal.MetalLight, false);
            _ctx.Box(new Vector3(b.x, t3 + 28.3f, b.z), new Vector3(0.8f, 0.8f, 0.8f), Pal.BeaconRed, 0f, false, BoxFaces.All);
            _ctx.Text("UNDERWORLD", new Vector3(b.x, t2 - 6f, b.z - 8.12f), 180f, 0.22f, Pal.SignWhite, 0.1f);
            _ctx.Text("UNDERWORLD", new Vector3(b.x, t2 - 6f, b.z + 8.12f), 0f, 0.22f, Pal.SignWhite, 0.1f);
        }

        void OctagonTower(Vector3 c)
        {
            var b = c + Vector3.up * Sw;
            var podium = BuildingSpec.Make(21f, 21f, 8.6f, Pal.Facade9, Pal.GlassDark);
            podium.RoofUnits = 0;
            float t1 = _ctx.Building(b, podium);
            const float r = 9.5f, h = 66f;
            var tb = new Vector3(b.x, t1, b.z);
            _ctx.Prism(tb, r, h, 8, Pal.GlassTeal, true, 22.5f);
            for (float y = 3.5f; y < h - 1f; y += 3.5f)
                _ctx.Prism(tb + Vector3.up * y, r + 0.12f, 0.35f, 8, Pal.Facade9, false, 22.5f, true, true);
            _ctx.Prism(tb + Vector3.up * h, r - 2.5f, 7f, 8, Pal.Facade9, false, 22.5f);
            _ctx.Prism(tb + Vector3.up * (h + 5.6f), r - 2.38f, 0.6f, 8, Pal.SignGreen, false, 22.5f, false, false);
            _ctx.Prism(tb + Vector3.up * (h + 7f), 0.3f, 12f, 6, Pal.MetalLight, false);
            _ctx.Box(tb + Vector3.up * (h + 19.2f), new Vector3(0.6f, 0.6f, 0.6f), Pal.BeaconRed, 0f, false, BoxFaces.All);
        }

        // ------------------------------------------------------------------ polizia

        void PoliceStation(Vector3 c)
        {
            float x0 = c.x - Inner, xf = c.x + 4f; // facciata a est (verso la strada x = -60)
            var spec = BuildingSpec.Make(xf - x0 - 0.5f, Inner * 2f - 1f, 10.8f, Pal.PoliceWhite, Pal.GlassDark);
            spec.ShopFront = false;
            spec.RoofUnits = 2;
            spec.Antenna = true;
            var bc = new Vector3((x0 + xf) * 0.5f, Sw, c.z);
            _ctx.Building(bc, spec);
            float face = bc.x + spec.Width * 0.5f;
            // Fascia blu e pensilina dell'ingresso con la scritta POLIZIA.
            _ctx.Box(new Vector3(bc.x, Sw + 3.5f, c.z), new Vector3(spec.Width + 0.2f, 0.7f, spec.Depth + 0.2f), Pal.PoliceBlue, 0f, false, BoxFaces.All);
            _ctx.BoxMinMax(new Vector3(face, Sw + 3.0f, c.z - 3.2f), new Vector3(face + 2.6f, Sw + 3.4f, c.z + 3.2f), Pal.PoliceBlue, false, BoxFaces.All);
            _ctx.Text("POLICE", new Vector3(face + 2.6f, Sw + 3.2f, c.z), 90f, 0.045f, Pal.SignWhite, 0.03f);
            foreach (float s in new[] { -1f, 1f })
            {
                _ctx.Box(new Vector3(face + 2.3f, Sw + 3.55f, c.z + s * 2.8f), new Vector3(0.3f, 0.3f, 0.3f), s < 0 ? Pal.BeaconRed : Pal.SignBlue, 0f, false);
                _ctx.Box(new Vector3(face + 2.4f, Sw + 1.5f, c.z + s * 3f), new Vector3(0.2f, 3f, 0.2f), Pal.MetalDark, 0f, true);
            }
            _ctx.Sign("POLICE", new Vector3(face + 0.1f, Sw + 8.2f, c.z), 90f, 0.2f, Pal.PoliceBlue, Pal.SignWhite);
            // Porta (incassata, scura) e gradino.
            _ctx.Box(new Vector3(face + 0.05f, Sw + 1.3f, c.z), new Vector3(0.12f, 2.6f, 2.4f), Pal.GlassDark, 0f, false, BoxFaces.All);
            // Asta della bandiera.
            _ctx.Prism(new Vector3(face + 5.5f, Sw, c.z + 7f), 0.07f, 8f, 6, Pal.MetalLight, false);
            _ctx.Box(new Vector3(face + 5.5f, Sw + 7.2f, c.z + 7.7f), new Vector3(0.04f, 0.9f, 1.4f), Pal.PoliceBlue, 0f, false, BoxFaces.All);

            // Piazzale con quattro posti per le auto della polizia (vuoti: ci appaiono le pattuglie).
            float[] bays = { -9f, -5.9f, 5.9f, 9f };
            for (int i = 0; i < bays.Length; i++)
            {
                var p = new Vector3(face + 4f, Sw, c.z + bays[i]);
                BayLines(p, 5f, 2.9f, true);
                _ctx.Anchor("police.spawn." + i, AnchorKind.PoliceStation, p, 90f, 2.5f);
            }
            _ctx.Anchor("police.station", AnchorKind.PoliceStation, new Vector3(face + 1.6f, Sw, c.z + 1.6f), 90f, 2.5f);
            _ctx.Anchor("police.release", AnchorKind.PoliceStation, new Vector3(face + 4.2f, Sw, c.z), 90f, 2f);
        }

        /// <summary>Linee di un posto auto (a U, aperto verso la corsia). <paramref name="alongX"/>: auto lungo x.</summary>
        void BayLines(Vector3 center, float length, float width, bool alongX)
        {
            float y = center.y + 0.01f;
            if (alongX)
            {
                foreach (float s in new[] { -1f, 1f })
                    _ctx.Box(new Vector3(center.x, y, center.z + s * width * 0.5f), new Vector3(length, 0.02f, 0.12f), Pal.Marking, 0f, false, BoxFaces.PosY);
            }
            else
            {
                foreach (float s in new[] { -1f, 1f })
                    _ctx.Box(new Vector3(center.x + s * width * 0.5f, y, center.z), new Vector3(0.12f, 0.02f, length), Pal.Marking, 0f, false, BoxFaces.PosY);
            }
        }

        // ------------------------------------------------------------------ concessionaria

        void Dealership(Vector3 c)
        {
            var min = new Vector3(c.x - Inner, 0f, c.z - Inner);
            var max = new Vector3(c.x + 5f, 0f, c.z + Inner);
            const float h = 7f;
            var openings = new[]
            {
                // Facciata di vetro verso la piazza (ovest), con un portone largo per far uscire le auto.
                new Opening(Side.West, 0f, 5f, 4.2f),
                Opening.Window(Side.West, -6.75f, 7.5f, 4.8f, 0.4f),
                Opening.Window(Side.West, 6.75f, 7.5f, 4.8f, 0.4f),
                Opening.Window(Side.North, -2f, 10f, 3f, 1f),
                Opening.Window(Side.South, -2f, 10f, 3f, 1f),
            };
            float floor = _ctx.Room(min, max, Sw, h, 0.3f, Pal.White, Pal.Marble, Pal.RoofLight, openings);
            // La fascia finisce 2 cm sopra il tetto (tetto alto 0,35): niente facce complanari (z-fighting).
            _ctx.WrapBand(min, max, Sw + h - 0.6f, Sw + h + 0.37f, 0.12f, Pal.SignBlue);
            _ctx.Sign("DEALERSHIP", new Vector3(min.x - 0.12f, Sw + 5.75f, c.z), 270f, 0.11f, Pal.Black, Pal.SignWhite, 0.2f);

            // Pedane per le auto in mostra (le mette il modulo Veicoli sui punti display.0..3).
            var pods = new[] { new Vector2(-5.8f, -6.2f), new Vector2(-5.8f, 6.2f), new Vector2(0.6f, -6.2f), new Vector2(0.6f, 6.2f) };
            for (int i = 0; i < pods.Length; i++)
            {
                var p = new Vector3(c.x + pods[i].x, floor, c.z + pods[i].y);
                _ctx.Prism(p, 2.8f, 0.15f, 12, Pal.MetalDark, true);
                _ctx.Prism(p + Vector3.up * 0.15f, 2.5f, 0.02f, 12, Pal.Chrome, false);
                _ctx.Anchor("dealership.display." + i, AnchorKind.Dealership, p + Vector3.up * 0.15f, 270f, 2.8f);
            }
            // Bancone del venditore in fondo.
            var desk = new Vector3(max.x - 1.6f, floor, c.z);
            _ctx.Box(desk + Vector3.up * 0.55f, new Vector3(0.9f, 1.1f, 3.2f), Pal.White);
            _ctx.Box(desk + new Vector3(0f, 1.13f, 0f), new Vector3(1.0f, 0.06f, 3.3f), Pal.Black, 0f, false, BoxFaces.All);
            _ctx.Box(desk + new Vector3(0.1f, 1.45f, 0.6f), new Vector3(0.06f, 0.5f, 0.8f), Pal.ScreenBlue, 0f, false, BoxFaces.All);
            _ctx.Text("SALES", new Vector3(max.x - 0.3f, floor + 3.6f, c.z), 270f, 0.08f, Pal.SignBlue, 0.04f);
            _ctx.Anchor("dealership.main", AnchorKind.Dealership, desk + new Vector3(-2.2f, 0f, 0f), 90f, 2.5f);
            _ctx.Anchor("dealership.exit", AnchorKind.Dealership, new Vector3(c.x - 17f, 0f, c.z), 180f, 3f);

            _ctx.Light(new Vector3(c.x - 2.6f, floor + 5.6f, c.z - 4f), new Color(1f, 0.97f, 0.92f), 13f, 2f);
            _ctx.Light(new Vector3(c.x - 2.6f, floor + 5.6f, c.z + 4f), new Color(1f, 0.97f, 0.92f), 13f, 2f);

            // Officina dietro lo showroom.
            var shop = BuildingSpec.Make(Inner - 5.6f, Inner * 2f - 1f, 5.6f, Pal.Facade2, Pal.GlassDark);
            shop.ShopFront = false;
            shop.RoofUnits = 1;
            _ctx.Building(new Vector3(c.x + 5.6f + (Inner - 5.6f) * 0.5f, Sw, c.z), shop);
        }

        // ------------------------------------------------------------------ negozi

        void GeneralStore(Vector3 c)
        {
            var min = new Vector3(c.x - 10f, 0f, c.z - 2f);
            var max = new Vector3(c.x + 6f, 0f, c.z + 10f);
            float mid = (min.x + max.x) * 0.5f; // -42
            var openings = new[]
            {
                Opening.Door(Side.North, -4f, 2.2f, 2.8f),
                Opening.Window(Side.North, 3f, 8f, 2.4f, 0.6f),
            };
            float floor = _ctx.Room(min, max, Sw, 4.5f, 0.3f, Pal.Facade8, Pal.FloorTile, Pal.Roof, openings);
            // Tenda e insegna.
            _ctx.Box(new Vector3(mid, Sw + 3.35f, max.z + 0.75f), new Vector3(15.6f, 0.08f, 1.6f), Pal.AwningRed, Quaternion.Euler(18f, 0f, 0f), false, BoxFaces.All);
            _ctx.Sign("MARKET", new Vector3(mid, Sw + 4.1f, max.z + 0.12f), 0f, 0.1f, Pal.AwningRed, Pal.SignWhite, 0.2f);

            // Bancone con la cassa, scaffale dietro (il punto della rapina è dietro il bancone).
            float cz = max.z - 0.3f - 3.6f; // fronte del bancone
            _ctx.BoxMinMax(new Vector3(mid + 2f, floor, cz - 0.8f), new Vector3(mid + 6.4f, floor + 1.05f, cz), Pal.Wood);
            _ctx.BoxMinMax(new Vector3(mid + 1.95f, floor + 1.05f, cz - 0.85f), new Vector3(mid + 6.45f, floor + 1.1f, cz + 0.05f), Pal.White, false, BoxFaces.All);
            _ctx.Box(new Vector3(mid + 4.6f, floor + 1.3f, cz - 0.4f), new Vector3(0.5f, 0.35f, 0.4f), Pal.MetalDark, 0f, false);
            _ctx.Box(new Vector3(mid + 4.6f, floor + 1.4f, cz - 0.18f), new Vector3(0.3f, 0.12f, 0.04f), Pal.SignGreen, 0f, false, BoxFaces.All);
            float back = min.z + 0.3f;
            _ctx.BoxMinMax(new Vector3(mid + 1f, floor, back), new Vector3(mid + 7.6f, floor + 2.2f, back + 0.5f), Pal.WoodDark, true, BoxFaces.Sides | BoxFaces.PosY);
            Products(new Vector3(mid + 1.2f, floor, back + 0.52f), 6.2f, 3, 0f);

            // Scaffali in due file e frigoriferi sul muro ovest.
            for (int row = 0; row < 2; row++)
            {
                float z = min.z + 4.2f + row * 3.4f;
                _ctx.BoxMinMax(new Vector3(min.x + 2.2f, floor, z - 0.4f), new Vector3(mid + 0.2f, floor + 1.6f, z + 0.4f), Pal.MetalLight);
                Products(new Vector3(min.x + 2.4f, floor, z + 0.42f), mid - min.x - 2.4f, 2, 0f);
                Products(new Vector3(min.x + 2.4f, floor, z - 0.42f), mid - min.x - 2.4f, 2, 180f);
            }
            _ctx.BoxMinMax(new Vector3(min.x + 0.3f, floor, min.z + 1.5f), new Vector3(min.x + 1.1f, floor + 2.3f, max.z - 2f), Pal.MetalLight, true, BoxFaces.Sides | BoxFaces.PosY);
            _ctx.BoxMinMax(new Vector3(min.x + 1.1f, floor + 0.2f, min.z + 1.7f), new Vector3(min.x + 1.14f, floor + 2.1f, max.z - 2.2f), Pal.ScreenBlue, false, BoxFaces.All);
            _ctx.Light(new Vector3(mid, floor + 3.6f, (min.z + max.z) * 0.5f), new Color(1f, 0.97f, 0.9f), 11f, 1.8f);

            _ctx.Anchor("shop.general", AnchorKind.Shop, new Vector3(mid + 4.2f, floor, cz + 1.3f), 180f, 2.5f);
            _ctx.Anchor("robbery.store0", AnchorKind.Robbery, new Vector3(mid + 4.2f, floor, cz - 2.1f), 0f, 1.8f, "store0");

            // Resto dell'isolato: edifici pieni a sud e a est.
            var s1 = BuildingSpec.Make(Inner * 2f - 1f, 7.2f, 15.2f, Pal.Facade5, Pal.Glass);
            s1.ShopFront = false;
            _ctx.Building(new Vector3(c.x, Sw, c.z - Inner + 4.1f), s1);
            var s2 = BuildingSpec.Make(Inner - 6.6f, 12f, 12.4f, Pal.Facade10, Pal.GlassDark);
            s2.ShopFront = true;
            _ctx.Building(new Vector3(max.x + 0.6f + (Inner - 6.6f) * 0.5f, Sw, (min.z + max.z) * 0.5f), s2);
        }

        /// <summary>Prodotti colorati su uno scaffale (scatoline in file). <paramref name="yaw"/> = lato del fronte.</summary>
        void Products(Vector3 start, float length, int shelves, float yaw)
        {
            Pal[] colors = { Pal.Red, Pal.Yellow, Pal.Blue, Pal.Green, Pal.Orange, Pal.White, Pal.Teal };
            float dz = yaw == 0f ? 0.06f : -0.06f;
            for (int s = 0; s < shelves; s++)
            {
                float y = start.y + 0.35f + s * 0.6f;
                for (float x = 0.2f; x < length - 0.2f; x += 0.45f)
                    _ctx.Box(new Vector3(start.x + x, y, start.z + dz), new Vector3(0.36f, 0.3f, 0.12f), colors[_ctx.Range(0, colors.Length)], 0f, false, BoxFaces.All & ~BoxFaces.NegY);
            }
        }

        void ClothingStore(Vector3 c)
        {
            var min = new Vector3(c.x - 8f, 0f, c.z - 2f);
            var max = new Vector3(c.x + 8f, 0f, c.z + 10f);
            var openings = new[]
            {
                Opening.Door(Side.North, 0f, 2.6f, 3f),
                Opening.Window(Side.North, -5f, 5f, 3f, 0.4f),
                Opening.Window(Side.North, 5f, 5f, 3f, 0.4f),
            };
            float floor = _ctx.Room(min, max, Sw, 5f, 0.3f, Pal.Facade3, Pal.FloorDark, Pal.RoofDark, openings);
            _ctx.Box(new Vector3(c.x, Sw + 3.75f, max.z + 0.7f), new Vector3(15.6f, 0.08f, 1.5f), Pal.Black, Quaternion.Euler(15f, 0f, 0f), false, BoxFaces.All);
            _ctx.Sign("FASHION", new Vector3(c.x, Sw + 4.45f, max.z + 0.12f), 0f, 0.11f, Pal.Black, Pal.SignYellow, 0.2f);

            // Manichini in vetrina, stender con vestiti colorati, specchi, cassa.
            foreach (float sx in new[] { -5f, 5f })
            {
                var m = new Vector3(c.x + sx, floor, max.z - 1.2f);
                _ctx.Box(m + Vector3.up * 0.1f, new Vector3(0.7f, 0.2f, 0.7f), Pal.Black);
                _ctx.Box(m + Vector3.up * 1.05f, new Vector3(0.45f, 1.7f, 0.25f), sx < 0 ? Pal.AwningRed : Pal.AwningBlue, 0f, false);
                _ctx.Box(m + Vector3.up * 2.05f, new Vector3(0.26f, 0.3f, 0.26f), Pal.Plaster, 0f, false, BoxFaces.All);
            }
            Pal[] cloth = { Pal.Red, Pal.Blue, Pal.Black, Pal.White, Pal.Teal, Pal.Yellow, Pal.AwningGreen, Pal.Carpet };
            for (int r = 0; r < 3; r++)
            {
                float rx = c.x - 4.5f + r * 4.5f, rz = c.z + 3.6f;
                foreach (float s in new[] { -1.5f, 1.5f })
                    _ctx.Box(new Vector3(rx + s, floor + 0.8f, rz), new Vector3(0.06f, 1.6f, 0.06f), Pal.Chrome, 0f, true);
                _ctx.Box(new Vector3(rx, floor + 1.6f, rz), new Vector3(3.1f, 0.05f, 0.05f), Pal.Chrome, 0f, false, BoxFaces.All);
                for (int k = 0; k < 7; k++)
                    _ctx.Box(new Vector3(rx - 1.3f + k * 0.43f, floor + 1.05f, rz), new Vector3(0.08f, 1.0f, 0.55f), cloth[_ctx.Range(0, cloth.Length)], 0f, false, BoxFaces.All);
            }
            _ctx.BoxMinMax(new Vector3(max.x - 0.34f, floor + 0.3f, min.z + 1f), new Vector3(max.x - 0.3f, floor + 2.6f, min.z + 3.4f), Pal.Chrome, false, BoxFaces.All);
            _ctx.BoxMinMax(new Vector3(min.x + 0.3f, floor + 0.3f, min.z + 1f), new Vector3(min.x + 0.34f, floor + 2.6f, min.z + 3.4f), Pal.Chrome, false, BoxFaces.All);
            _ctx.BoxMinMax(new Vector3(c.x - 2f, floor, min.z + 1.1f), new Vector3(c.x + 2f, floor + 1f, min.z + 1.9f), Pal.White);
            _ctx.Light(new Vector3(c.x, floor + 3.8f, (min.z + max.z) * 0.5f), new Color(1f, 0.95f, 0.88f), 11f, 1.8f);
            _ctx.Anchor("clothing.main", AnchorKind.ClothingStore, new Vector3(c.x, floor, min.z + 3.4f), 180f, 2.5f);

            var s1 = BuildingSpec.Make(Inner * 2f - 1f, 7.2f, 18.4f, Pal.Facade6, Pal.GlassDark);
            s1.Style = FacadeStyle.Pilasters;
            s1.ShopFront = false;
            _ctx.Building(new Vector3(c.x, Sw, c.z - Inner + 4.1f), s1);
        }

        // ------------------------------------------------------------------ parcheggio pubblico

        void PublicGarage(Vector3 c)
        {
            float y = Sw + 0.02f;
            // Fondo del parcheggio e muretto con l'ingresso a sud.
            _ctx.Box(new Vector3(c.x, Sw + 0.01f, c.z), new Vector3(Inner * 2f, 0.02f, Inner * 2f), Pal.AsphaltPatch, 0f, false, BoxFaces.PosY);
            float x0 = c.x - Inner, x1 = c.x + Inner, z0 = c.z - Inner, z1 = c.z + Inner;
            _ctx.BoxMinMax(new Vector3(x0, Sw, z1 - 0.3f), new Vector3(x1, Sw + 0.7f, z1), Pal.Concrete);
            _ctx.BoxMinMax(new Vector3(x0, Sw, z0), new Vector3(x0 + 0.3f, Sw + 0.7f, z1 - 0.3f), Pal.Concrete);
            _ctx.BoxMinMax(new Vector3(x1 - 0.3f, Sw, z0), new Vector3(x1, Sw + 0.7f, z1 - 0.3f), Pal.Concrete);
            _ctx.BoxMinMax(new Vector3(x0 + 0.3f, Sw, z0), new Vector3(c.x - 5f, Sw + 0.7f, z0 + 0.3f), Pal.Concrete);
            _ctx.BoxMinMax(new Vector3(c.x + 5f, Sw, z0), new Vector3(x1 - 0.3f, Sw + 0.7f, z0 + 0.3f), Pal.Concrete);

            // Posti auto: fila sud (lasciando libero l'ingresso) e fila nord. I quattro centrali a nord sono gli spawn.
            float rowS = z0 + 3.2f, rowN = z1 - 3.2f;
            float[] bx = { -9f, -6f, -3f, 0f, 3f, 6f, 9f };
            for (int i = 0; i < bx.Length; i++)
            {
                var pn = new Vector3(c.x + bx[i], y, rowN);
                BayLines(pn, 5f, 3f, false);
                bool spawn = i >= 2 && i <= 5;
                if (spawn) _ctx.Anchor("garage.spawn." + (i - 2), AnchorKind.Garage, new Vector3(pn.x, Sw, pn.z), 180f, 2.5f);
                else ParkedCar(pn, 180f);

                if (Mathf.Abs(bx[i]) < 5f) continue;
                var ps = new Vector3(c.x + bx[i], y, rowS);
                BayLines(ps, 5f, 3f, false);
                if (_ctx.Chance(0.75f)) ParkedCar(ps, 0f);
            }

            // Casotto con la cassa automatica e l'insegna.
            var kiosk = new Vector3(x0 + 2.2f, Sw, z0 + 7.6f);
            _ctx.Box(kiosk + Vector3.up * 1.3f, new Vector3(2.4f, 2.6f, 2.4f), Pal.Facade9);
            _ctx.Box(kiosk + Vector3.up * 1.6f, new Vector3(2.52f, 1.0f, 2.52f), Pal.GlassBlue, 0f, false, BoxFaces.Sides);
            _ctx.Box(kiosk + Vector3.up * 2.7f, new Vector3(2.8f, 0.2f, 2.8f), Pal.RoofDark, 0f, false, BoxFaces.All);
            var machine = kiosk + new Vector3(2.0f, 0f, 0f);
            _ctx.Box(machine + Vector3.up * 0.8f, new Vector3(0.5f, 1.6f, 0.6f), Pal.AwningYellow);
            _ctx.Box(machine + new Vector3(0.26f, 1.15f, 0f), new Vector3(0.04f, 0.4f, 0.4f), Pal.ScreenBlue, 0f, false, BoxFaces.All);
            _ctx.Anchor("garage.public", AnchorKind.Garage, machine + new Vector3(1.3f, 0f, 0f), 270f, 2.5f);
            var pole = new Vector3(c.x - 4.6f, Sw, z0 - 0.6f);
            _ctx.Box(pole + Vector3.up * 2.2f, new Vector3(0.2f, 4.4f, 0.2f), Pal.MetalDark);
            _ctx.Sign("PARKING", pole + new Vector3(0f, 4.6f, -0.2f), 180f, 0.09f, Pal.AwningBlue, Pal.SignWhite, 0.18f);
            _ctx.Text("P", pole + new Vector3(0f, 3.4f, -0.12f), 180f, 0.12f, Pal.SignBlue, 0.04f);
            foreach (var lp in new[] { new Vector3(x0 + 0.8f, Sw, c.z), new Vector3(x1 - 0.8f, Sw, c.z) })
                _ctx.Lamp(lp, lp.x < c.x ? 90f : 270f);
        }

        // ------------------------------------------------------------------ appartamenti

        void Apartments(Vector3 c)
        {
            var spec = BuildingSpec.Make(20f, 16f, 19.6f, Pal.Brick, Pal.GlassDark);
            spec.Style = FacadeStyle.Pilasters;
            spec.ShopFront = false;
            spec.RoofUnits = 2;
            spec.WaterTank = true;
            float bz = c.z - Inner + 1.5f + 8f;
            _ctx.Building(new Vector3(c.x, Sw, bz), spec);
            float face = bz - 8f; // facciata sud
            // Zoccolo di pietra al piano terra.
            _ctx.Box(new Vector3(c.x, Sw + 1.6f, bz), new Vector3(20.5f, 3.2f, 16.5f), Pal.StoneDark, 0f, false, BoxFaces.Sides | BoxFaces.PosY);
            // Quattro portoni con pensilina e numero; il punto speciale è davanti a ogni porta.
            float[] doors = { -7.5f, -2.5f, 2.5f, 7.5f };
            for (int i = 0; i < doors.Length; i++)
            {
                float x = c.x + doors[i];
                _ctx.Box(new Vector3(x, Sw + 1.25f, face - 0.3f), new Vector3(1.5f, 2.5f, 0.12f), Pal.WoodDark, 0f, false, BoxFaces.All);
                _ctx.Box(new Vector3(x, Sw + 1.25f, face - 0.37f), new Vector3(0.08f, 0.08f, 0.04f), Pal.BankGold, 0f, false, BoxFaces.All);
                _ctx.Box(new Vector3(x, Sw + 2.8f, face - 0.85f), new Vector3(2.2f, 0.14f, 1.4f), Pal.MetalDark, 0f, false, BoxFaces.All);
                _ctx.Text((i + 1).ToString(), new Vector3(x + 1.05f, Sw + 1.9f, face - 0.27f), 180f, 0.07f, Pal.SignYellow, 0.03f);
                _ctx.Box(new Vector3(x, Sw + 0.06f, face - 0.9f), new Vector3(2f, 0.12f, 1.1f), Pal.Concrete, 0f, false);
                _ctx.Anchor("property.apt" + i, AnchorKind.Property, new Vector3(x, Sw, face - 2.1f), 180f, 1.8f, "apt" + i);
            }
            // Cortile dietro: alberi e cestini.
            _ctx.Box(new Vector3(c.x, Sw + 0.02f, c.z + Inner - 1.4f), new Vector3(20f, 0.04f, 2.4f), Pal.Grass, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            for (int i = 0; i < 4; i++)
                _ctx.Model(_ctx.Pick(StreetTrees), new Vector3(c.x - 7.5f + i * 5f, Sw + 0.04f, c.z + Inner - 1.4f), _ctx.Range(0f, 360f), -1, true, 0.75f);
        }

        // ------------------------------------------------------------------ vicolo e covo

        void AlleyAndHideout(Vector3 c)
        {
            const float az0 = -2f, az1 = 2f; // vicolo largo 4 m (rispetto al centro dell'isolato), da ovest a est
            float zA0 = c.z + az0, zA1 = c.z + az1;
            // Fondo del vicolo (asfalto rovinato) e pozzanghere.
            _ctx.Box(new Vector3(c.x, Sw + 0.01f, c.z), new Vector3(Block, 0.02f, az1 - az0), Pal.AsphaltPatch, 0f, false, BoxFaces.PosY);
            _ctx.Box(new Vector3(c.x - 4f, Sw + 0.025f, c.z + 0.6f), new Vector3(1.6f, 0.01f, 0.9f), Pal.Water, 0f, false, BoxFaces.PosY);

            // Lato nord del vicolo: due edifici.
            float nz0 = zA1 + 0.6f, nz1 = c.z + Inner;
            var n1 = BuildingSpec.Make(10.2f, nz1 - nz0, 15.6f, Pal.Brick, Pal.GlassDark);
            n1.ShopFront = false;
            _ctx.Building(new Vector3(c.x - 5.6f, Sw, (nz0 + nz1) * 0.5f), n1);
            var n2 = BuildingSpec.Make(10.2f, nz1 - nz0, 12.4f, Pal.Facade7, Pal.Glass);
            _ctx.Building(new Vector3(c.x + 5.6f, Sw, (nz0 + nz1) * 0.5f), n2);
            // Scala antincendio sul muro del vicolo.
            for (int f = 1; f < 4; f++)
            {
                _ctx.Box(new Vector3(c.x - 5.6f, Sw + f * 3.2f + 0.6f, nz0 - 0.6f), new Vector3(3.2f, 0.1f, 1.2f), Pal.MetalDark, 0f, false, BoxFaces.All);
                _ctx.Box(new Vector3(c.x - 5.6f, Sw + f * 3.2f + 1.1f, nz0 - 1.17f), new Vector3(3.2f, 1f, 0.05f), Pal.MetalDark, 0f, false, BoxFaces.All);
            }

            // Lato sud: un edificio e il garage del covo con la serranda aperta sul vicolo.
            float sz0 = c.z - Inner, sz1 = zA0 - 0.6f;
            var s1 = BuildingSpec.Make(13.4f, sz1 - sz0, 9.4f, Pal.Facade2, Pal.GlassDark);
            s1.ShopFront = false;
            s1.WaterTank = true;
            _ctx.Building(new Vector3(c.x - Inner + 6.7f, Sw, (sz0 + sz1) * 0.5f), s1);

            var gmin = new Vector3(c.x + 3.0f, 0f, sz0);
            var gmax = new Vector3(c.x + Inner, 0f, sz1);
            float floor = _ctx.Room(gmin, gmax, Sw, 4.4f, 0.3f, Pal.ConcreteDark, Pal.Concrete, Pal.RoofDark,
                new[] { new Opening(Side.North, 0f, 4.2f, 3.2f) });
            float gx = (gmin.x + gmax.x) * 0.5f;
            // Serranda mezza alzata, strisce gialle e nere, lampada sopra.
            _ctx.Box(new Vector3(gx, Sw + 3.05f, gmax.z + 0.12f), new Vector3(4.4f, 0.4f, 0.12f), Pal.Metal, 0f, false, BoxFaces.All);
            for (int k = 0; k < 6; k++)
                _ctx.Box(new Vector3(gx - 1.9f + k * 0.76f, Sw + 0.03f, gmax.z + 0.4f), new Vector3(0.38f, 0.02f, 0.5f), k % 2 == 0 ? Pal.Yellow : Pal.Black, 0f, false, BoxFaces.PosY);
            _ctx.Box(new Vector3(gx, Sw + 3.75f, gmax.z + 0.2f), new Vector3(0.6f, 0.2f, 0.5f), Pal.LampGlow, 0f, false, BoxFaces.All);
            // Dentro: banco da lavoro, gomme, casse.
            _ctx.BoxMinMax(new Vector3(gmax.x - 1.2f, floor, gmin.z + 1f), new Vector3(gmax.x - 0.3f, floor + 0.95f, gmin.z + 4.5f), Pal.WoodDark);
            _ctx.Model("prop.tire", new Vector3(gmin.x + 1f, floor, gmin.z + 1.2f), 0f, -1, true);
            _ctx.Model("prop.tire", new Vector3(gmin.x + 1f, floor, gmin.z + 2.1f), 10f, -1, true);
            _ctx.Model("prop.crate_large", new Vector3(gmin.x + 1.6f, floor, gmin.z + 4.4f), 90f);
            _ctx.Light(new Vector3(gx, floor + 3.4f, (gmin.z + gmax.z) * 0.5f), new Color(1f, 0.85f, 0.6f), 8f, 1.6f);
            _ctx.Anchor("hideout.drop", AnchorKind.Hideout, new Vector3(gx, floor, gmax.z - 2.4f), 0f, 2.5f);

            // Arredo del vicolo: cassonetti, casse, gomme, coni (lato nord, lascia libero il passaggio).
            var dump = new Vector3(c.x + 2f, Sw, zA1 - 0.75f);
            _ctx.Box(dump + Vector3.up * 0.6f, new Vector3(2f, 1.2f, 1.1f), Pal.AwningGreen);
            _ctx.Box(dump + Vector3.up * 1.25f, new Vector3(2.1f, 0.1f, 1.2f), Pal.MetalDark, 0f, false, BoxFaces.All);
            _ctx.Model("prop.crate_small", new Vector3(c.x - 9f, Sw, zA1 - 0.5f), 12f);
            _ctx.Model("prop.crate_wide", new Vector3(c.x - 7.6f, Sw, zA1 - 0.6f), 90f);
            _ctx.Model("prop.tire", new Vector3(c.x + 9.5f, Sw, zA1 - 0.4f), 90f);
            _ctx.Model("prop.cone", new Vector3(c.x - 12.5f, Sw, c.z - 1.4f), 0f);
            _ctx.Model("prop.cone", new Vector3(c.x - 12.5f, Sw, c.z + 1.4f), 0f);
        }

        // ------------------------------------------------------------------ parco e cantiere

        void Park(Vector3 c)
        {
            float y = Sw;
            _ctx.Box(new Vector3(c.x, y + 0.02f, c.z), new Vector3(Inner * 2f, 0.04f, Inner * 2f), Pal.Grass, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            // Viali di ghiaia a croce.
            _ctx.Box(new Vector3(c.x, y + 0.05f, c.z), new Vector3(Inner * 2f, 0.02f, 3.4f), Pal.Gravel, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            _ctx.Box(new Vector3(c.x, y + 0.07f, c.z), new Vector3(3.4f, 0.02f, Inner * 2f), Pal.Gravel, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            // Obelisco al centro.
            _ctx.Box(new Vector3(c.x, y + 0.4f, c.z), new Vector3(2.4f, 0.8f, 2.4f), Pal.StoneDark);
            _ctx.Model("nature.statue_obelisk", new Vector3(c.x, y + 0.8f, c.z), 0f);
            // Alberi e cespugli nei quattro quadranti.
            for (int q = 0; q < 4; q++)
            {
                float sx = q % 2 == 0 ? -1f : 1f, sz = q < 2 ? -1f : 1f;
                for (int k = 0; k < 4; k++)
                {
                    var p = new Vector3(c.x + sx * _ctx.Range(3.5f, 9.5f), y + 0.04f, c.z + sz * _ctx.Range(3.5f, 9.5f));
                    _ctx.Model(_ctx.Pick(ParkTrees), p, _ctx.Range(0f, 360f), -1, true, _ctx.Range(0.8f, 1.2f));
                }
                _ctx.Model("nature.bush_large", new Vector3(c.x + sx * 2.8f, y + 0.04f, c.z + sz * 9.6f), _ctx.Range(0f, 360f));
                _ctx.Model("nature.bush_detailed", new Vector3(c.x + sx * 9.6f, y + 0.04f, c.z + sz * 2.8f), _ctx.Range(0f, 360f));
                _ctx.Model("nature.rock_small", new Vector3(c.x + sx * 10f, y + 0.04f, c.z + sz * 6.5f), _ctx.Range(0f, 360f));
                _ctx.Model(q % 2 == 0 ? "nature.flower_yellow" : "nature.flower_red", new Vector3(c.x + sx * 2.6f, y + 0.04f, c.z + sz * 3f), 0f);
                _ctx.Bench(new Vector3(c.x + sx * 6.5f, y + 0.04f, c.z + sz * 2.4f), sz < 0 ? 0f : 180f);
            }
        }

        void ConstructionSite(Vector3 c)
        {
            float y = Sw;
            _ctx.Box(new Vector3(c.x, y + 0.02f, c.z), new Vector3(Inner * 2f, 0.04f, Inner * 2f), Pal.Soil, 0f, false, BoxFaces.PosY | BoxFaces.Sides);
            // Recinzione (pannelli), cancello aperto a est.
            for (int side = 0; side < 4; side++)
            {
                for (int k = 0; k < 7; k++)
                {
                    float a = -Inner + 1.6f + k * 3.15f;
                    if (side == 3 && Mathf.Abs(a) < 3f) continue; // cancello
                    Vector3 p = side switch
                    {
                        0 => new Vector3(c.x + a, y, c.z - Inner),
                        1 => new Vector3(c.x + a, y, c.z + Inner),
                        2 => new Vector3(c.x - Inner, y, c.z + a),
                        _ => new Vector3(c.x + Inner, y, c.z + a),
                    };
                    float yaw = side < 2 ? 0f : 90f;
                    _ctx.Box(p + Vector3.up * 1.1f, new Vector3(3.05f, 2.2f, 0.08f), k % 2 == 0 ? Pal.AwningYellow : Pal.MetalLight, yaw, true, BoxFaces.All);
                }
            }
            // Scheletro dell'edificio: pilastri e solette.
            for (int f = 0; f < 4; f++)
            {
                float fy = y + f * 3.6f;
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    _ctx.Box(new Vector3(c.x - 7f + i * 5.5f, fy + 1.8f, c.z - 3f + j * 5.5f), new Vector3(0.5f, 3.6f, 0.5f), Pal.Concrete, 0f, f == 0);
                if (f > 0) _ctx.Box(new Vector3(c.x - 1.5f, fy + 0.15f, c.z + 2.5f), new Vector3(12f, 0.3f, 12f), Pal.Concrete, 0f, f == 1);
            }
            // Gru a torre (punto di riferimento a sud-ovest).
            var mast = new Vector3(c.x + 7f, y, c.z - 7f);
            _ctx.Box(mast + Vector3.up * 24f, new Vector3(1.6f, 48f, 1.6f), Pal.Yellow);
            for (float hy = 2f; hy < 48f; hy += 3f)
                _ctx.Box(mast + Vector3.up * hy, new Vector3(1.8f, 0.2f, 1.8f), Pal.Orange, 0f, false, BoxFaces.All);
            // Braccio (48..49,2 m) appoggiato sulla cima del traliccio, contrappeso, cabina e cavo.
            _ctx.Box(mast + new Vector3(-9f, 48.6f, 0f), new Vector3(34f, 1.2f, 1.2f), Pal.Yellow, 0f, false, BoxFaces.All);
            _ctx.Box(mast + new Vector3(6f, 47f, 0f), new Vector3(2.6f, 2.4f, 2f), Pal.ConcreteDark, 0f, false, BoxFaces.All);
            _ctx.Box(mast + new Vector3(0f, 50.2f, 0f), new Vector3(1.4f, 2f, 1.4f), Pal.Yellow, 0f, false, BoxFaces.NoBottom);
            _ctx.Box(mast + new Vector3(-1.35f, 46.6f, 0f), new Vector3(1.4f, 1.4f, 1.6f), Pal.GlassDark, 0f, false, BoxFaces.All);
            _ctx.Box(mast + new Vector3(-20f, 39f, 0f), new Vector3(0.05f, 18f, 0.05f), Pal.MetalDark, 0f, false, BoxFaces.Sides);
            _ctx.Box(mast + new Vector3(-20f, 29.8f, 0f), new Vector3(0.5f, 0.4f, 0.3f), Pal.MetalDark, 0f, false, BoxFaces.All);
            _ctx.Box(mast + new Vector3(0f, 51.4f, 0f), new Vector3(0.4f, 0.4f, 0.4f), Pal.BeaconRed, 0f, false);
            // Materiale e segnali.
            _ctx.Model("prop.crate_long", new Vector3(c.x - 8f, y + 0.04f, c.z - 8f), 0f);
            _ctx.Model("prop.crate_long", new Vector3(c.x - 8f, y + 0.84f, c.z - 8f), 4f, -1, false);
            _ctx.Model("prop.crate_large", new Vector3(c.x - 3f, y + 0.04f, c.z - 8.5f), 90f);
            _ctx.Model("prop.crate_small", new Vector3(c.x + 2f, y + 0.04f, c.z - 9f), 30f);
            _ctx.Model("prop.warning_sign", new Vector3(c.x + Inner + 1.2f, Sw, c.z + 4f), 90f);
            _ctx.Model("prop.warning_post", new Vector3(c.x + Inner + 1.2f, Sw, c.z - 3.6f), 90f);
            for (int k = 0; k < 5; k++)
                _ctx.Model("prop.cone", new Vector3(c.x + Inner + 1.6f, Sw, c.z - 2.4f + k * 1.2f), _ctx.Range(0f, 360f));
        }

        // ------------------------------------------------------------------ arredo delle strade

        void StreetFurniture()
        {
            for (int gx = 0; gx < Grid; gx++)
            for (int gz = 0; gz < Grid; gz++)
            {
                if (gx == 3 && gz == 3) continue; // la piazza ha il suo arredo
                var c = new Vector3(BlockCenter(gx), Sw, BlockCenter(gz));
                // Lampioni agli angoli, con il braccio verso l'incrocio.
                for (int k = 0; k < 4; k++)
                {
                    float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? -1f : 1f;
                    var p = c + new Vector3(sx * (HalfBlock - 0.7f), 0f, sz * (HalfBlock - 0.7f));
                    _ctx.Lamp(p, Mathf.Atan2(sx, sz) * Mathf.Rad2Deg);
                }
                // Alberi lungo i viali vicino al centro (non davanti agli ingressi).
                bool boulevard = Mathf.Abs(gx - 3) <= 2 && Mathf.Abs(gz - 3) <= 2;
                if (boulevard)
                {
                    for (int side = 0; side < 4; side++)
                    for (int k = -1; k <= 1; k++)
                    {
                        if (k == 0) continue;
                        Vector3 p = side switch
                        {
                            0 => c + new Vector3(k * 7f, 0f, -(HalfBlock - 1.4f)),
                            1 => c + new Vector3(k * 7f, 0f, HalfBlock - 1.4f),
                            2 => c + new Vector3(-(HalfBlock - 1.4f), 0f, k * 7f),
                            _ => c + new Vector3(HalfBlock - 1.4f, 0f, k * 7f),
                        };
                        if (!IsClear(p, 1f) || !BlockAllowsTrees(gx, gz, side)) continue;
                        _ctx.StreetTree(p, _ctx.Pick(StreetTrees), _ctx.Range(0.72f, 0.88f));
                    }
                }
                // Arredo piccolo: cestino, idrante, panchina (posizioni dal seme).
                var bin = c + new Vector3(HalfBlock - 1.3f, 0f, _ctx.Range(-4f, 4f));
                if (_ctx.Chance(0.6f) && IsClear(bin, 0.5f)) _ctx.Bin(bin);
                var hyd = c + new Vector3(_ctx.Range(-4f, 4f), 0f, -(HalfBlock - 0.8f));
                if (_ctx.Chance(0.5f) && IsClear(hyd, 0.5f)) _ctx.Hydrant(hyd);
            }

            // Semafori agli incroci intorno alla piazza.
            foreach (float ix in new[] { -20f, 20f })
            foreach (float iz in new[] { -20f, 20f })
            {
                for (int k = 0; k < 4; k++)
                {
                    float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? -1f : 1f;
                    var p = new Vector3(ix + sx * 6.6f, Sw, iz + sz * 6.6f);
                    _ctx.TrafficLight(p, k * 90f + 45f, (k + (ix > 0 ? 1 : 0)) % 3);
                }
            }

            BusStop(BusStopPos, 90f);
        }

        static readonly Vector3 BusStopPos = new Vector3(-26.8f, Sw, 8f);

        /// <summary>Pensilina del bus rivolta verso <paramref name="yaw"/> (la strada).</summary>
        void BusStop(Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            _ctx.Box(p + rot * new Vector3(0f, 2.6f, 0f), new Vector3(4.2f, 0.12f, 1.8f), Pal.MetalDark, rot, false, BoxFaces.All);
            _ctx.Box(p + rot * new Vector3(0f, 1.35f, -0.85f), new Vector3(4.2f, 2.3f, 0.05f), Pal.GlassClear, rot, true, BoxFaces.All);
            foreach (float sx in new[] { -2f, 2f })
                _ctx.Box(p + rot * new Vector3(sx, 1.27f, -0.78f), new Vector3(0.1f, 2.26f, 0.1f), Pal.MetalDark, rot, false);
            _ctx.Bench(p + rot * new Vector3(0f, 0f, -0.35f), yaw);
            _ctx.Sign("BUS", p + rot * new Vector3(2.45f, 2.3f, 0.6f), yaw, 0.08f, Pal.AwningYellow, Pal.Black, 0.12f);
        }

        /// <summary>Niente alberi davanti alle facciate importanti (banca, concessionaria, negozi, polizia).</summary>
        static bool BlockAllowsTrees(int gx, int gz, int side)
        {
            // side: 0 = sud, 1 = nord, 2 = ovest, 3 = est
            if (gx == 3 && gz == 4 && side == 0) return false; // banca verso la piazza
            if (gx == 4 && gz == 3 && side == 2) return false; // vetrina della concessionaria
            if (gx == 2 && gz == 2 && side == 1) return false; // negozio
            if (gx == 3 && gz == 2 && side == 1) return false; // abbigliamento
            if (gx == 1 && gz == 3 && side == 3) return false; // polizia
            if (gx == 4 && gz == 4 && side == 0) return false; // ingresso del parcheggio
            if (gx == 2 && gz == 4 && side == 0) return false; // porte degli appartamenti
            return true;
        }

        void FixedCar(string id, Vector3 p, float yaw)
        {
            _keepClear.Add(new Rect(p.x - 2f, p.z - 4f, 4f, 8f));
            _ctx.Model(id, p, yaw);
        }

        void ParkedCars()
        {
            // Due auto della polizia davanti alla stazione, un taxi vicino alla piazza, un furgone delle consegne.
            FixedCar("car.police", new Vector3(-64.6f, 0f, -8f), 180f);
            FixedCar("car.police", new Vector3(-64.6f, 0f, 8f), 180f);
            FixedCar("car.taxi", new Vector3(-24.6f, 0f, -6f), 180f);
            FixedCar("car.delivery", new Vector3(-55.4f, 0f, -33f), 0f);

            // Auto ferme lungo i marciapiedi (corsia di destra), mai negli incroci, sulle strisce o davanti agli ingressi.
            for (int i = 0; i <= Grid; i++)
            {
                float rc = RoadCenter(i);
                for (int j = 0; j < Grid; j++)
                {
                    float s0 = RoadCenter(j) + Road * 0.5f;
                    foreach (float off in new[] { 7.5f, 14f, 20.5f })
                    {
                        // Strada nord-sud (x = rc): lato est verso nord, lato ovest verso sud.
                        foreach (float side in new[] { -1f, 1f })
                        {
                            if (!_ctx.Chance(0.32f)) continue;
                            var p = new Vector3(rc + side * 4.6f, 0f, s0 + off);
                            if (InPlayArea(p) && IsClear(p, 2f)) ParkedCar(p, side > 0 ? 0f : 180f);
                        }
                        foreach (float side in new[] { -1f, 1f })
                        {
                            if (!_ctx.Chance(0.32f)) continue;
                            var p = new Vector3(s0 + off, 0f, rc + side * 4.6f);
                            if (InPlayArea(p) && IsClear(p, 2f)) ParkedCar(p, side > 0 ? 270f : 90f);
                        }
                    }
                }
            }
        }

        static bool InPlayArea(Vector3 p) => Mathf.Abs(p.x) < RingOuter - 2f && Mathf.Abs(p.z) < RingOuter - 2f;

        void ParkedCar(Vector3 p, float yaw)
        {
            string id = _ctx.Pick(CivilCars);
            int paint = _ctx.Range(0, ArtLibrary.CarPaints.Length);
            _ctx.Model(id, new Vector3(p.x, p.y, p.z), yaw + _ctx.Range(-2f, 2f), paint);
        }

        // ------------------------------------------------------------------ uscite e confini

        void Exits()
        {
            var north = _ctx.Gateway(new Vector3(ExitNorthX, 0f, Edge), Side.North, Road, "SUBURBS");
            var east = _ctx.Gateway(new Vector3(Edge, 0f, ExitEastZ), Side.East, Road, "INDUSTRIAL");
            var south = _ctx.Gateway(new Vector3(ExitSouthX, 0f, -Edge), Side.South, Road, "OUTSKIRTS");
            _ctx.Anchor("exit.suburbs", AnchorKind.Exit, north, 0f, 5f, nameof(AreaId.Suburbs));
            _ctx.Anchor("exit.industrial", AnchorKind.Exit, east, 90f, 5f, nameof(AreaId.Industrial));
            _ctx.Anchor("exit.outskirts", AnchorKind.Exit, south, 180f, 5f, nameof(AreaId.Outskirts));
            // Arrivi: dentro la mappa, sulla strada dell'uscita, rivolti verso il centro.
            _ctx.Anchor("arrival.from_suburbs", AnchorKind.Arrival, new Vector3(ExitNorthX + 3f, 0f, 120f), 180f, 3f, nameof(AreaId.Suburbs));
            _ctx.Anchor("arrival.from_industrial", AnchorKind.Arrival, new Vector3(120f, 0f, ExitEastZ - 3f), 270f, 3f, nameof(AreaId.Industrial));
            _ctx.Anchor("arrival.from_outskirts", AnchorKind.Arrival, new Vector3(ExitSouthX - 3f, 0f, -120f), 0f, 3f, nameof(AreaId.Outskirts));

            // Cartelli stradali che indicano le uscite dalla piazza.
            _ctx.Sign("SUBURBS ^", new Vector3(-26.2f, 4.2f, 26.2f), 180f, 0.07f, Pal.AwningGreen, Pal.SignWhite, 0.15f);
            _ctx.Sign("INDUSTRIAL ^", new Vector3(26.2f, 4.2f, 13.8f), 270f, 0.07f, Pal.AwningGreen, Pal.SignWhite, 0.15f);
            _ctx.Sign("OUTSKIRTS ^", new Vector3(26.2f, 4.2f, -26.2f), 0f, 0.07f, Pal.AwningGreen, Pal.SignWhite, 0.15f);
            foreach (var p in new[] { new Vector3(-26.2f, Sw, 26.2f), new Vector3(26.2f, Sw, 13.8f), new Vector3(26.2f, Sw, -26.2f) })
                _ctx.Box(p + Vector3.up * 1.9f, new Vector3(0.14f, 3.8f, 0.14f), Pal.MetalDark);

            // Muri invisibili sul confine, aperti solo dentro i portali.
            const float wallH = 40f, t = 1f, gapHalf = Road * 0.5f + 1.5f;
            BoundaryX(Edge + t * 0.5f, ExitNorthX, gapHalf, wallH, t);
            BoundaryX(-Edge - t * 0.5f, ExitSouthX, gapHalf, wallH, t);
            BoundaryZ(Edge + t * 0.5f, ExitEastZ, gapHalf, wallH, t);
            BoundaryZ(-Edge - t * 0.5f, float.NaN, gapHalf, wallH, t);
        }

        void BoundaryX(float z, float gapX, float gapHalf, float h, float t)
        {
            float a0 = -Edge - t, a1 = Edge + t;
            if (float.IsNaN(gapX)) { _ctx.Collider(new Vector3(0f, h * 0.5f, z), new Vector3(a1 - a0, h, t)); return; }
            float l0 = gapX - gapHalf - a0, l1 = a1 - (gapX + gapHalf);
            _ctx.Collider(new Vector3(a0 + l0 * 0.5f, h * 0.5f, z), new Vector3(l0, h, t));
            _ctx.Collider(new Vector3(a1 - l1 * 0.5f, h * 0.5f, z), new Vector3(l1, h, t));
        }

        void BoundaryZ(float x, float gapZ, float gapHalf, float h, float t)
        {
            float a0 = -Edge - t, a1 = Edge + t;
            if (float.IsNaN(gapZ)) { _ctx.Collider(new Vector3(x, h * 0.5f, 0f), new Vector3(t, h, a1 - a0)); return; }
            float l0 = gapZ - gapHalf - a0, l1 = a1 - (gapZ + gapHalf);
            _ctx.Collider(new Vector3(x, h * 0.5f, a0 + l0 * 0.5f), new Vector3(t, h, l0));
            _ctx.Collider(new Vector3(x, h * 0.5f, a1 - l1 * 0.5f), new Vector3(t, h, l1));
        }

        /// <summary>Edifici di sfondo oltre il confine (senza collider): chiudono la vista e non lasciano il vuoto.</summary>
        void Perimeter()
        {
            for (int side = 0; side < 4; side++)
            {
                float gap = side switch { 0 => ExitNorthX, 1 => ExitSouthX, 2 => ExitEastZ, _ => float.NaN };
                for (float a = -168f; a < 168f; a += 16f)
                {
                    float w = _ctx.Range(11f, 15f), d = _ctx.Range(12f, 18f);
                    float h = _ctx.Range(10f, 34f);
                    float center = a + 8f;
                    if (!float.IsNaN(gap) && Mathf.Abs(center - gap) < 18f) continue; // spazio per il portale e il tunnel
                    float off = Edge + 3.5f + d * 0.5f;
                    Vector3 p = side switch
                    {
                        0 => new Vector3(center, 0f, off),
                        1 => new Vector3(center, 0f, -off),
                        2 => new Vector3(off, 0f, center),
                        _ => new Vector3(-off, 0f, center),
                    };
                    bool alongX = side < 2;
                    var spec = BuildingSpec.Make(alongX ? w : d, alongX ? d : w, Mathf.Round(h / 3.2f) * 3.2f + 0.4f, _ctx.Pick(Facades), _ctx.Pick(Glasses));
                    spec.RoofUnits = _ctx.Range(0, 2);
                    spec.ShopFront = _ctx.Chance(0.5f);
                    BackgroundBuilding(p, spec);
                }
            }
        }

        void BackgroundBuilding(Vector3 p, BuildingSpec spec)
        {
            // Come Building, ma senza collider (è fuori dal confine): disegna solo corpo e fasce.
            float y0 = 0f, h = spec.Height;
            _ctx.Box(new Vector3(p.x, y0 + h * 0.5f, p.z), new Vector3(spec.Width, h, spec.Depth), spec.Facade, 0f, false);
            for (float y = 4.15f; y + 1.35f < h - 0.5f; y += spec.FloorHeight)
                _ctx.Box(new Vector3(p.x, y + 0.675f, p.z), new Vector3(spec.Width + 0.12f, 1.35f, spec.Depth + 0.12f), spec.Glass, 0f, false, BoxFaces.All);
            _ctx.Box(new Vector3(p.x, h + 0.4f, p.z), new Vector3(spec.Width, 0.8f, spec.Depth), spec.Roof, 0f, false);
        }

        // ------------------------------------------------------------------ inquadrature per l'anteprima

        void PreviewShots()
        {
            _ctx.PreviewShot("plaza_street", new Vector3(-7f, 1.7f, -9.5f), new Vector3(3f, 6f, 30f));
            _ctx.PreviewShot("skyline", new Vector3(-118f, 78f, -178f), new Vector3(10f, 22f, 20f));
            _ctx.PreviewShot("bank", new Vector3(7f, 1.8f, 17f), new Vector3(0f, 5f, 32f));
            _ctx.PreviewShot("bank_interior", new Vector3(-6f, 2.2f, 35f), new Vector3(1f, 1.4f, 46f));
            _ctx.PreviewShot("dealership", new Vector3(15.5f, 1.9f, -9f), new Vector3(33f, 2.2f, 2f));
            _ctx.PreviewShot("dealership_interior", new Vector3(30.2f, 2.0f, -9.4f), new Vector3(42f, 1.0f, 3f));
            _ctx.PreviewShot("exit_industrial", new Vector3(112f, 2.4f, 13f), new Vector3(152f, 6f, 20f));
            _ctx.PreviewShot("police", new Vector3(-50f, 2.2f, -12f), new Vector3(-74f, 3f, 1f));
            _ctx.PreviewShot("shop_interior", new Vector3(-47f, 1.8f, -30.8f), new Vector3(-37f, 1.0f, -39f));
            _ctx.PreviewShot("garage_alley", new Vector3(66f, 1.8f, -38.5f), new Vector3(90f, 1.5f, -41f));
            _ctx.PreviewShot("parking", new Vector3(28f, 6f, 24f), new Vector3(42f, 0f, 44f));
            _ctx.PreviewShot("overview", new Vector3(0f, 260f, -150f), new Vector3(0f, 0f, 5f));
        }
    }
}
