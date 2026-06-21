using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class ActivityModel : Activity, iBaseStats
    {
        public string? LibraryId { get; set; }
        public Items? Item { get; set; }
        public List<ActivityModel>? GroupedResults { get; set; }
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
        public string? FullName { get; set; }
    }
}
