using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class ItemsWithStats : ItemsWithParentData, iStatsWithActivity
    {
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }

        public Activity? LatestActivity { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? LatestActivityDate { get; set; }
        public bool HasArchivedItems { get; set; } = false;
    }
}
