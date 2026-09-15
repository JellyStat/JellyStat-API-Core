using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;

namespace Jellystat_API_Core.Services.Tasks
{
    public interface ITaskSchedulerService
    {
        Task EnqueueTaskAsync(Server? server, TaskSettings taskSettings);
        Task RegisterGlobalTasks();
        Task RegisterServerAsync(Server server);
        Task UnregisterServerAsync(string serverId);
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        int GetQueueCount();
        Task<bool> StopRunningTaskAsync(string? serverId, TaskTypes taskType);

        Task RegisterTasksAsync(Server? server, List<TaskSettings>? tasks = null);
    }
}
