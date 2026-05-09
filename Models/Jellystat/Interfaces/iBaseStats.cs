using Jellystat_API_Core.Data.Schema;

namespace Jellystat_API_Core.Models.Jellystat.Interfaces
{
    public interface iBaseStats
    {
        int? PlayCount { get; set; }
        long? PlayDuration { get; set; }
        Activity? LatestActivity { get; set; }
        DateTime? LatestActivityDate { get; set; }

    }
}
