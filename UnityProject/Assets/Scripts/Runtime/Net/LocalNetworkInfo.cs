using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Underworld.Net
{
    /// <summary>Indirizzo IP di questo PC nella rete di casa (per "Gioca con gli amici").</summary>
    public static class LocalNetworkInfo
    {
        /// <summary>Primo IPv4 non di loopback di una scheda attiva (preferisce le schede con un gateway). Null se non c'è.</summary>
        public static string GetLanIPv4()
        {
            string fallback = null;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) hasGateway = true;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                        string text = ip.ToString();
                        if (text.StartsWith("169.254.", StringComparison.Ordinal)) continue; // indirizzo automatico senza rete
                        if (hasGateway) return text;
                        fallback ??= text;
                    }
                }
            }
            catch (Exception)
            {
                // Alcuni sistemi non danno l'elenco delle schede: provo con il nome del PC.
            }
            if (fallback != null) return fallback;
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
            }
            catch (Exception)
            {
                // Nessuna rete.
            }
            return null;
        }
    }
}
