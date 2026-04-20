using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Data.Schema
{
    public class Server
    {
        public required string Id { get; set; }
        public required string URL { get; set; }

        public string ExternalURL { get; set; } = "";

        public required string APIKey { get; set; }
        public required string Name { get; set; }

        public ServerTypes Type { get; set; }
    }
}
