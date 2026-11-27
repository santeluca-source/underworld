using Underworld.Core;
using Underworld.Player;
using Underworld.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Aiuto per creare la scena di un'area: sole, cielo, luce ambiente, nebbia, AreaBuilder e camera.
    /// Il mondo NON è salvato nella scena: lo costruisce AreaBuilder a runtime (scena piccola nel repo).
    /// Le altre aree (Industrial, Suburbs, Outskirts) possono usare <see cref="Create"/> nel loro passo.
    /// </summary>
    public static class AreaSceneFactory
    {
        public const string SkyboxPath = WorldArtStep.MaterialFolder + "/World_Sky.mat";

        public sealed class Look
        {
            public Color Sun = Hex("FFE6C7");
            public float SunIntensity = 1.6f;
            public Vector3 SunEuler = new Vector3(42f, -35f, 0f);
            public Color AmbientSky = Hex("A9C4E6"), AmbientEquator = Hex("C9BBA8"), AmbientGround = Hex("4D4842");
            public Color Fog = Hex("BFCCD9");
            public float FogStart = 70f, FogEnd = 300f;
            /// <summary>Dove guarda la camera all'apertura della scena (prima che arrivi il giocatore).</summary>
            public Vector3 CameraPosition = new Vector3(-7f, 2f, -10f), CameraLookAt = new Vector3(3f, 6f, 30f);
        }

        /// <summary>Crea e salva la scena dell'area. Ritorna il percorso.</summary>
        public static string Create(AreaId area, Look look)
        {
            SetupContext.EnsureFolder("Assets/Scenes");
            string path = $"Assets/Scenes/{AreaInfo.SceneName(area)}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = look.Sun;
            sun.intensity = look.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(look.SunEuler);

            RenderSettings.skybox = SkyMaterial(look);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.Fog;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var builderGo = new GameObject("AreaBuilder");
            builderGo.AddComponent<AreaBuilder>().area = area;

            // Camera della scena: il modulo di rete le assegna il giocatore come bersaglio.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ThirdPersonCamera>();
            camGo.transform.position = look.CameraPosition;
            camGo.transform.rotation = Quaternion.LookRotation(look.CameraLookAt - look.CameraPosition);

            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        /// <summary>Cielo procedurale (shader integrato, funziona anche in URP) con i colori della nebbia.</summary>
        static Material SkyMaterial(Look look)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            var m = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (m == null)
            {
                m = new Material(shader) { name = "World_Sky" };
                AssetDatabase.CreateAsset(m, SkyboxPath);
            }
            m.shader = shader;
            m.SetFloat("_SunDisk", 2f);
            m.SetFloat("_SunSize", 0.035f);
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetColor("_SkyTint", Hex("8FA8C8"));
            m.SetColor("_GroundColor", look.Fog);
            m.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }

    /// <summary>Scena Area_Downtown (indice di build per priorità 100).</summary>
    public sealed class DowntownSceneStep : IProjectSetupStep
    {
        public int Order => 450;
        public string Name => "Scena Area_Downtown";

        public void Run(SetupContext ctx)
        {
            string path = AreaSceneFactory.Create(AreaId.Downtown, new AreaSceneFactory.Look());
            ctx.AddBuildScene(path, 100);
        }
    }
}
