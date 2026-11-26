using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Underworld.Dev;
using Underworld.Player;

namespace Underworld.EditorTools
{
    /// <summary>
    /// VECCHIO generatore (Fase 2): salva Downtown dentro la scena Downtown_Test.
    /// Sarà sostituito dal costruttore a runtime (AreaBuilder) e poi cancellato.
    /// </summary>
    public sealed class LegacyDowntownStep : IProjectSetupStep
    {
        public int Order => 500;
        public string Name => "Scena Downtown_Test (vecchia)";

        const string ScenePath = "Assets/Scenes/Downtown_Test.unity";
        const string MatFolder = "Assets/Materials/Generated";

        static readonly Color Asphalt = Hex("2E3036");
        static readonly Color Sidewalk = Hex("9A968E");
        static readonly Color Grass = Hex("6FA35A");
        static readonly Color Marking = Hex("E8E4D8");
        static readonly Color Glass = Hex("33475E");
        static readonly Color Roof = Hex("4A4A50");
        static readonly Color[] Facades =
        {
            Hex("C9B79C"), Hex("B5654A"), Hex("8C8F96"), Hex("D8D2C4"),
            Hex("6E7F8D"), Hex("A88B6A"), Hex("C4A35A"), Hex("7D5A50"),
        };
        static readonly Color[] CarColors =
        {
            Hex("C0392B"), Hex("2C6FBB"), Hex("F1C40F"), Hex("ECF0F1"), Hex("1C1C1C"), Hex("27AE60"),
        };

        readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();
        Shader _lit;

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder(MatFolder);
            SetupContext.EnsureFolder("Assets/Scenes");
            _lit = UrpStep.LitShader();
            BuildScene();
            ctx.AddBuildScene(ScenePath, 500);
        }

        void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex("FFE6C7");
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("A9C4E6");
            RenderSettings.ambientEquatorColor = Hex("C9BBA8");
            RenderSettings.ambientGroundColor = Hex("4D4842");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("BFCCD9");
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 260f;
            RenderSettings.sun = sun;

            var world = new GameObject("Downtown").transform;
            BuildCity(world);

            var player = BuildPlayer(new Vector3(0f, 0.35f, -4f));

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 600f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            var tpc = camGo.AddComponent<ThirdPersonCamera>();
            tpc.target = player.transform;
            camGo.transform.position = player.transform.position + new Vector3(0, 2.5f, -5f);
            player.GetComponent<ThirdPersonController>().cameraTransform = camGo.transform;

            var dev = new GameObject("Dev");
            dev.AddComponent<DevHud>();
            dev.AddComponent<AutoScreenshot>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        const int Grid = 7;
        const float Block = 28f;
        const float Road = 12f;
        const float Pitch = Block + Road;

