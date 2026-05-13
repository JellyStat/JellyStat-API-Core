using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Jellystat_API_Core.Services.Tasks
{
    public class TaskSchedulerService : ITaskSchedulerService
    {
        private readonly ILogger<TaskSchedulerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentQueue<(Server Server, TaskSettings TaskSettings)> _taskQueue;
        private readonly ConcurrentDictionary<string, List<ScheduledTask>> _scheduledTasks;
        private readonly SemaphoreSlim _queueSemaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task? _processingTask;
        private Task? _schedulingTask;

        public TaskSchedulerService(
            ILogger<TaskSchedulerService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _taskQueue = new ConcurrentQueue<(Server, TaskSettings)>();
            _scheduledTasks = new ConcurrentDictionary<string, List<ScheduledTask>>();
            _queueSemaphore = new SemaphoreSlim(0);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Task EnqueueTaskAsync(Server server, TaskSettings taskSettings)
        {
            _taskQueue.Enqueue((server, taskSettings));
            _logger.LogInformation($"Task {taskSettings.Task} enqueued for server {server.Name}. Queue count: {_taskQueue.Count}");
            _queueSemaphore.Release();
            return Task.CompletedTask;
        }

        public Task RegisterServerAsync(Server server)
        {
            var scheduledTasks = new List<ScheduledTask>();

            foreach (var taskSettings in server.TaskSettings)
            {
                if (taskSettings.Enabled)
                {
                    var scheduledTask = new ScheduledTask(server, taskSettings);
                    scheduledTasks.Add(scheduledTask);
                    _logger.LogInformation($"Registered task {taskSettings.Task} for server {server.Name} with interval {taskSettings.IntervalMinutes} minutes");
                }
            }

            _scheduledTasks.AddOrUpdate(server.Id, scheduledTasks, (key, existing) => scheduledTasks);
            return Task.CompletedTask;
        }

        public Task UnregisterServerAsync(string serverId)
        {
            if (_scheduledTasks.TryRemove(serverId, out _))
            {
                _logger.LogInformation($"Unregistered all tasks for server {serverId}");
            }
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task Scheduler Service is starting.");
            _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);
            _schedulingTask = ScheduleTasksAsync(_cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task Scheduler Service is stopping.");
            _cancellationTokenSource.Cancel();

            if (_processingTask != null)
            {
                await _processingTask;
            }

            if (_schedulingTask != null)
            {
                await _schedulingTask;
            }
        }

        public int GetQueueCount()
        {
            return _taskQueue.Count;
        }

        private async Task ScheduleTasksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task scheduler started. Checking scheduled tasks every 30 seconds.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var serverTasks in _scheduledTasks.Values)
                    {
                        foreach (var scheduledTask in serverTasks)
                        {
                            if (scheduledTask.ShouldExecute())
                            {
                                _logger.LogInformation(
                                    $"Scheduled task {scheduledTask.TaskSettings.Task} triggered for server {scheduledTask.Server.Name}. " +
                                    $"Last execution: {scheduledTask.LastExecutionTime?.ToString() ?? "Never"}, " +
                                    $"Next execution: {scheduledTask.NextExecutionTime}");

                                await EnqueueTaskAsync(scheduledTask.Server, scheduledTask.TaskSettings);
                                scheduledTask.UpdateNextExecution();
                            }
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Task scheduling cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in task scheduler.");
                }
            }

            _logger.LogInformation("Task scheduler stopped.");
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task queue processor started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _queueSemaphore.WaitAsync(cancellationToken);

                    if (_taskQueue.TryDequeue(out var taskItem))
                    {
                        var (server, taskSettings) = taskItem;
                        _logger.LogInformation($"Processing task {taskSettings.Task} for server {server.Name}");

                        try
                        {
                            await ExecuteTaskAsync(server, taskSettings);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error executing task {taskSettings.Task} for server {server.Name}");
                        }

                        if (_taskQueue.Count > 0)
                        {
                            _logger.LogInformation($"Waiting 5 seconds before processing next task. Remaining tasks: {_taskQueue.Count}");
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Task queue processing cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in task queue processor.");
                }
            }

            _logger.LogInformation("Task queue processor stopped.");
        }

        private async Task ExecuteTaskAsync(Server server, TaskSettings taskSettings)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var apiInstanceManager = _serviceProvider.GetRequiredService<IAPIInstanceManager>();
                iAPI? api = apiInstanceManager.GetInstance(server.Id);
                if (api == null)
                {
                    return;
                }

                switch (taskSettings.Task)
                {
                    case TaskTypes.Backup:
                        _logger.LogInformation($"Executing Backup task for server {server.Name}");
                        await ExecuteBackupTask(server);
                        break;

                    case TaskTypes.FullSync:
                        _logger.LogInformation($"Executing FullSync task for server {server.Name}");
                        await api.syncTask.FullSync();
                        break;

                    case TaskTypes.PartialSync:
                        _logger.LogInformation($"Executing PartialSync task for server {server.Name}");
                        await api.syncTask.PartialSync();
                        break;

                    case TaskTypes.PlaybackReportingPluginSync:
                        _logger.LogInformation($"Executing PlaybackReportingPluginSync task for server {server.Name}");
                        await ExecutePlaybackReportingPluginSyncTask(server);
                        break;

                    default:
                        _logger.LogWarning($"Unknown task type: {taskSettings.Task} for server {server.Name}");
                        break;
                }

                stopwatch.Stop();
                _logger.LogInformation($"Task {taskSettings.Task} completed for server {server.Name} in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, $"Task {taskSettings.Task} failed for server {server.Name} after {stopwatch.ElapsedMilliseconds}ms");
                throw;
            }
        }

        private async Task ExecuteBackupTask(Server server)
        {
            _logger.LogInformation($"Backup task logic not yet implemented for server {server.Name}");
            await Task.CompletedTask;
        }

        private async Task ExecutePlaybackReportingPluginSyncTask(Server server)
        {
            _logger.LogInformation($"PlaybackReportingPluginSync task logic not yet implemented for server {server.Name}");
            await Task.CompletedTask;
        }
    }
}
