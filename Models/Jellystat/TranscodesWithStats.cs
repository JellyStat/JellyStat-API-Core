using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class TranscodesWithStats : iBaseStats
    {
        public string? Name { get; set; }
        public int? PlayCount { get; set; }


        [System.Text.Json.Serialization.JsonIgnore]
        public Activity? LatestActivity { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? LatestActivityDate { get; set; }


        [System.Text.Json.Serialization.JsonIgnore]
        public long? PlayDuration { get; set; }

    }
}
