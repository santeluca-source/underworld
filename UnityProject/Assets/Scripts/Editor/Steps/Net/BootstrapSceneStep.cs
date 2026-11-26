using Underworld.Net;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Crea la scena Bootstrap (indice 0 della build): un oggetto GameRoot con NetworkManager,
    /// UnderworldTransport, GameLauncher e GameRoot (prefab Player e lista dei prefab di rete collegati).
    /// Tick 30, approvazione delle connessioni e gestione delle scene accese.
    /// </summary>
    public sealed class BootstrapSceneStep : IProjectSetupStep
    {
        public const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        public int Order => 400;
        public string Name => "Scena Bootstrap (rete)";

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder("Assets/Scenes");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabStep.PrefabPath);
            if (playerPrefab == null) throw new System.Exception("Manca il prefab Player: il passo PlayerPrefabStep deve girare prima.");
            var prefabs = ctx.GetNetworkPrefabs();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera nera solo per la scena di avvio (niente scritta "No cameras rendering").
            var camGo = new GameObject("BootstrapCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;

            var root = new GameObject("GameRoot");
            var transport = root.AddComponent<UnderworldTransport>();
            var nm = root.AddComponent<NetworkManager>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.TickRate = NetConstants.TickRate;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = true;
            nm.NetworkConfig.PlayerPrefab = null;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            root.AddComponent<GameLauncher>();
            var gr = root.AddComponent<GameRoot>();
            gr.playerPrefab = playerPrefab;
            gr.networkPrefabs = prefabs;

            EditorSceneManager.SaveScene(scene, ScenePath);
            ctx.AddBuildScene(ScenePath, 0);
        }
    }
}