        void BuildCity(Transform world)
        {
            var rng = new System.Random(1337);
            float half = Grid * Pitch * 0.5f;

            Box("Asphalt", world, new Vector3(0, -0.1f, 0), new Vector3(Grid * Pitch + 80f, 0.2f, Grid * Pitch + 80f), Asphalt);

            var markings = new GameObject("RoadMarkings").transform; markings.SetParent(world);
            var blocks = new GameObject("Blocks").transform; blocks.SetParent(world);
            var props = new GameObject("Props").transform; props.SetParent(world);

            int center = Grid / 2;
            for (int gx = 0; gx < Grid; gx++)
            for (int gz = 0; gz < Grid; gz++)
            {
                Vector3 c = new Vector3(-half + Pitch * (gx + 0.5f), 0f, -half + Pitch * (gz + 0.5f));
                var blockRoot = new GameObject($"Block_{gx}_{gz}").transform;
                blockRoot.SetParent(blocks);
                Box("Sidewalk", blockRoot, c + Vector3.up * 0.1f, new Vector3(Block, 0.2f, Block), Sidewalk);
                float dist = Mathf.Max(Mathf.Abs(gx - center), Mathf.Abs(gz - center));
                if (gx == center && gz == center) BuildPlaza(blockRoot, c, rng);
                else if (dist == 3 && rng.NextDouble() < 0.18) BuildPark(blockRoot, c, rng);
                else BuildBuildings(blockRoot, c, dist, rng);
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                    Lamp(props, c + corner * (Block * 0.5f - 1.2f));
            }

            for (int i = 1; i < Grid; i++)
            {
                float roadPos = -half + Pitch * i;
                for (float t = -half; t < half; t += 6f)
                {
                    Decal(markings, new Vector3(roadPos, 0.01f, t + 1.5f), new Vector3(0.25f, 0.02f, 3f));
                    Decal(markings, new Vector3(t + 1.5f, 0.01f, roadPos), new Vector3(3f, 0.02f, 0.25f));
                }
            }

            for (int i = 0; i < 40; i++)
            {
                int line = rng.Next(1, Grid);
                float roadPos = -half + Pitch * line;
                float along = -half + (float)rng.NextDouble() * Grid * Pitch;
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                bool alongZ = rng.NextDouble() < 0.5;
                Vector3 pos = alongZ
                    ? new Vector3(roadPos + side * (Road * 0.5f - 1.6f), 0f, along)
                    : new Vector3(along, 0f, roadPos + side * (Road * 0.5f - 1.6f));
                if (Mathf.Abs(pos.x) < Pitch * 0.6f && Mathf.Abs(pos.z) < Pitch * 0.6f) continue;
                Car(props, pos, alongZ ? 0f : 90f, CarColors[rng.Next(CarColors.Length)]);
            }

            foreach (var t in world.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        }

        void BuildBuildings(Transform root, Vector3 c, float dist, System.Random rng)
        {
            float lot = (Block - 2f) * 0.5f;
            for (int lx = 0; lx < 2; lx++)
            for (int lz = 0; lz < 2; lz++)
            {
                Vector3 lc = c + new Vector3((lx - 0.5f) * lot, 0.2f, (lz - 0.5f) * lot);
                float w = lot - 1.5f - (float)rng.NextDouble() * 2f;
                float d = lot - 1.5f - (float)rng.NextDouble() * 2f;
                float maxH = Mathf.Lerp(48f, 10f, dist / 3f);
                float h = Mathf.Round(Mathf.Lerp(maxH * 0.45f, maxH, (float)rng.NextDouble()) / 3.2f) * 3.2f;
                h = Mathf.Max(h, 6.4f);
                Building(root, lc, w, d, h, Facades[rng.Next(Facades.Length)], rng);
            }
        }

        void Building(Transform parent, Vector3 basePos, float w, float d, float h, Color facade, System.Random rng)
        {
            var b = new GameObject("Building").transform;
            b.SetParent(parent);
            b.position = basePos;
            Box("Body", b, basePos + Vector3.up * (h * 0.5f), new Vector3(w, h, d), facade);
            int floors = Mathf.FloorToInt(h / 3.2f);
            for (int f = 1; f < floors; f++)
            {
                float y = f * 3.2f - 1.2f;
                var band = Box("Windows", b, basePos + Vector3.up * y, new Vector3(w + 0.12f, 1.3f, d - 1.2f), Glass);
                Object.DestroyImmediate(band.GetComponent<Collider>());
                var band2 = Box("Windows", b, basePos + Vector3.up * y, new Vector3(w - 1.2f, 1.3f, d + 0.12f), Glass);
                Object.DestroyImmediate(band2.GetComponent<Collider>());
            }
            var door = Box("Door", b, basePos + new Vector3(0, 1.2f, -d * 0.5f - 0.05f), new Vector3(2f, 2.4f, 0.2f), Hex("2B2B2B"));
            Object.DestroyImmediate(door.GetComponent<Collider>());
            Box("Roof", b, basePos + Vector3.up * (h + 0.25f), new Vector3(w + 0.4f, 0.5f, d + 0.4f), Roof);
            if (rng.NextDouble() < 0.6)
                Box("RoofUnit", b, basePos + new Vector3(w * 0.2f, h + 1.1f, -d * 0.15f), new Vector3(w * 0.3f, 1.4f, d * 0.3f), Hex("8A8D92"));
        }

        void BuildPlaza(Transform root, Vector3 c, System.Random rng)
        {
            Box("PlazaGrass", root, c + Vector3.up * 0.22f, new Vector3(Block - 6f, 0.06f, Block - 6f), Grass);
            Cylinder("FountainBase", root, c + Vector3.up * 0.5f, new Vector3(6f, 0.3f, 6f), Sidewalk);
            Cylinder("FountainWater", root, c + Vector3.up * 0.62f, new Vector3(5.2f, 0.2f, 5.2f), Hex("4FA3C7"));
            Cylinder("FountainPillar", root, c + Vector3.up * 1.6f, new Vector3(0.8f, 1.2f, 0.8f), Sidewalk);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Tree(root, c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9.5f, rng);
            }
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0, i * 90f + 45f, 0) * Vector3.forward;
                var bench = Box("Bench", root, c + dir * 6f + Vector3.up * 0.65f, new Vector3(2.2f, 0.15f, 0.6f), Hex("8B5A3C"));
                bench.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        void BuildPark(Transform root, Vector3 c, System.Random rng)
        {
            Box("ParkGrass", root, c + Vector3.up * 0.22f, new Vector3(Block - 2f, 0.06f, Block - 2f), Grass);
            for (int i = 0; i < 9; i++)
                Tree(root, c + new Vector3((float)rng.NextDouble() * 20f - 10f, 0, (float)rng.NextDouble() * 20f - 10f), rng);
        }

        void Tree(Transform parent, Vector3 pos, System.Random rng)
        {
            float s = 0.8f + (float)rng.NextDouble() * 0.5f;
            Cylinder("Trunk", parent, pos + Vector3.up * (1.2f * s), new Vector3(0.35f, 1.2f * s, 0.35f), Hex("6B4A33"));
            var crown = Prim(PrimitiveType.Sphere, "Crown", parent, pos + Vector3.up * (3.2f * s), Vector3.one * 2.8f * s, Hex("4E8A3E"));
            crown.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
        }

