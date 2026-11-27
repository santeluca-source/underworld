using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Un modulo che aggiunge i suoi componenti al prefab Player (contratto, sezione 13).
    /// PlayerPrefabStep trova da solo ogni classe che implementa questa interfaccia (TypeCache)
    /// e la chiama dopo aver costruito il personaggio base, in ordine di Order (più basso = prima).
    /// La classe deve avere un costruttore vuoto. Non togliere i componenti di rete del personaggio.
    /// </summary>
    public interface IPlayerPrefabContributor
    {
        int Order { get; }
        void Contribute(GameObject playerRoot);
    }
}
