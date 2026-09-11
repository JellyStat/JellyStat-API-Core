using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Jellystat_API_Core.Services.TaskLogging
{
    public class TaskLogging : ITaskLogging, IAsyncDisposable
    {
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<TaskLogging> _logger;
        private readonly ConcurrentDictionary<string, PendingLogState> _pendingLogs = new();

        public TaskLogging(IServiceScopeFactory serviceScopeFactory, ILogger<TaskLogging> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        public async Task<Logs> CreateLogAsync(TaskTypes taskType, string? serverId = null, CancellationToken cancellationToken = default)
        {
            Logs log = new()
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = serverId,
                Task = taskType,
                DateCreated = DateTime.UtcNow,
                Messages = new List<string>()
            };

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            context.Logs.Add(log);
            await context.SaveChangesAsync(cancellationToken);

            _pendingLogs[log.Id] = new PendingLogState(log);

            var message = new JellystatWebsocketMessage<Logs>()
            {
                Type = WebsocketServerTypes.TaskStart,
                Data = log
            };

            await Global.WebSocketServerService.BroadcastObjectAsync(message, serverId);

            return log;
        }

        public Task AddMessageAsync(Logs log, string message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(log);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            cancellationToken.ThrowIfCancellationRequested();

            PendingLogState state = GetOrCreateState(log);

            lock (state.SyncRoot)
            {
                state.Log.Messages.Add(message);
            }
            Debug.WriteLine(message);

            ScheduleDebouncedFlush(state);

            return Task.CompletedTask;
        }

        public Task SuccessAsync(Logs log, CancellationToken cancellationToken = default)
        {
            return CompleteAsync(log, true, cancellationToken);
        }

        public Task ErrorAsync(Logs log, CancellationToken cancellationToken = default)
        {
            return CompleteAsync(log, false, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (PendingLogState state in _pendingLogs.Values)
            {
                try
                {
                    state.Stopwatch.Stop();
                    lock (state.SyncRoot)
                    {
                        if (state.Log.Success == null)
                        {
                            state.Log.Duration = (float)state.Stopwatch.Elapsed.TotalSeconds;
                        }
                    }

                    await FlushAsync(state, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to flush task log {LogId} during disposal.", state.Log.Id);
                }
                finally
                {
                    state.DisposeCancellation();
                }
            }

            _pendingLogs.Clear();
        }

        private async Task CompleteAsync(Logs log, bool success, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(log);

            PendingLogState state = GetOrCreateState(log);

            state.Stopwatch.Stop();
            state.DisposeCancellation();

            lock (state.SyncRoot)
            {
                state.Log.Success = success;
                state.Log.Duration = (float)state.Stopwatch.Elapsed.TotalSeconds;
            }

            var message = new JellystatWebsocketMessage<Logs>()
            {
                Type = WebsocketServerTypes.TaskLogUpdate,
                Data = state.Log
            };

            await Global.WebSocketServerService.BroadcastObjectAsync(message, state.Log.ServerId);

            await FlushAsync(state, cancellationToken);

            _pendingLogs.TryRemove(state.Log.Id, out _);
            state.DisposeCancellation();
        }

        private PendingLogState GetOrCreateState(Logs log)
        {
            return _pendingLogs.GetOrAdd(log.Id, _ => new PendingLogState(log));
        }

        private void ScheduleDebouncedFlush(PendingLogState state)
        {
            CancellationTokenSource cancellationTokenSource = new();
            CancellationTokenSource? previousCancellation = Interlocked.Exchange(ref state.DebounceCancellation, cancellationTokenSource);

            previousCancellation?.Cancel();
            previousCancellation?.Dispose();

            _ = DebouncedFlushAsync(state, cancellationTokenSource.Token);
        }

        private async Task DebouncedFlushAsync(PendingLogState state, CancellationToken cancellationToken)
        {
            try
            {
                var wsmessage = new JellystatWebsocketMessage<Logs>()
                {
                    Type = WebsocketServerTypes.TaskLogUpdate,
                    Data = state.Log
                };

                await Global.WebSocketServerService.BroadcastObjectAsync(wsmessage, state.Log.ServerId);

                await Task.Delay(DebounceDelay, cancellationToken);
                await FlushAsync(state, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush debounced task log {LogId}.", state.Log.Id);
            }
        }

        private async Task FlushAsync(PendingLogState state, CancellationToken cancellationToken)
        {
            await state.FlushLock.WaitAsync(cancellationToken);

            try
            {
                Logs snapshot;

                lock (state.SyncRoot)
                {
                    snapshot = new Logs
                    {
                        Id = state.Log.Id,
                        ServerId = state.Log.ServerId,
                        Task = state.Log.Task,
                        DateCreated = state.Log.DateCreated,
                        Duration = state.Log.Duration,
                        Success = state.Log.Success,
                        Messages = new List<string>(state.Log.Messages)
                    };
                }

                await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
                ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                Logs? databaseLog = await context.Logs.FirstOrDefaultAsync(l => l.Id == snapshot.Id, cancellationToken);
                if (databaseLog == null)
                {
                    context.Logs.Add(snapshot);
                }
                else
                {
                    databaseLog.Messages = snapshot.Messages;
                    databaseLog.Success = snapshot.Success;
                    databaseLog.Duration = snapshot.Duration;
                }

                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskLogging] Error on FlushAsync: {ex.Message}");
            }
            finally
            {
                state.FlushLock.Release();
            }
        }

        private sealed class PendingLogState
        {
            public PendingLogState(Logs log)
            {
                Log = log;
                Stopwatch = Stopwatch.StartNew();
            }

            public Logs Log { get; }
            public Stopwatch Stopwatch { get; }
            public SemaphoreSlim FlushLock { get; } = new(1, 1);
            public object SyncRoot { get; } = new();
            public CancellationTokenSource? DebounceCancellation;

            public void DisposeCancellation()
            {
                CancellationTokenSource? cancellation = Interlocked.Exchange(ref DebounceCancellation, null);
                if (cancellation == null)
                {
                    return;
                }

                cancellation.Cancel();
                cancellation.Dispose();
            }
        }
    }
}
