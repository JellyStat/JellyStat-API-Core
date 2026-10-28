using System.Net;
using System.Net.Sockets;

namespace Jellystat_API_Core.Helpers
{
    public static class NetworkHelper
    {
        /// <summary>
        /// True only for globally routable addresses. Rejects unspecified, loopback, private, link-local,
        /// carrier-grade NAT, multicast/reserved and IPv6 unique-local ranges.
        /// </summary>
        public static bool IsPublicAddress(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return false;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = ip.GetAddressBytes();
                return !(b[0] == 0
                    || b[0] == 10
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || b[0] >= 224);
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)) return false;
                byte first = ip.GetAddressBytes()[0];
                return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (first & 0xFE) == 0xFC);
            }

            return false;
        }
    }
}
