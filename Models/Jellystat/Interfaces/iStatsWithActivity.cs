using Jellystat_API_Core.Data.Schema;

namespace Jellystat_API_Core.Models.Jellystat.Interfaces
{
    public interface iStatsWithActivity : iBaseStats
    {
        Activity? LatestActivity { get; set; }
        DateTime? LatestActivityDate { get; set; }

    }
}
