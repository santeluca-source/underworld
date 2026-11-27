using Underworld.Economy;
using Underworld.Inventory;
using Underworld.Testing;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Fasi 5–6: aggiunge al prefab Player il portafoglio (PlayerWallet), l'inventario (PlayerInventory)
    /// e il canale dei comandi di test (PlayerTestHooks, attivo solo su un server con -testmode).
    /// </summary>
    public sealed class ServerPlayerContributor : IPlayerPrefabContributor
    {
        public int Order => 100;

        public void Contribute(GameObject playerRoot)
        {
            if (playerRoot.GetComponent<PlayerWallet>() == null) playerRoot.AddComponent<PlayerWallet>();
            if (playerRoot.GetComponent<PlayerInventory>() == null) playerRoot.AddComponent<PlayerInventory>();
            if (playerRoot.GetComponent<PlayerTestHooks>() == null) playerRoot.AddComponent<PlayerTestHooks>();
        }
    }
}
