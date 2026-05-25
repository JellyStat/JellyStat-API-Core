namespace Jellystat_API_Core.Models.Jellystat
{
    public class ChartStats
    {
        public required string Key { get; set; }

        public List<ChartStat> Stats { get; set; } = new List<ChartStat>();

    }
}
