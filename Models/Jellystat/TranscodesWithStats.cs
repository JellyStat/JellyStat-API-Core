using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class TranscodesWithStats : iBaseStats
    {
        public string? Name { get; set; }
        public int? PlayCount { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public long? PlayDuration { get; set; }

    }
}
