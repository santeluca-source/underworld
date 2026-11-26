using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Underworld.UI;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Scena MainMenu: solo la camera con lo sfondo 3D (città notturna fatta a codice).
    /// La UI non sta nella scena: la crea UiRoot da sola e decide la schermata dalla scena attiva.
    /// Priorità nella build: 10 (subito dopo Bootstrap).
    /// </summary>
    public sealed class MainMenuSceneStep : IProjectSetupStep
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        public const int BuildPriority = 10;

        public int Order => 410;
        public string Name => "UI: scena MainMenu";

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Luce e cielo li fanno gli shader dello sfondo: niente skybox, niente luce ambiente.
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);

            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.035f, 0.06f);
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 4500f;
            cam.fieldOfView = 42f;
            camGo.AddComponent<AudioListener>();
            var urp = cam.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
            urp.antialiasing = AntialiasingMode.None;

            var backdropGo = new GameObject("MenuBackdrop");
            var backdrop = backdropGo.AddComponent<MenuBackdrop>();
            backdrop.targetCamera = cam;
            backdrop.buildingMaterial = LoadMaterial("SkylineBuildings");
            backdrop.glowMaterial = LoadMaterial("SkylineGlow");
            backdrop.groundMaterial = LoadMaterial("SkylineGround");
            backdrop.skyMaterial = LoadMaterial("SkylineSky");

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.Exception("Salvataggio della scena non riuscito: " + ScenePath);
            ctx.AddBuildScene(ScenePath, BuildPriority);
        }

        static Material LoadMaterial(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(UiAssetsStep.MaterialPath(name));
            if (m == null) throw new System.Exception("Materiale mancante: " + name + " (passo UiAssetsStep)");
            return m;
        }
    }
}
