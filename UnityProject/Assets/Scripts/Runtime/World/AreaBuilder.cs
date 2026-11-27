using System;
using Underworld.Core;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>
    /// Sta nella scena di ogni area (Area_Downtown, ...). In Awake costruisce il mondo con il costruttore dell'area
    /// (stesso seme su server e client), poi avvisa con <see cref="WorldBuilt"/>.
    /// Sul server headless crea solo collider e punti speciali; sul server costruisce anche la NavMesh.
    /// Quando la scena si chiude libera mesh, materiali e texture.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class AreaBuilder : MonoBehaviour
    {
        public AreaId area = AreaId.Downtown;

        [Tooltip("0 = seme standard dell'area. Cambialo solo per prove: server e client devono usare lo stesso.")]
        public int seed;

        /// <summary>Il costruttore dell'area caricata (null se non c'è).</summary>
        public static AreaBuilder Current { get; private set; }

        /// <summary>Chiamato quando il mondo dell'area è pronto (punti speciali registrati).</summary>
        public static event Action<AreaBuilder> WorldBuilt;

        public BuildResult Result { get; private set; }
        public bool IsBuilt => Result != null;

        /// <summary>
        /// Chiama <paramref name="callback"/> quando il mondo è pronto: subito se è già costruito,
        /// altrimenti al prossimo <see cref="WorldBuilt"/> (una volta sola). Utile ai moduli che si
        /// avviano dopo la scena e vogliono attaccare oggetti ai punti speciali.
        /// </summary>
        public static void WhenBuilt(Action<AreaBuilder> callback)
        {
            if (callback == null) return;
            if (Current != null && Current.IsBuilt) { callback(Current); return; }
            void Once(AreaBuilder b)
            {
                WorldBuilt -= Once;
                callback(b);
            }
            WorldBuilt += Once;
        }

        void Awake()
        {
            Current = this;
            // Il mondo usa coordinate assolute: il costruttore sta nell'origine.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            Build(WorldBuildOptions.ForThisProcess());
        }

        /// <summary>Costruisce il mondo (una volta sola).</summary>
        public BuildResult Build(WorldBuildOptions options)
        {
            if (Result != null) return Result;
            options ??= WorldBuildOptions.ForThisProcess();
            if (seed != 0) options.Seed = seed;
            var root = new GameObject("World").transform;
            root.SetParent(transform, false);
            try
            {
                Result = WorldBuilder.Build(area, root, options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[World] Costruzione dell'area {area} fallita: {e}");
                // Niente mondo a metà nella scena (WorldBuilder ha già liberato le risorse create).
                if (root != null)
                {
                    if (Application.isPlaying) Destroy(root.gameObject);
                    else DestroyImmediate(root.gameObject);
                }
                return null;
            }
            if (Result.Graphics) HudBus.SetArea(AreaInfo.DisplayName(area));
            WorldBuilt?.Invoke(this);
            return Result;
        }

        /// <summary>Costruisce la NavMesh se non c'è (il server la costruisce da solo dopo il mondo).</summary>
        public void EnsureNavMesh()
        {
            if (Result != null && Result.NavSurface == null) WorldBuilder.BuildNavMesh(Result);
        }

        void OnDestroy()
        {
            if (Result != null) WorldBuilder.Release(Result);
            if (Current == this) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            WorldBuilt = null;
        }

#if UNITY_EDITOR
        [ContextMenu("Costruisci ora (anteprima nell'editor)")]
        void EditorPreview()
        {
            if (Application.isPlaying) return;
            var old = transform.Find("World");
            if (old != null) DestroyImmediate(old.gameObject);
            var root = new GameObject("World") { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(transform, false);
            WorldBuilder.Build(area, root, new WorldBuildOptions { Graphics = true, Seed = seed });
        }
#endif
    }
}
