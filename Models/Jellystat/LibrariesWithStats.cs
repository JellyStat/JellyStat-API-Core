using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Models.Jellystat.Interfaces;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class LibrariesWithStats : Libraries, iBaseStats
    {
        public int? PlayCount { get; set; }
        public long? PlayDuration { get; set; }
        public long? PlaybackDuration { get; set; }

        public long? Size { get; set; }
        public DateTime? LastPlayedDate { get; set; }
        public string? LastPlayedName { get; set; }

        public List<TypeCountModel> TypeCounts { get; set; } = new List<TypeCountModel>();
    }
}
