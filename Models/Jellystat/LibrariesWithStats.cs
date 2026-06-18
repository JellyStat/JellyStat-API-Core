using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class LibrariesWithStats : Libraries, iStatsWithActivity
    {
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
        public long? PlaybackDuration { get; set; }

        public long? Size { get; set; }

        public Activity? LatestActivity { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? LatestActivityDate { get; set; }

        public List<TypeCountModel> TypeCounts { get; set; } = new List<TypeCountModel>();
    }
}
