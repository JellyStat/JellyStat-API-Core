using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class ClientsWithStats : iBaseStats
    {
        public string ClientName { get; set; }
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
        public DateTime? LastPlayedDate { get; set; }
        public string? LastPlayedName { get; set; }
    }
}
