using Jellystat_API_Core.Models.Jellystat;
using System.Collections.Generic;

namespace Jellystat_API_Core.Models.WebSocket
{
    public class TaskQueueUpdate
    {
        public List<TaskSettings> EnqueuedTasks { get; set; } = new();
        public TaskSettings? CurrentTask { get; set; }
    }
}
