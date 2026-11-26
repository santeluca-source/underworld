using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Unity Transport con una sola differenza: quando il server è su questo PC (127.0.0.1)
    /// il client apre la porta solo su 127.0.0.1 invece che su tutte le schede di rete.
    /// Così i test e il gioco locale non aprono porte verso la rete (niente avviso del firewall).
    /// </summary>
    public sealed class UnderworldTransport : UnityTransport
    {
        protected override NetworkConnection Connect(NetworkEndpoint serverEndpoint)
        {
            if (serverEndpoint.Family == NetworkFamily.Ipv4 && serverEndpoint.IsLoopback)
            {
                ref NetworkDriver driver = ref GetNetworkDriver();
                if (driver.IsCreated && !driver.Bound)
                {
                    int result = driver.Bind(NetworkEndpoint.LoopbackIpv4);
                    if (result != 0)
                        Debug.LogWarning($"{NetConstants.ClientLog} Bind su 127.0.0.1 non riuscito (codice {result}): uso l'indirizzo standard.");
                }
            }
            return base.Connect(serverEndpoint);
        }
    }
}
