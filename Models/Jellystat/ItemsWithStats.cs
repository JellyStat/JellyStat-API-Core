using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class ItemsWithStats : Items, iBaseStats
    {
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
        public DateTime? LastPlayedDate { get; set; }
        public string? LastPlayedName { get; set; }
    }
}
