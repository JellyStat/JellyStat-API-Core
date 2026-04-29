using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class TaskSettings
    {
        public TaskTypes Task { get; set; }
        public int IntervalMinutes { get; set; } = 60;

        public bool Enabled { get; set; } = true;

        public TaskSettings(TaskTypes Task, int IntervalMinutes = 60, bool Enabled = true)
        {
            this.Task = Task;
            this.IntervalMinutes = IntervalMinutes;
            this.Enabled = Enabled;
        }
    }
}
