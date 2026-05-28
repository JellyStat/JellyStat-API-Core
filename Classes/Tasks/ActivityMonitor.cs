using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Models.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class ActivityMonitor : BaseTask
    {
        public iAPI api { get; set; }

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private readonly object _lock = new();
        private volatile bool _isStopping = false;

        public int IntervalMilliseconds { get; set; } = 1000;
        public override bool IsRunning => _monitoringTask != null && !_monitoringTask.IsCompleted;

        private List<ActivityStaging> watchdogData { get; set; } = [];
        private IList<BaseSessionItem> LastSessionData = [];

        private IServiceScopeFactory _serviceScopeFactory { get; set; }

        public ActivityMonitor(iAPI api, IServiceScopeFactory serviceScopeFactory, int intervalMilliseconds = 1000)
        {
            this.api = api;
            this.IntervalMilliseconds = intervalMilliseconds;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (IsRunning || _isStopping)
                    return;

                _cancellationTokenSource = new CancellationTokenSource();
                watchdogData = [];
                _monitoringTask = Task.Run(() => MonitoringLoopAsync(_cancellationTokenSource.Token));
            }
        }

        public async Task StopAsync()
        {
            Task? taskToWait;
            lock (_lock)
            {
                if (_cancellationTokenSource == null || _monitoringTask == null)
                    return;

                _isStopping = true;
                _cancellationTokenSource.Cancel();
                taskToWait = _monitoringTask;
            }

            List<ActivityStaging> dataToFlush = new List<ActivityStaging>();

            try
            {
                try
                {
                    await taskToWait;
                }
                catch (OperationCanceledException)
                {
                    // monitoring task was cancelled - continue to flush data
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ActivityMonitor] StopAsync: monitoring task faulted: {ex.Message}");
                }

                lock (_lock)
                {
                    dataToFlush = watchdogData.ToList();
                    watchdogData.Clear();
                }

                if (dataToFlush.IsNotEmpty())
                {
                    await ProcessActivityData(dataToFlush);
                }
            }
            finally
            {
                lock (_lock)
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    _monitoringTask = null;
                    _isStopping = false;
                }
            }
        }

        private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ExecuteMonitoringTaskAsync();
                    await BroadcastSessions();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ActivityMonitor] ERROR in ExecuteMonitoringTaskAsync: {ex.Message}");
                    Debug.WriteLine($"[ActivityMonitor] StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"[ActivityMonitor] Inner Exception: {ex.InnerException.Message}");
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(IntervalMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ExecuteMonitoringTaskAsync()
        {


            List<ActivityStaging> dataToRemove = ProcessWatchDogData();

            await ProcessActivityData(dataToRemove);

        }

        ///////////////////
        private List<ActivityStaging> ProcessWatchDogData()
        {
            List<ExcludedUsers> excludedUsers = api.excludedUsers;
            List<BaseSessionItem> sessionData = api.GetSessionData().Where(s => !excludedUsers.Any(eu => eu.UserId == s.UserId && eu.ServerId == s.ServerId)).ToList();

            if (sessionData.IsEmpty() && watchdogData.IsEmpty())
            {
                return new List<ActivityStaging>();
            }
            List<BaseSessionItem> dataToInsert = GetSessionsNotInWatchDog(sessionData, watchdogData);
            List<ActivityStaging> dataToRemove = GetWatchDogNotInSessions(sessionData, watchdogData);

            if (dataToInsert.IsNotEmpty())
            {

                watchdogData.AddRange(dataToInsert.Select(s => new ActivityStaging
                {
                    ServerId = s.ServerId,
                    Name = s.NowPlayingItem!.Name,
                    SeriesName = s.NowPlayingItem!.SeriesName,
                    UserId = s.UserId,
                    UserName = s.UserName,
                    Client = s.Client,
                    Device = s.DeviceName,
                    DeviceId = s.DeviceId,
                    ItemId = s.NowPlayingItem!.Id,
                    SeriesId = s.NowPlayingItem!.SeriesId,
                    SeasonId = s.NowPlayingItem!.SeasonId,
                    Duration = 0,
                    DirectPlay = s.IsDirectPlay,
                    PlayMethod = s.PlayMethod,
                    MediaStreams = s.NowPlayingItem!.MediaStreams,
                    TranscodingInfo = s.TranscodingInfo,
                    Container = s.NowPlayingItem!.Container,
                    IpAddress = s.IPAddress,
                    IsPaused = s.IsPaused,
                    RuntimeTicks = s.NowPlayingItem!.RuntimeTicks

                }).ToList());

            }


            UpdateWatchdogData(sessionData);

            return dataToRemove;
        }


        private async Task ProcessActivityData(List<ActivityStaging> activityData)
        {
            if (activityData.IsEmpty()) return;
            using (var scope = _serviceScopeFactory.CreateScope())
            {

                ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Update duration for items that were playing before stopped (duration is only updated on pause)
                DateTime endTime = DateTime.UtcNow;
                foreach (ActivityStaging playback in activityData)
                {
                    if (!playback.IsPaused)
                    {
                        int diffInSeconds = (int)Math.Round((endTime - playback.DateCreated).TotalSeconds, MidpointRounding.AwayFromZero);
                        playback.Duration += Math.Abs(diffInSeconds);
                        playback.DateCreated = endTime;
                    }
                }

                List<ActivityStaging> playbackToInsert = activityData.Where(d => d.Duration > Global.MinimumDurationThreshold).ToList();

                //Do update existing from Activity logic here

                List<Data.Schema.Activity> ExistingRecords = await GetLatestActivity(context);

                // Create lookup for O(1) access instead of O(n) with Any()
                var playbackLookup = playbackToInsert.ToLookup(d => (d.ServerId, d.UserId, d.ItemId));

                List<Data.Schema.Activity> ExistingRecordsToUpdate = ExistingRecords.Where(er =>
                {
                    var key = (er.ServerId, er.UserId, er.ItemId);
                    return playbackLookup.Contains(key) &&
                           playbackLookup[key].Any(d => (d.Duration + er.Duration).ToTicks() <= d.RuntimeTicks);
                }).ToList();

                if (ExistingRecordsToUpdate.IsNotEmpty())
                {
                    foreach (Data.Schema.Activity record in ExistingRecordsToUpdate)
                    {
                        ActivityStaging? matchingStaging = playbackToInsert.FirstOrDefault(p => p.ServerId == record.ServerId && p.UserId == record.UserId && p.ItemId == record.ItemId);
                        if (matchingStaging != null)
                        {
                            record.Duration = record.Duration + matchingStaging.Duration;
                            record.DateCreated = matchingStaging.DateCreated;
                        }
                    }

                    try
                    {
                        await context.SaveChangesAsync();
                        playbackToInsert = playbackToInsert.Where(p => !ExistingRecordsToUpdate.Any(er => er.ServerId == p.ServerId && er.UserId == p.UserId && er.ItemId == p.ItemId)).ToList();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        Debug.WriteLine($"[ActivityMonitor] Concurrency exception updating existing records: {ex.Message}. Records may have been modified/deleted by another process. Continuing...");
                        foreach (var entry in ex.Entries)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }

                //

                if (playbackToInsert.IsNotEmpty())
                {
                    await context.BulkInsertOrUpdateAsync(playbackToInsert.Select(s => new Data.Schema.Activity
                    {
                        ServerId = s.ServerId,
                        Name = s.Name,
                        SeriesName = s.SeriesName,
                        UserId = s.UserId,
                        UserName = s.UserName,
                        Client = s.Client,
                        Device = s.Device,
                        DeviceId = s.DeviceId,
                        ItemId = s.ItemId,
                        SeriesId = s.SeriesId,
                        SeasonId = s.SeasonId,
                        Duration = s.Duration,
                        DirectPlay = s.DirectPlay,
                        PlayMethod = s.PlayMethod,
                        MediaStreams = s.MediaStreams,
                        TranscodingInfo = s.TranscodingInfo,
                        Container = s.Container,
                        IpAddress = s.IpAddress,
                        RuntimeTicks = s.RuntimeTicks

                    }));

                    await context.SaveChangesAsync();

                }


                watchdogData = watchdogData.Where(wd => !activityData.Any(d => d.UserId == wd.UserId && d.DeviceId == wd.DeviceId && d.ItemId == wd.ItemId)).ToList();
            }
        }
        ///////////////////

        private List<BaseSessionItem> GetSessionsNotInWatchDog(List<BaseSessionItem> sessionData, List<ActivityStaging> watchdogData)
        {
            if (watchdogData.IsEmpty()) return sessionData;

            // Create HashSet for O(1) lookup instead of O(n) with Any()
            var watchdogKeys = watchdogData.Select(wd => (wd.UserId, wd.DeviceId, wd.ItemId)).ToHashSet();

            List<BaseSessionItem> newSessionData = sessionData.Where(s =>
            {
                var key = (s.UserId, s.DeviceId, s.NowPlayingItem!.Id);
                return !watchdogKeys.Contains(key);
            }).ToList();

            return newSessionData;
        }

        private void UpdateWatchdogData(List<BaseSessionItem> sessionData)
        {
            foreach (ActivityStaging item in watchdogData)
            {
                BaseSessionItem? sessionItem = sessionData.FirstOrDefault(s => s.UserId == item.UserId && s.DeviceId == item.DeviceId && s.NowPlayingItem!.Id == item.ItemId);
                if (sessionItem != null && sessionItem.IsPaused != item.IsPaused)
                {
                    item.IsPaused = sessionItem.IsPaused;
                    if (sessionItem.IsPaused)
                    {
                        DateTime startTime = item.DateCreated;
                        DateTime lastPaused = sessionItem.LastPausedDate ?? DateTime.UtcNow;
                        Debug.WriteLine($"[ActivityMonitor] Updating duration for paused item. User: {item.UserId}, Item: {item.ItemId}, StartTime: {startTime}, LastPaused: {lastPaused}");
                        int diffInSeconds = (int)Math.Round((lastPaused - startTime).TotalSeconds, MidpointRounding.AwayFromZero);
                        item.Duration = item.Duration + Math.Abs(diffInSeconds);
                        item.DateCreated = lastPaused;
                    }
                    else
                    {
                        item.DateCreated = DateTime.UtcNow;
                    }
                }
            }
        }

        private List<ActivityStaging> GetWatchDogNotInSessions(List<BaseSessionItem> sessionData, List<ActivityStaging> watchdogData)
        {
            if (sessionData.IsEmpty()) return watchdogData;

            var sessionKeys = sessionData.Select(s => (s.UserId, s.DeviceId, s.NowPlayingItem!.Id)).ToHashSet();
            return watchdogData.Where(wd => !sessionKeys.Contains((wd.UserId, wd.DeviceId, wd.ItemId))).ToList();
        }

        public async Task<List<Data.Schema.Activity>> GetLatestActivity(ApplicationDbContext context, bool refresh = false)
        {

            var cutoffTime = DateTime.UtcNow.AddHours(-Global.NewWatchThresholdHours);


            List<Data.Schema.Activity> results = await context.Activity
            .Where(a => a.DateCreated > cutoffTime && a.ServerId == api.config.Id && a.RuntimeTicks > 0)
            .GroupBy(a => new { a.UserId, a.ItemId })
            .Select(g => g.OrderByDescending(a => a.DateCreated).First())
            .ToListAsync();

            return results.Where(a => a.CompletionPercentage < Global.CompletionThreshold).OrderByDescending(a => a.DateCreated).ToList();
        }

        public async Task BroadcastSessions()
        {
            IList<BaseSessionItem> sessionsItems = api.GetSessionData();
            if (sessionsItems == LastSessionData)
            {
                return;
            }
            LastSessionData = sessionsItems;

            JellystatWebsocketMessage<IList<BaseSessionItem>> message = new JellystatWebsocketMessage<IList<BaseSessionItem>>()
            {
                Type = WebsocketServerTypes.Sessions,
                Data = sessionsItems
            };
            await Global.WebSocketServerService.BroadcastObjectAsync(message, api.config.Id);
        }
    }
}
