using UnityEngine;

namespace Underworld.Core
{
    // Nota (traccia NET): la classe sta in un file con il suo nome. Unity serializza un MonoBehaviour
    // nelle scene solo se il file si chiama come la classe; prima era in AreaAnchors.cs e nelle
    // scene risultava "script mancante". Membri invariati.
    /// <summary>
    /// Punto speciale di un'area (spawn, uscita, negozio, ...). Lo crea il costruttore dell'area.
    /// Id stabile e uguale su server e client. Formato: "area.tipo.nome", es. "downtown.exit.industrial".
    /// </summary>
    public class AreaAnchor : MonoBehaviour
    {
        public string Id;
        public AnchorKind Kind;
        public float Radius = 2f;
        /// <summary>Dato libero del modulo (es. prezzo, area di destinazione).</summary>
        public string Data;

        void OnEnable() => AreaAnchors.Register(this);
        void OnDisable() => AreaAnchors.Unregister(this);
    }
}
