using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Classes.Tasks;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Models.WebSocket;
using Jellystat_API_Core.Services.API;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Jellystat_API_Core.Services.Tasks
{
    public class TaskSchedulerService : ITaskSchedulerService
    {
        private readonly ILogger<TaskSchedulerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentQueue<(Server? Server, TaskSettings TaskSettings)> _taskQueue;
        private readonly ConcurrentDictionary<string, List<ScheduledTask>> _scheduledTasks;
        private readonly SemaphoreSlim _queueSemaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task? _processingTask;
        private Task? _schedulingTask;
        private Task? _broadcastingTask;
        private readonly ConcurrentDictionary<string, TaskSettings?> _currentRunningTasks;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private static string GlobalTaskId => "Global";

        public TaskSchedulerService(
            ILogger<TaskSchedulerService> logger,
            IServiceProvider serviceProvider, IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _taskQueue = new ConcurrentQueue<(Server?, TaskSettings)>();
            _scheduledTasks = new ConcurrentDictionary<string, List<ScheduledTask>>();
            _queueSemaphore = new SemaphoreSlim(0);
            _cancellationTokenSource = new CancellationTokenSource();
            _currentRunningTasks = new ConcurrentDictionary<string, TaskSettings?>();
            _serviceScopeFactory = serviceScopeFactory;
        }

        private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task scheduler broadcasting loop started. Broadcasting enqueued tasks every 2 seconds.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var serverIds = _scheduledTasks.Keys.ToList();
                    foreach (var serverId in serverIds)
                    {
                        try
                        {
                            await BroadcastEnqueuedTasksAsync(serverId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error broadcasting enqueued tasks for server {ServerId}", serverId);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Task scheduler broadcasting loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in broadcasting loop.");
                }
            }

            _logger.LogInformation("Task scheduler broadcasting loop stopped.");
        }

        public Task EnqueueTaskAsync(Server? server, TaskSettings taskSettings)
        {
            _taskQueue.Enqueue((server, taskSettings));
            string serverName = server != null ? $" for server {server.Name}" : String.Empty;
            _logger.LogInformation($"Task {taskSettings.Task} enqueued{serverName}. Queue count: {_taskQueue.Count}");
            _queueSemaphore.Release();
            return Task.CompletedTask;
        }

        public async Task RegisterServerAsync(Server server)
        {
            await RegisterTasksAsync(server);
        }

        public async Task RegisterGlobalTasks()
        {
            List<TaskSettings>? tasks = new List<TaskSettings>();
            var scheduledTasks = new List<ScheduledTask>();


            List<Data.Schema.Logs?> latestExecutions = new List<Data.Schema.Logs?>();

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Logs.Where(log => (log.ServerId == null) && log.Success == null).ExecuteUpdateAsync(log => log.SetProperty(l => l.Success, false));


                latestExecutions = await context.Logs
                    .Where(log => log.ServerId == null)
                    .GroupBy(log => log.Task)
                    .Select(group => group.OrderByDescending(log => log.DateCreated).FirstOrDefault())
                    .ToListAsync() ?? [];
                List<Data.Schema.GlobalTasks> globalTasks = new List<Data.Schema.GlobalTasks>();
                globalTasks = await context.GlobalTasks.ToListAsync();
                tasks = globalTasks.Select(gt =>
                {
                    return new TaskSettings(gt.Task, gt.IntervalMinutes, gt.Enabled);
                }).ToList();
            }

            foreach (var taskSettings in tasks)
            {
                if (taskSettings.Enabled && taskSettings.IntervalMinutes > 0)
                {
                    var scheduledTask = new ScheduledTask(null, taskSettings);
                    Data.Schema.Logs? latestLog = latestExecutions.FirstOrDefault(log => log?.Task == taskSettings.Task);
                    if (latestLog != null)
                    {
                        scheduledTask.LastExecutionTime = latestLog.DateCreated;
                        scheduledTask.NextExecutionTime = latestLog.DateCreated.AddMinutes(taskSettings.IntervalMinutes);
                    }
                    scheduledTasks.Add(scheduledTask);
                    _logger.LogInformation($"Registered global task {taskSettings.Task} with interval {taskSettings.IntervalMinutes} minutes");
                }
            }


            _scheduledTasks.AddOrUpdate(GlobalTaskId, scheduledTasks, (key, existing) => scheduledTasks);

        }

        public async Task RegisterTasksAsync(Server? server, List<TaskSettings>? tasks = null)
        {
            if (server == null && tasks == null) return;
            tasks ??= server?.TaskSettings ?? new List<TaskSettings>();

            if (tasks.IsEmpty()) return;

            var scheduledTasks = new List<ScheduledTask>();

            List<Data.Schema.Logs?> latestExecutions = new List<Data.Schema.Logs?>();

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                _currentRunningTasks.TryGetValue(server.Id, out var currentTask);
                if (currentTask == null)
                {
                    await context.Logs.Where(log => (log.ServerId == server.Id || log.ServerId == null) && log.Success == null).ExecuteUpdateAsync(log => log.SetProperty(l => l.Success, false));
                }

                latestExecutions = await context.Logs
                    .Where(log => log.ServerId == server.Id || log.ServerId == null)
                    .GroupBy(log => log.Task)
                    .Select(group => group.OrderByDescending(log => log.DateCreated).FirstOrDefault())
                    .ToListAsync() ?? [];
            }

            foreach (var taskSettings in tasks)
            {
                if (taskSettings.Enabled)
                {
                    var scheduledTask = new ScheduledTask(server, taskSettings);
                    Data.Schema.Logs? latestLog = latestExecutions.FirstOrDefault(log => log?.Task == taskSettings.Task);
                    if (latestLog != null)
                    {
                        scheduledTask.LastExecutionTime = latestLog.DateCreated;
                        scheduledTask.NextExecutionTime = latestLog.DateCreated.AddMinutes(taskSettings.IntervalMinutes);
                    }
                    scheduledTasks.Add(scheduledTask);
                    _logger.LogInformation($"Registered task {taskSettings.Task} for server {server.Name} with interval {taskSettings.IntervalMinutes} minutes");
                }
            }

            _scheduledTasks.AddOrUpdate(server.Id, scheduledTasks, (key, existing) => scheduledTasks);
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
            _broadcastingTask = BroadcastLoopAsync(_cancellationTokenSource.Token);
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

            if (_broadcastingTask != null)
            {
                await _broadcastingTask;
            }
        }

        public int GetQueueCount()
        {
            return _taskQueue.Count;
        }

        private async Task BroadcastEnqueuedTasksAsync(string serverId)
        {
            // Snapshot the queue and find any tasks for the specified server
            var queuedTasks = _taskQueue.ToArray()
                .Where(q => q.Server == null || q.Server.Id == serverId)
                .Select(q => q.TaskSettings)
                .ToList();

            _currentRunningTasks.TryGetValue(serverId, out var currentTask);

            var update = new Models.WebSocket.TaskQueueUpdate()
            {
                EnqueuedTasks = queuedTasks,
                CurrentTask = currentTask
            };

            var message = new JellystatWebsocketMessage<Models.WebSocket.TaskQueueUpdate>()
            {
                Type = WebsocketServerTypes.TaskUpdate,
                Data = update
            };


            await Global.WebSocketServerService.BroadcastObjectAsync(message, serverId != GlobalTaskId ? serverId : null);
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
                                string serverName = scheduledTask.Server != null ? $" for server {scheduledTask.Server.Name}" : String.Empty;
                                _logger.LogInformation(
                                    $"Scheduled task {scheduledTask.TaskSettings.Task} triggered{serverName}. " +
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
                        (Server? server, TaskSettings taskSettings) = taskItem;

                        string serverName = server != null ? $"for server {server.Name}" : String.Empty;
                        _logger.LogInformation($"Processing task {taskSettings.Task} {serverName}");

                        // Mark current running task for this server
                        string taskId = server?.Id ?? GlobalTaskId;
                        try
                        {

                            _currentRunningTasks[taskId] = taskSettings;
                        }
                        catch
                        {
                            _currentRunningTasks.AddOrUpdate(taskId, taskSettings, (k, v) => taskSettings);
                        }

                        try
                        {
                            await ExecuteTaskAsync(server, taskSettings);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error executing task {taskSettings.Task} {serverName}");
                        }
                        finally
                        {
                            _currentRunningTasks.TryRemove(taskId, out _);
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

        private async Task ExecuteTaskAsync(Server? server, TaskSettings taskSettings)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (server == null)
                {
                    switch (taskSettings.Task)
                    {
                        case TaskTypes.Backup:
                            _logger.LogInformation($"Executing Backup task");
                            await ExecuteBackupTask();
                            break;

                        case TaskTypes.Restore:
                            _logger.LogInformation($"Executing Restore task for server");
                            await ExecuteRestoreTask(taskSettings.Arguments.FirstOrDefault() ?? String.Empty);
                            break;

                        default:
                            _logger.LogWarning($"Unknown task type: {taskSettings.Task}");
                            break;
                    }
                    return;
                }
                else
                {
                    var apiInstanceManager = _serviceProvider.GetRequiredService<IAPIInstanceManager>();
                    iAPI? api = apiInstanceManager.GetInstance(server.Id);
                    if (api == null)
                    {
                        return;
                    }

                    switch (taskSettings.Task)
                    {
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
                }


                stopwatch.Stop();
                _logger.LogInformation($"Task {taskSettings.Task} completed for server {server.Name} in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                string serverName = server != null ? $" for server {server.Name}" : String.Empty;
                _logger.LogError(ex, $"Task {taskSettings.Task} failed{serverName} after {stopwatch.ElapsedMilliseconds}ms");
                throw;
            }
        }

        private async Task ExecuteBackupTask()
        {
            BackupTask backup = new BackupTask(_serviceScopeFactory);
            CancellationToken cancellationToken = new CancellationToken();
            await backup.RunBackupAsync(cancellationToken);
        }

        private async Task ExecutePlaybackReportingPluginSyncTask(Server server)
        {
            _logger.LogInformation($"PlaybackReportingPluginSync task logic not yet implemented for server {server.Name}");
            await Task.CompletedTask;
        }

        private async Task ExecuteRestoreTask(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogWarning("Restore task skipped due to missing file path.");
                return;
            }
            RestoreTask restore = new RestoreTask(_serviceScopeFactory);
            CancellationToken cancellationToken = new CancellationToken();
            await restore.RunRestoreAsync(filePath, cancellationToken);
        }
    }
}
