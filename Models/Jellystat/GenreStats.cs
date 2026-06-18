using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class GenreStats : iBaseStats
    {
        public string Name { get; set; }
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
    }
}
