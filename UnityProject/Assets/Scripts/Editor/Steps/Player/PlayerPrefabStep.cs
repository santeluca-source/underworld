using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Underworld.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Crea il prefab di rete Player (Assets/Prefabs/Player.prefab) e lo registra nella lista dei prefab di rete.
    /// Personaggio a forme semplici (stesso aspetto della vecchia scena di prova) con:
    /// NetworkObject, NetworkTransform (autorità del proprietario: posizione xyz + rotazione y, interpolazione),
    /// CharacterController, ThirdPersonController (spento: lo accende PlayerNetwork sul proprietario),
    /// PlayerVisual, MovementValidator, PlayerNetwork.
    /// Poi chiama ogni IPlayerPrefabContributor (altri moduli aggiungono i loro componenti).
    /// </summary>
    public sealed class PlayerPrefabStep : IProjectSetupStep
    {
        public const string PrefabPath = "Assets/Prefabs/Player.prefab";
        const string MatFolder = "Assets/Materials/Player";
        /// <summary>Layer "Ignore Raycast": la camera non urta il proprio personaggio.</summary>
        const int PlayerLayer = 2;

        public int Order => 250;
        public string Name => "Prefab di rete Player";

        readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();
        Shader _lit;

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder("Assets/Prefabs");
            SetupContext.EnsureFolder(MatFolder);
            _lit = UrpStep.LitShader();

            var root = BuildPlayer();
            try
            {
                RunContributors(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new Exception("Salvataggio del prefab Player non riuscito.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EnsureGlobalObjectIdHash(prefab);
            ctx.RegisterNetworkPrefab(prefab);
            Debug.Log($"[UNDERWORLD] Prefab Player pronto: {PrefabPath} (hash {prefab.GetComponent<NetworkObject>().PrefabIdHash}).");
        }

        static void RunContributors(GameObject root)
        {
            var contributors = TypeCache.GetTypesDerivedFrom<IPlayerPrefabContributor>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (IPlayerPrefabContributor)Activator.CreateInstance(t))
                .OrderBy(c => c.Order).ThenBy(c => c.GetType().FullName, StringComparer.Ordinal)
                .ToList();
            foreach (var c in contributors)
            {
                Debug.Log($"[UNDERWORLD] Player: contributo {c.GetType().Name} (ordine {c.Order}).");
                c.Contribute(root);
            }
        }

        /// <summary>
        /// Netcode calcola l'hash del prefab in OnValidate. Lo chiamo a mano dopo il salvataggio
        /// (in batchmode l'import non sempre lo fa) e salvo di nuovo se è cambiato.
        /// </summary>
        static void EnsureGlobalObjectIdHash(GameObject prefab)
        {
            var no = prefab.GetComponent<NetworkObject>();
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            onValidate?.Invoke(no, null);
            if (no.PrefabIdHash == 0) throw new Exception("Il prefab Player non ha un GlobalObjectIdHash valido.");
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        GameObject BuildPlayer()
        {
            var player = new GameObject("Player") { layer = PlayerLayer };

            player.AddComponent<NetworkObject>();

            var nt = player.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleY = true;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.InLocalSpace = false;
            nt.Interpolate = true;
            nt.UseHalfFloatPrecision = false;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;

            var ctrl = player.AddComponent<ThirdPersonController>();
            ctrl.enabled = false; // lo accende PlayerNetwork solo sul proprietario

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

            player.AddComponent<MovementValidator>();
            player.AddComponent<PlayerNetwork>();

            foreach (var col in body.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            foreach (var t in player.GetComponentsInChildren<Transform>()) t.gameObject.layer = PlayerLayer;
            return player;
        }

        void LocalBox(string name, Transform parent, Vector3 localPos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
        }

        Material Mat(Color c)
        {
            if (_mats.TryGetValue(c, out var m)) return m;
            string path = $"{MatFolder}/M_Player_{ColorUtility.ToHtmlStringRGB(c)}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(_lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = _lit;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
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
