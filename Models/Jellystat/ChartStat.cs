namespace Jellystat_API_Core.Models.Jellystat
{
    public class ChartStat
    {
        public required string Legend { get; set; }
        public int Count { get; set; } = 0;
        public long PlayDuration { get; set; } = 0;

    }
}
