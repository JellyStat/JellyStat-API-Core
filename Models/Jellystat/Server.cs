using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class Server
    {
        public required string Id { get; set; }
        public required string URL { get; set; }

        public string ExternalURL { get; set; } = "";

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string APIKey { get; set; } = string.Empty;
        public required string Name { get; set; }

        public ServerTypes Type { get; set; }

        public List<TaskSettings> TaskSettings { get; set; } = new List<TaskSettings> {
            new TaskSettings(TaskTypes.Backup, (24 * 60)),
            new TaskSettings(TaskTypes.FullSync, (24 * 60)),
            new TaskSettings(TaskTypes.PartialSync, (12 * 60)),
        };

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
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
