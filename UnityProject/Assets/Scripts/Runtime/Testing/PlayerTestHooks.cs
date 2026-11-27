using System;
using Underworld.Net;
using Underworld.Server;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Testing
{
    /// <summary>
    /// Canale dei comandi di test sul personaggio (prefab Player). Il componente c'è sempre (il prefab deve essere
    /// uguale su server e client), ma il server esegue i comandi solo con -testmode (ServerTestHooks):
    /// senza, ogni richiesta è rifiutata, contata e la risposta è "disabled".
    /// Lo usano solo gli scenari automatici: il gioco non lo chiama mai.
    /// </summary>
    public sealed class PlayerTestHooks : NetworkBehaviour
    {
        /// <summary>Il componente del giocatore locale (null sul server).</summary>
        public static PlayerTestHooks Local { get; private set; }

        /// <summary>Risposte ricevute dal server (proprietario).</summary>
        public int Replies { get; private set; }
        public bool LastOk { get; private set; }
        public string LastResult { get; private set; }

        public event Action<bool, string> Replied;

        public override void OnNetworkSpawn()
        {
            if (IsOwner && !IsServer) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        /// <summary>Proprietario: manda un comando (set_cash, give_item, state).</summary>
        public void Send(string command, string arg = "", long value = 0)
        {
            if (!IsOwner || !IsSpawned) return;
            TestCommandRpc(new FixedString32Bytes(command ?? ""), new FixedString64Bytes(arg ?? ""), value);
        }

        [Rpc(SendTo.Server)]
        void TestCommandRpc(FixedString32Bytes command, FixedString64Bytes arg, long value, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} Comando di test dal client {sender} su un personaggio non suo: ignorato.");
                return;
            }
            bool ok = ServerTestHooks.TryExecute(sender, command.ToString(), arg.ToString(), value, out string result);
            if (result != null && result.Length > 500) result = result.Substring(0, 500);
            ReplyRpc(ok, new FixedString512Bytes(result ?? ""));
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ReplyRpc(bool ok, FixedString512Bytes result, RpcParams rpcParams = default)
        {
            if (!IsOwner || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            Replies++;
            LastOk = ok;
            LastResult = result.ToString();
            Replied?.Invoke(ok, LastResult);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Local = null;
    }
}
