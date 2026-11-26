using Underworld.Core;
using Underworld.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Crea la scena Area_TestGround (priorità di build 900): area piatta e leggera per i test automatici.
    /// Sole, piano 200 × 200 m, qualche cassa lontana dagli spawn, Main Camera con ThirdPersonCamera,
    /// 8 punti di spawn in cerchio (id testground.spawn.N).
    /// </summary>
    public sealed class TestGroundSceneStep : IProjectSetupStep
    {
        public const string ScenePath = "Assets/Scenes/Area_TestGround.unity";
        const string MatFolder = "Assets/Materials/TestGround";

        public int Order => 410;
        public string Name => "Scena Area_TestGround";

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder("Assets/Scenes");
            SetupContext.EnsureFolder(MatFolder);
            var lit = UrpStep.LitShader();
            var groundMat = Mat(lit, "M_TestGround_Ground", new Color(0.42f, 0.46f, 0.40f));
            var boxMat = Mat(lit, "M_TestGround_Box", new Color(0.78f, 0.55f, 0.30f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.76f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.7f, 0.7f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.26f);

            var world = new GameObject("TestGround").transform;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(world, false);
            ground.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(200f, 0.2f, 200f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            // Casse lontane dal cerchio degli spawn (le camminate dei test vanno dritte per ~15 m).
            Vector3[] boxes =
            {
                new Vector3(40f, 1f, 40f), new Vector3(-45f, 1.5f, 30f), new Vector3(35f, 0.75f, -50f),
                new Vector3(-30f, 2f, -40f), new Vector3(60f, 1f, 0f), new Vector3(0f, 1f, -65f),
            };
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Box_" + i;
                b.transform.SetParent(world, false);
                float s = boxes[i].y * 2f;
                b.transform.localPosition = boxes[i];
                b.transform.localScale = new Vector3(s, s, s);
                b.GetComponent<Renderer>().sharedMaterial = boxMat;
            }
            foreach (var t in world.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

            var spawns = new GameObject("Spawns").transform;
            const int count = 8;
            const float radius = 8f;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var go = new GameObject("Spawn_" + i);
                go.transform.SetParent(spawns, false);
                go.transform.position = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                // Guarda verso l'esterno.
                go.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                var anchor = go.AddComponent<AreaAnchor>();
                anchor.Id = "testground.spawn." + i;
                anchor.Kind = AnchorKind.Spawn;
                anchor.Radius = 1.5f;
            }

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 400f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ThirdPersonCamera>();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -22f), Quaternion.Euler(25f, 0f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            ctx.AddBuildScene(ScenePath, 900);
        }

        static Material Mat(Shader lit, string name, Color c)
        {
            string path = $"{MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = lit;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
