using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class ActivityMonitor
    {
        public iAPI api { get; set; }

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private readonly object _lock = new();

        public int IntervalMilliseconds { get; set; } = 1000;
        public bool IsRunning => _monitoringTask != null && !_monitoringTask.IsCompleted;

        private List<ActivityStaging> watchdogData { get; set; } = [];
        private List<Data.Schema.Activity> latestActivityCache { get; set; } = [];

        public ActivityMonitor(iAPI api, int intervalMilliseconds = 1000)
        {
            this.api = api;
            this.IntervalMilliseconds = intervalMilliseconds;
            initDataAsync().Wait();


        }

        public async Task initDataAsync()
        {

            watchdogData = await Global.Database.context.ActivityStaging.Where(a => a.ServerId == api.config.Id).ToListAsync();
        }

        public void Start()
        {
            lock (_lock)
            {
                if (IsRunning)
                    return;

                _cancellationTokenSource = new CancellationTokenSource();
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

                _cancellationTokenSource.Cancel();
                taskToWait = _monitoringTask;
            }

            try
            {
                await taskToWait;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_lock)
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    _monitoringTask = null;
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
            List<ExcludedUsers> excludedUsers = api.excludedUsers;
            List<BaseSessionItem> sessionData = api.GetSessionData().Where(s => !excludedUsers.Any(eu => eu.UserId == s.UserId && eu.ServerId == s.ServerId)).ToList();

            if (sessionData.IsEmpty() && watchdogData.IsEmpty())
            {
                return;
            }
            List<BaseSessionItem> dataToInsert = GetSessionsNotInWatchDog(sessionData, watchdogData);
            List<ActivityStaging> dataToRemove = GetWatchDogNotInSessions(sessionData, watchdogData);

            if (dataToInsert.IsNotEmpty())
            {

                await Global.Database.context.BulkInsertOrUpdateAsync(dataToInsert.Select(s => new ActivityStaging
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
                    MediaStreams = s.NowPlayingItem!.MediaStreams,
                    TranscodingInfo = s.TranscodingInfo,
                    Container = s.NowPlayingItem!.Container,
                    IpAddress = s.IPAddress,
                    IsPaused = s.IsPaused,
                    RuntimeTicks = s.NowPlayingItem!.RuntimeTicks

                }));

                await Global.Database.context.SaveChangesAsync();


            }


            int updateCount = await UpdateWatchdogDataAsync(sessionData);


            // Update duration for items that were playing before stopped (duration is only updated on pause)
            DateTime endTime = DateTime.UtcNow;
            foreach (ActivityStaging playback in dataToRemove)
            {
                if (!playback.IsPaused)
                {
                    int diffInSeconds = (int)Math.Round((endTime - playback.DateCreated).TotalSeconds, MidpointRounding.AwayFromZero);
                    playback.Duration += Math.Abs(diffInSeconds);
                    playback.DateCreated = endTime;
                }
            }


            List<ActivityStaging> playbackToInsert = dataToRemove.Where(d => d.Duration > Global.MinimumDurationThreshold).ToList();



            //Do update existing from Activity logic here

            List<Data.Schema.Activity> ExistingRecords = await GetLatestActivity();

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
                    await Global.Database.context.SaveChangesAsync();
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
                await Global.Database.context.BulkInsertOrUpdateAsync(playbackToInsert.Select(s => new Data.Schema.Activity
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
                    MediaStreams = s.MediaStreams,
                    TranscodingInfo = s.TranscodingInfo,
                    Container = s.Container,
                    IpAddress = s.IpAddress,
                    RuntimeTicks = s.RuntimeTicks

                }));

                await Global.Database.context.SaveChangesAsync();

            }

            if (dataToRemove.IsNotEmpty())
            {
                await Global.Database.context.BulkDeleteAsync(dataToRemove);
            }


            if (dataToRemove.IsNotEmpty() || dataToInsert.IsNotEmpty())
            {
                watchdogData = await Global.Database.context.ActivityStaging.Where(a => a.ServerId == api.config.Id).ToListAsync();
            }

            if (dataToRemove.IsNotEmpty() || dataToInsert.IsNotEmpty() || updateCount > 0)
            {
                Debug.WriteLine("[ActivityMonitor] Monitoring task completed. Sessions: " + sessionData.Count + ", Watchdog: " + watchdogData.Count + ", To Insert: " + dataToInsert.Count + ", To Update: " + updateCount + ", To Remove: " + dataToRemove.Count);
            }


            // Implement your monitoring logic here
            await Task.CompletedTask;
        }

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

        private async Task<int> UpdateWatchdogDataAsync(List<BaseSessionItem> sessionData)
        {
            int changesCount = 0;

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

                    changesCount++;
                }
            }

            if (changesCount > 0)
            {
                try
                {
                    await Global.Database.context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    Debug.WriteLine($"[ActivityMonitor] Concurrency exception updating watchdog data: {ex.Message}. Records may have been modified/deleted by another process. Continuing...");
                    foreach (var entry in ex.Entries)
                    {
                        entry.State = EntityState.Detached;
                    }
                }
            }
            return changesCount;
        }

        private List<ActivityStaging> GetWatchDogNotInSessions(List<BaseSessionItem> sessionData, List<ActivityStaging> watchdogData)
        {
            if (sessionData.IsEmpty()) return watchdogData;

            var sessionKeys = sessionData.Select(s => (s.UserId, s.DeviceId, s.NowPlayingItem!.Id)).ToHashSet();
            return watchdogData.Where(wd => !sessionKeys.Contains((wd.UserId, wd.DeviceId, wd.ItemId))).ToList();
        }

        public async Task<List<Data.Schema.Activity>> GetLatestActivity(bool refresh = false)
        {

            if (!refresh && latestActivityCache.IsNotEmpty() && !latestActivityCache.Any(a => a.DateCreated > DateTime.UtcNow.AddHours(-Global.NewWatchThresholdHours)))
            {
                return latestActivityCache;
            }

            var cutoffTime = DateTime.UtcNow.AddHours(-Global.NewWatchThresholdHours);

            List<Data.Schema.Activity> results = await Global.Database.context.Activity
            .Where(a => a.DateCreated > cutoffTime && a.ServerId == api.config.Id && a.RuntimeTicks > 0)
            .GroupBy(a => new { a.UserId, a.ItemId })
            .Select(g => g.OrderByDescending(a => a.DateCreated).First())
            .ToListAsync();

            latestActivityCache = results.Where(a => a.CompletionPercentage < Global.CompletionThreshold).OrderByDescending(a => a.DateCreated).ToList();
            return results;

        }

        public async Task BroadcastSessions()
        {
            IList<BaseSessionItem> sessionsItems = api.GetSessionData();


            if (sessionsItems.IsNotEmpty())
            {
                await Global.WebSocketServerService.BroadcastObjectAsync(sessionsItems, api.config.Id);
            }
        }
    }
}