        void Lamp(Transform parent, Vector3 pos)
        {
            var pole = Cylinder("LampPole", parent, pos + Vector3.up * 2.4f, new Vector3(0.15f, 2.4f, 0.15f), Hex("2F3237"));
            var head = Box("LampHead", parent, pos + Vector3.up * 4.85f, new Vector3(0.6f, 0.2f, 0.6f), Hex("FFF1C1"));
            Object.DestroyImmediate(head.GetComponent<Collider>());
            pole.name = "Lamp";
        }

        void Car(Transform parent, Vector3 pos, float yaw, Color color)
        {
            var car = new GameObject("Car").transform;
            car.SetParent(parent);
            car.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            LocalBox("Body", car, new Vector3(0, 0.6f, 0), new Vector3(1.9f, 0.7f, 4.3f), color);
            LocalBox("Cabin", car, new Vector3(0, 1.2f, -0.2f), new Vector3(1.7f, 0.6f, 2.2f), Glass);
            foreach (var w in new[] { new Vector3(-0.95f, 0.35f, 1.4f), new Vector3(0.95f, 0.35f, 1.4f), new Vector3(-0.95f, 0.35f, -1.4f), new Vector3(0.95f, 0.35f, -1.4f) })
            {
                var wheel = LocalBox("Wheel", car, w, new Vector3(0.3f, 0.7f, 0.7f), Hex("151515"));
                Object.DestroyImmediate(wheel.GetComponent<Collider>());
            }
        }

        void Decal(Transform parent, Vector3 pos, Vector3 size)
        {
            var m = Box("Line", parent, pos, size, Marking);
            Object.DestroyImmediate(m.GetComponent<Collider>());
        }

        GameObject BuildPlayer(Vector3 pos)
        {
            var player = new GameObject("Player") { layer = 2 };
            player.transform.position = pos;
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;
            var ctrl = player.AddComponent<ThirdPersonController>();

            Color jacket = Hex("1F2A38"), skin = Hex("E0B48E"), pants = Hex("2A2A2E"), accent = Hex("E04A2F");
            var body = new GameObject("Body").transform;
            body.SetParent(player.transform, false);
            LocalBox("Torso", body, new Vector3(0, 1.2f, 0), new Vector3(0.62f, 0.7f, 0.34f), jacket);
            LocalBox("Stripe", body, new Vector3(0, 1.2f, 0.175f), new Vector3(0.64f, 0.08f, 0.02f), accent);
            LocalBox("Head", body, new Vector3(0, 1.72f, 0), new Vector3(0.36f, 0.38f, 0.36f), skin);
            LocalBox("Cap", body, new Vector3(0, 1.94f, 0.03f), new Vector3(0.4f, 0.1f, 0.42f), accent);
            LocalBox("EyeL", body, new Vector3(-0.08f, 1.75f, 0.181f), new Vector3(0.06f, 0.06f, 0.01f), Hex("111111"));
            LocalBox("EyeR", body, new Vector3(0.08f, 1.75f, 0.181f), new Vector3(0.06f, 0.06f, 0.01f), Hex("111111"));

            Transform Limb(string name, Vector3 pivot, Vector3 size, Color col)
            {
                var p = new GameObject(name).transform;
                p.SetParent(body, false);
                p.localPosition = pivot;
                LocalBox(name + "Mesh", p, new Vector3(0, -size.y * 0.5f, 0), size, col);
                return p;
            }

            var vis = player.AddComponent<PlayerVisual>();
            vis.controller = ctrl;
            vis.body = body;
            vis.leftLeg = Limb("LegL", new Vector3(-0.15f, 0.85f, 0), new Vector3(0.22f, 0.85f, 0.26f), pants);
            vis.rightLeg = Limb("LegR", new Vector3(0.15f, 0.85f, 0), new Vector3(0.22f, 0.85f, 0.26f), pants);
            vis.leftArm = Limb("ArmL", new Vector3(-0.42f, 1.52f, 0), new Vector3(0.18f, 0.66f, 0.2f), jacket);
            vis.rightArm = Limb("ArmR", new Vector3(0.42f, 1.52f, 0), new Vector3(0.18f, 0.66f, 0.2f), jacket);

            foreach (var col in body.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            foreach (var t in player.GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
            return player;
        }

        GameObject Box(string name, Transform parent, Vector3 worldPos, Vector3 size, Color c)
            => Prim(PrimitiveType.Cube, name, parent, worldPos, size, c);

        GameObject Cylinder(string name, Transform parent, Vector3 worldPos, Vector3 size, Color c)
            => Prim(PrimitiveType.Cylinder, name, parent, worldPos, size, c);

        GameObject LocalBox(string name, Transform parent, Vector3 localPos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }

        GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 worldPos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }

        Material Mat(Color c)
        {
            if (_mats.TryGetValue(c, out var m)) return m;
            string path = $"{MatFolder}/M_{ColorUtility.ToHtmlStringRGB(c)}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(_lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = _lit;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", c == Glass ? 0.85f : 0.15f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            _mats[c] = m;
            return m;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
