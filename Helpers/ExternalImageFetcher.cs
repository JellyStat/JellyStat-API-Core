using System.Net;
using System.Net.Sockets;

namespace Jellystat_API_Core.Helpers
{
    /// <summary>
    /// Fetches images from external hosts (e.g. client app icons) without any server credentials.
    /// Only public addresses are reachable: the check runs at connect time on the resolved IP,
    /// so a hostname that resolves to a private address (DNS rebinding) is also blocked.
    /// </summary>
    public static class ExternalImageFetcher
    {
        private const long MaxImageBytes = 2 * 1024 * 1024;

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                // Safe to follow: every hop opens a new connection through ConnectCallback, so a redirect
                // to an internal address is still blocked (e.g. github.com/.../raw/... -> raw.githubusercontent.com)
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                // A proxy would make the connect-time IP check meaningless
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                    if (addresses.Length == 0 || addresses.Any(a => !NetworkHelper.IsPublicAddress(a)))
                    {
                        throw new HttpRequestException($"Blocked request to non-public host '{context.DnsEndPoint.Host}'");
                    }

                    // Try each address in turn, so an IPv6-first host still works on an IPv4-only server
                    Exception? lastError = null;
                    foreach (IPAddress ip in addresses)
                    {
                        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (Exception ex) when (ex is SocketException)
                        {
                            socket.Dispose();
                            lastError = ex;
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }

                    throw new HttpRequestException($"Could not connect to '{context.DnsEndPoint.Host}'", lastError);
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = MaxImageBytes
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Global.UserAgent);
            return client;
        }

        /// <summary>
        /// GETs an absolute http(s) URL. Never throws for network problems: blocked or failed requests
        /// come back as a non-success response so callers can pass the status straight through.
        /// </summary>
        public static async Task<HttpResponseMessage> GetAsync(Uri url)
        {
            if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            try
            {
                return await Client.GetAsync(url);
            }
            catch (TaskCanceledException)
            {
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
            }
            catch (HttpRequestException)
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }
        }
    }
}
