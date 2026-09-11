using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Data.Schema
{
    public class GlobalTasks
    {
        public TaskTypes Task { get; set; }
        public int IntervalMinutes { get; set; } = 60;
        public bool Enabled { get; set; } = true;
    }
}
