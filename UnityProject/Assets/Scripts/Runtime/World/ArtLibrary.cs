using System;
using System.Collections.Generic;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>Da quale kit viene un modello.</summary>
    public enum ArtKit : byte { Code, CarKit, FactoryKit, NatureKit }

    /// <summary>Collider da creare quando il modello è messo nel mondo.</summary>
    public enum ArtCollider : byte
    {
        /// <summary>Nessun collider (fiori, erba, cose piccole).</summary>
        None,
        /// <summary>Scatola uguale al riquadro del modello (auto, casse, coni).</summary>
        Bounds,
        /// <summary>Scatola stretta al centro, alta quanto il modello (alberi: solo il tronco).</summary>
        Trunk,
    }

    /// <summary>Un modello della libreria: id stabile, asset, misure e scala reale.</summary>
    [Serializable]
    public sealed class ArtModel
    {
        [Tooltip("Id stabile, es. car.sedan, prop.cone, nature.tree_oak.")]
        public string id;
        public GameObject prefab;
        public ArtKit kit;
        [Tooltip("Riquadro misurato sul modello in unità del file (tutta la gerarchia).")]
        public Bounds bounds;
        [Tooltip("Scala uniforme che porta il modello alla misura reale (calcolata dalla misura, non a occhio).")]
        public float scale = 1f;
        public ArtCollider collider = ArtCollider.Bounds;
        [Tooltip("Auto civili: coppia di colori della carrozzeria nella colormap (riga * 8 + coppia). -1 = colore fisso.")]
        public int bodyPair = -1;

        /// <summary>Misure reali (metri) del modello.</summary>
        public Vector3 RealSize => bounds.size * scale;

        /// <summary>Riquadro reale rispetto al punto di appoggio del modello.</summary>
        public Bounds RealBounds => new Bounds(bounds.center * scale, bounds.size * scale);

        /// <summary>Vero se si può cambiare il colore della carrozzeria.</summary>
        public bool Recolorable => bodyPair >= 0;
    }

    /// <summary>Un pezzo di mesh del modello con la sua posizione nella gerarchia del prefab.</summary>
    public readonly struct ModelPart
    {
        public readonly Mesh Mesh;
        public readonly Matrix4x4 Local;
        public readonly Material[] Materials;

        public ModelPart(Mesh mesh, Matrix4x4 local, Material[] materials)
        {
            Mesh = mesh;
            Local = local;
            Materials = materials;
        }
    }

    /// <summary>
    /// Libreria dell'arte del mondo (ScriptableObject in Resources/World/ArtLibrary).
    /// La costruisce il passo di setup "Arte del mondo" dai kit Kenney.
    /// Il codice di gioco mette i modelli nel mondo SOLO da qui (BuildContext.Model o Instantiate),
    /// così misure, scala e materiali sono sempre quelli giusti.
    /// </summary>
    [CreateAssetMenu(menuName = "UNDERWORLD/Art Library", fileName = "ArtLibrary")]
    public sealed class ArtLibrary : ScriptableObject
    {
        public const string ResourcePath = "World/ArtLibrary";

        /// <summary>Colori di carrozzeria per le auto civili (coppie della colormap del Car Kit: riga * 8 + coppia).</summary>
        public static readonly int[] CarPaints =
        {
            1 * 8 + 6, // rosso
            1 * 8 + 7, // blu
            1 * 8 + 3, // verde
            1 * 8 + 4, // giallo
            1 * 8 + 5, // arancio
            2 * 8 + 6, // bianco
            2 * 8 + 4, // grigio scuro
            2 * 8 + 3, // argento
            3 * 8 + 4, // verde acqua
            3 * 8 + 3, // blu chiaro
        };

        [Header("Materiali condivisi (URP Lit)")]
        public Material paletteMatte;
        public Material paletteGloss;
        public Material paletteGlow;
        public Material carKit;
        public Material factoryKit;

        [Header("Modelli")]
        public List<ArtModel> models = new List<ArtModel>();

        [Tooltip("Colori pieni dei modelli (Nature Kit). Si aggiungono alla tavolozza dopo i colori Pal.")]
        public List<Color> flatColors = new List<Color>();

        [NonSerialized] Dictionary<string, ArtModel> _byId;
        [NonSerialized] Dictionary<ArtModel, ModelPart[]> _parts;

        static ArtLibrary _loaded;

        /// <summary>La libreria del gioco (caricata una volta da Resources).</summary>
        public static ArtLibrary Load()
        {
            if (_loaded == null) _loaded = Resources.Load<ArtLibrary>(ResourcePath);
            return _loaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _loaded = null;

        void OnEnable()
        {
            _byId = null;
            _parts = null;
        }

        public bool Has(string id) => Find(id) != null;

        /// <summary>Il modello con questo id, o null.</summary>
        public ArtModel Find(string id)
        {
            if (_byId == null || _byId.Count != models.Count)
            {
                _byId = new Dictionary<string, ArtModel>(StringComparer.Ordinal);
                foreach (var m in models)
                    if (m != null && !string.IsNullOrEmpty(m.id)) _byId[m.id] = m;
            }
            return id != null && _byId.TryGetValue(id, out var r) ? r : null;
        }

        /// <summary>Il modello con questo id. Errore chiaro se manca (id sbagliato o setup non eseguito).</summary>
        public ArtModel Get(string id)
        {
            var m = Find(id);
            if (m == null || m.prefab == null)
                throw new KeyNotFoundException($"[World] Modello '{id}' non trovato nella libreria. Esegui il setup (Tools/unity.ps1 -Action setup).");
            return m;
        }

        /// <summary>Materiale condiviso di un kit con texture (Car Kit, Factory Kit). Null per gli altri.</summary>
        public Material KitMaterial(ArtKit kit) => kit switch
        {
            ArtKit.CarKit => carKit,
            ArtKit.FactoryKit => factoryKit,
            _ => null,
        };

        /// <summary>Pezzi di mesh del modello (letti una volta dal prefab).</summary>
        public ModelPart[] GetParts(ArtModel model)
        {
            _parts ??= new Dictionary<ArtModel, ModelPart[]>();
            if (_parts.TryGetValue(model, out var parts)) return parts;

            var filters = model.prefab.GetComponentsInChildren<MeshFilter>(true);
            var list = new List<ModelPart>(filters.Length);
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                list.Add(new ModelPart(mf.sharedMesh, mf.transform.localToWorldMatrix,
                    mr != null ? mr.sharedMaterials : Array.Empty<Material>()));
            }
            parts = list.ToArray();
            _parts[model] = parts;
            return parts;
        }

        /// <summary>Indice di un colore pieno in <see cref="flatColors"/> (tolleranza piccola). -1 se manca.</summary>
        public int FlatColorIndex(Color c)
        {
            for (int i = 0; i < flatColors.Count; i++)
            {
                var f = flatColors[i];
                if (Mathf.Abs(f.r - c.r) < 0.004f && Mathf.Abs(f.g - c.g) < 0.004f && Mathf.Abs(f.b - c.b) < 0.004f)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Crea un'istanza "viva" del modello (oggetti che si muovono, es. veicoli): radice alla posizione data
        /// con la scala reale, materiali condivisi del kit. Per l'arredo fermo usa BuildContext.Model (mesh unite).
        /// </summary>
        public GameObject Instantiate(string id, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var model = Get(id);
            var root = new GameObject(id);
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = Vector3.one * model.scale;
            var inst = Instantiate(model.prefab, root.transform, false);
            inst.name = model.prefab.name;
            var shared = KitMaterial(model.kit);
            if (shared != null)
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = shared;
                    r.sharedMaterials = mats;
                }
            return root;
        }
    }
}
