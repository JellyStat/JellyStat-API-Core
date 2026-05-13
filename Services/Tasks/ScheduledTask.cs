using Jellystat_API_Core.Models.Jellystat;

namespace Jellystat_API_Core.Services.Tasks
{
    public class ScheduledTask
    {
        public Server Server { get; set; }
        public TaskSettings TaskSettings { get; set; }
        public DateTime? LastExecutionTime { get; set; }
        public DateTime NextExecutionTime { get; set; }

        public ScheduledTask(Server server, TaskSettings taskSettings)
        {
            Server = server;
            TaskSettings = taskSettings;
            LastExecutionTime = null;
            NextExecutionTime = DateTime.UtcNow;
        }

        public void UpdateNextExecution()
        {
            LastExecutionTime = DateTime.UtcNow;
            NextExecutionTime = DateTime.UtcNow.AddMinutes(TaskSettings.IntervalMinutes);
        }

        public bool ShouldExecute()
        {
            return TaskSettings.Enabled && DateTime.UtcNow >= NextExecutionTime;
        }
    }
}
