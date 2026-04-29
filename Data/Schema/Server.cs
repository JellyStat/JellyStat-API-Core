using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;

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

        public List<TaskSettings> TaskSettings { get; set; } = new List<TaskSettings> {
            new TaskSettings(TaskTypes.Backup, (24 * 60)),
            new TaskSettings(TaskTypes.FullSync, (24 * 60)),
            new TaskSettings(TaskTypes.PartialSync, (12 * 60)),
        };
    }
}
