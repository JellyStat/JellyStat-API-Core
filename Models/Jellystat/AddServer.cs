using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class AddServer
    {
        public required string URL { get; set; }
        public string ExternalURL { get; set; } = "";
        public string APIKey { get; set; } = string.Empty;

        public ServerTypes Type { get; set; } = ServerTypes.Jellyfin;
    }
}
