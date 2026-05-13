using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.Tasks;

namespace Jellystat_API_Core.Examples
{
    /// <summary>
    /// Example demonstrating how to use the TaskSchedulerService
    /// </summary>
    public class TaskSchedulerServiceExample
    {
        private readonly ITaskSchedulerService _taskSchedulerService;

        public TaskSchedulerServiceExample(ITaskSchedulerService taskSchedulerService)
        {
            _taskSchedulerService = taskSchedulerService;
        }

        /// <summary>
        /// Example: Register a server for automatic task scheduling
        /// This will automatically enqueue tasks based on their IntervalMinutes and Enabled settings
        /// </summary>
        public async Task RegisterServerForSchedulingExample()
        {
            var server = new Server
            {
                Id = "server-123",
                Name = "My Jellyfin Server",
                URL = "http://localhost:8096",
                APIKey = "your-api-key-here",
                TaskSettings = new List<TaskSettings>
                {
                    new TaskSettings(TaskTypes.Backup, 1440, true),        // Every 24 hours
                    new TaskSettings(TaskTypes.FullSync, 1440, true),      // Every 24 hours
                    new TaskSettings(TaskTypes.PartialSync, 720, true),    // Every 12 hours
                }
            };

            // Register the server - tasks will be automatically scheduled based on their intervals
            await _taskSchedulerService.RegisterServerAsync(server);

            // The scheduler will check every 30 seconds and automatically enqueue tasks
            // when their interval has elapsed since last execution
        }

        /// <summary>
        /// Example: Manually enqueue a task (bypass scheduling)
        /// </summary>
        public async Task ManuallyEnqueueTaskExample(Server server)
        {
            // Find a specific task setting
            var fullSyncTask = server.TaskSettings.FirstOrDefault(ts => ts.Task == TaskTypes.FullSync);

            if (fullSyncTask != null)
            {
                // Manually enqueue the task immediately (won't wait for scheduled interval)
                await _taskSchedulerService.EnqueueTaskAsync(server, fullSyncTask);
            }
        }

        /// <summary>
        /// Example: Register multiple servers for automatic scheduling
        /// </summary>
        public async Task RegisterMultipleServersExample(List<Server> servers)
        {
            foreach (var server in servers)
            {
                await _taskSchedulerService.RegisterServerAsync(server);
            }

            // All servers will now have their tasks automatically scheduled
            // based on each task's IntervalMinutes and Enabled status
        }

        /// <summary>
        /// Example: Unregister a server to stop automatic scheduling
        /// </summary>
        public async Task UnregisterServerExample(string serverId)
        {
            await _taskSchedulerService.UnregisterServerAsync(serverId);

            // Tasks for this server will no longer be automatically scheduled
            // (but manually enqueued tasks will still execute)
        }

        /// <summary>
        /// Example: Check queue count
        /// </summary>
        public void CheckQueueCountExample()
        {
            int queueCount = _taskSchedulerService.GetQueueCount();
            Console.WriteLine($"Current queue count: {queueCount}");
        }

        /// <summary>
        /// Example: Update server task settings and re-register
        /// </summary>
        public async Task UpdateServerTaskSettingsExample(Server server)
        {
            // Unregister first to stop old scheduling
            await _taskSchedulerService.UnregisterServerAsync(server.Id);

            // Modify task settings
            var fullSyncTask = server.TaskSettings.FirstOrDefault(ts => ts.Task == TaskTypes.FullSync);
            if (fullSyncTask != null)
            {
                fullSyncTask.IntervalMinutes = 360; // Change to 6 hours
                fullSyncTask.Enabled = false;        // Disable
            }

            // Re-register with new settings
            await _taskSchedulerService.RegisterServerAsync(server);
        }
    }
}
