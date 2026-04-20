using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class Server
    {
        public required string Id { get; set; }
        public required string URL { get; set; }

        public string ExternalURL { get; set; } = "";

        public required string APIKey { get; set; }
        public required string Name { get; set; }

        public ServerTypes Type { get; set; }

        public WebSocketClientConfig WebSocketConfig => new WebSocketClientConfig
        {
            Id = Id,
            Url = URL.Replace("http", "ws").Replace("https", "wss") + "/socket",
            Headers = new Dictionary<string, string>
                {
                    { "Authorization", $"MediaBrowser Token=\"{APIKey}\"" },
                    { "User-Agent", $"Jellystat" }
                }

        };

        public Server() { }
    }
}
