namespace Jellystat_API_Core.Models.Jellystat.Interfaces
{
    public interface iBaseStats
    {
        int? PlayCount { get; set; }
        long? PlayDuration { get; set; }

    }
}
