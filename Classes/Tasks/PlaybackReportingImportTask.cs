using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Services.TaskLogging;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Activity = Jellystat_API_Core.Data.Schema.Activity;

namespace Jellystat_API_Core.Classes.Tasks
{
    /// <summary>
    /// Imports playback history from the Playback Reporting plugin into the Activity table.
    ///
    /// Unlike v1, rows are not filtered on the library: a row is imported whether or not its item still exists and
    /// whether or not it overlaps activity Jellystat tracked itself. The one exception is the user: rows for users
    /// not in the Users table and rows with no item id are skipped.
    /// Imported rows are flagged Imported = true so they can be reviewed afterwards (unlinked rows show up in
    /// Activity Migration; anything unwanted can be deleted).
    ///
    /// Every run reads the whole plugin history. Each plugin row maps to a fixed Activity Id (server + rowid), so rows
    /// not in Activity are inserted (including ones deleted since the last run) and rows already there get the
    /// plugin's own fields refreshed. Item links, user names and item names are left alone so edits made in
    /// Activity Migration and by syncs survive a re-import.
    /// </summary>
    public class PlaybackReportingImportTask : BaseTask
    {
        private const int BatchSize = 5000;

        // Configuration file names the plugin registers under (Emby, Jellyfin), same check as v1
        private static readonly string[] PluginConfigurationFileNames = ["playback_reporting.xml", "Jellyfin.Plugin.PlaybackReporting.xml"];

        // Jellyfin's plugin stores episodes as "Series Name - s01e02 - Episode Name"
        private static readonly Regex EpisodeNamePattern = new(@"^(?<series>.+?) - s(?<season>\d+)e(?<episode>\d+) - (?<name>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] DateFormats =
        [
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
            "yyyy-MM-ddTHH:mm:ss",
        ];

        private readonly iAPI api;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public PlaybackReportingImportTask(iAPI api, IServiceScopeFactory serviceScopeFactory)
        {
            this.api = api;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                Debug.WriteLine($"Playback Reporting import already running for server {api.config.Name}, skipping.");
                return;
            }

            IsRunning = true;

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var loggingService = scope.ServiceProvider.GetRequiredService<ITaskLogging>();
                Logs log = await loggingService.CreateLogAsync(TaskTypes.PlaybackReportingPluginSync, api.config.Id);

                try
                {
                    await ImportAsync(context, log, loggingService, cancellationToken);
                    await loggingService.SuccessAsync(log);
                }
                catch (OperationCanceledException)
                {
                    await loggingService.AddMessageAsync(log, "Playback Reporting import cancelled. Batches already processed were kept.");
                    await loggingService.ErrorAsync(log);
                }
                catch (Exception ex)
                {
                    await loggingService.AddMessageAsync(log, $"Error running Playback Reporting import: {ex.Message}");
                    await loggingService.ErrorAsync(log);
                    Debug.WriteLine($"Error running Playback Reporting import: {ex}");
                }
                finally
                {
                    context.ChangeTracker.Clear();
                }
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ImportAsync(ApplicationDbContext context, Logs log, ITaskLogging loggingService, CancellationToken cancellationToken)
        {
            string serverId = api.config.Id;

            await loggingService.AddMessageAsync(log, $"Starting Playback Reporting import for server {api.config.Name}");

            IList<BasePlugin> plugins = await api.GetInstalledPlugins();
            bool hasPlugin = plugins.Any(p => p.ConfigurationFileName != null && PluginConfigurationFileNames.Contains(p.ConfigurationFileName));
            if (!hasPlugin)
            {
                throw new InvalidOperationException("The Playback Reporting plugin was not found on this server. Install it on the server, then run the import again.");
            }

            // Only used to page through the plugin table; every run starts at the first row
            long lastRowId = 0;

            // Queries run with ReplaceUserId off, so rows carry user ids
            Dictionary<string, Users> knownUsers = await context.Users
                .Where(u => u.ServerId == serverId)
                .ToDictionaryAsync(u => u.Id, cancellationToken);

            var totals = new ImportTotals();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string query = "SELECT rowid, DateCreated, UserId, ItemId, ItemType, ItemName, PlaybackMethod, ClientName, DeviceName, PlayDuration " +
                               $"FROM PlaybackActivity WHERE rowid > {lastRowId} ORDER BY rowid LIMIT {BatchSize}";

                List<Dictionary<string, string?>>? rows = await api.SubmitPlaybackReportingQuery(query, cancellationToken);

                if (rows == null)
                {
                    throw new InvalidOperationException("The Playback Reporting plugin was not found on this server. Install it on the server, then run the import again.");
                }

                if (rows.Count == 0)
                {
                    break;
                }

                List<PluginRow> batch = new(rows.Count);
                long batchMaxRowId = lastRowId;
                foreach (Dictionary<string, string?> row in rows)
                {
                    if (long.TryParse(row.GetValueOrDefault("rowid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long rawRowId))
                    {
                        batchMaxRowId = Math.Max(batchMaxRowId, rawRowId);
                    }

                    PluginRow? parsed = PluginRow.Parse(row);
                    if (parsed == null)
                    {
                        // Only possible without a rowid, which SQLite always provides
                        totals.NoRowId++;
                        continue;
                    }
                    if (parsed.ItemId.Length == 0)
                    {
                        totals.NoItemId++;
                        continue;
                    }
                    if (!parsed.HasValidDate) totals.BadDate++;
                    batch.Add(parsed);
                }

                List<Activity> activities = await BuildActivitiesAsync(context, batch, knownUsers, totals, cancellationToken);

                // Counted only for the log; the upsert below handles both cases
                List<string> ids = activities.Select(a => a.Id).ToList();
                int existingCount = await context.Activity.CountAsync(a => ids.Contains(a.Id), cancellationToken);

                if (activities.Count > 0)
                {
                    // Insert new rows; on existing rows refresh only what the plugin owns
                    await context.BulkInsertOrUpdateAsync(activities, new BulkConfig
                    {
                        PropertiesToIncludeOnUpdate =
                        [
                            nameof(Activity.DateCreated),
                            nameof(Activity.Duration),
                            nameof(Activity.Client),
                            nameof(Activity.Device),
                            nameof(Activity.PlayMethod),
                            nameof(Activity.DirectPlay),
                        ],
                    }, cancellationToken: cancellationToken);
                }

                totals.Updated += existingCount;
                totals.Imported += activities.Count - existingCount;
                totals.Read += rows.Count;
                lastRowId = batchMaxRowId;

                if (rows.Count < BatchSize)
                {
                    break;
                }
            }

            if (totals.Read == 0)
            {
                await loggingService.AddMessageAsync(log, "The Playback Reporting plugin has no data to import.");
                return;
            }

            await loggingService.AddMessageAsync(log, $"Read {totals.Read} row(s) for server {api.config.Name}: {totals.Imported} added, {totals.Updated} already in Activity and updated");
            if (totals.UnknownUser > 0)
            {
                await loggingService.AddMessageAsync(log, $"{totals.UnknownUser} row(s) skipped (unknown user)");
            }
            if (totals.NotInLibrary > 0)
            {
                await loggingService.AddMessageAsync(log, $"{totals.NotInLibrary} row(s) point at items not in the library; link or delete them in Activity Migration");
            }
            if (totals.NoItemId > 0)
            {
                await loggingService.AddMessageAsync(log, $"{totals.NoItemId} row(s) were skipped because they have no item id");
            }
            if (totals.BadDate > 0)
            {
                await loggingService.AddMessageAsync(log, $"{totals.BadDate} rows have an unreadable date in the plugin and were imported dated 1970-01-01");
            }
            if (totals.NoRowId > 0)
            {
                await loggingService.AddMessageAsync(log, $"{totals.NoRowId} row(s) had no rowid and could not be imported");
            }
        }

        private async Task<List<Activity>> BuildActivitiesAsync(ApplicationDbContext context, List<PluginRow> batch, Dictionary<string, Users> knownUsers, ImportTotals totals, CancellationToken cancellationToken)
        {
            string serverId = api.config.Id;
            List<string> itemIds = batch.Select(r => r.ItemId).Distinct().ToList();

            var items = await context.Items
                .AsNoTracking()
                .Where(i => i.ServerId == serverId && itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Type, i.Duration, i.ParentId })
                .ToDictionaryAsync(i => i.Id, cancellationToken);

            List<string> episodeIds = items.Values.Where(i => i.Type == ItemTypes.Episode).Select(i => i.Id).ToList();

            // Series and season above each episode, from the ItemsPath closure table
            var ancestors = await (from path in context.ItemsPath
                                   where episodeIds.Contains(path.DescendantId) && path.Depth > 0
                                   join ancestor in context.Items on path.AncestorId equals ancestor.Id
                                   where ancestor.ServerId == serverId && (ancestor.Type == ItemTypes.Series || ancestor.Type == ItemTypes.Season)
                                   select new { EpisodeId = path.DescendantId, ancestor.Id, ancestor.Name, ancestor.Type })
                                  .ToListAsync(cancellationToken);

            var seriesByEpisode = ancestors.Where(a => a.Type == ItemTypes.Series)
                .GroupBy(a => a.EpisodeId)
                .ToDictionary(g => g.Key, g => g.First());
            var seasonByEpisode = ancestors.Where(a => a.Type == ItemTypes.Season)
                .GroupBy(a => a.EpisodeId)
                .ToDictionary(g => g.Key, g => g.First().Id);

            List<Activity> activities = new(batch.Count);

            foreach (PluginRow row in batch)
            {
                // Only import activity for users Jellystat knows about
                if (row.UserId == null || !knownUsers.TryGetValue(row.UserId, out Users? user))
                {
                    totals.UnknownUser++;
                    continue;
                }

                var activity = new Activity
                {
                    Id = DeterministicActivityId(serverId, row.RowId),
                    ServerId = serverId,
                    UserId = user.Id,
                    UserName = user.Username,
                    Client = row.ClientName,
                    Device = row.DeviceName,
                    DeviceId = null,
                    ItemId = row.ItemId,
                    Name = row.ItemName ?? row.ItemId,
                    Duration = row.PlayDuration,
                    PlayMethod = row.PlayMethod,
                    DirectPlay = row.PlayMethod == "DirectPlay",
                    MediaStreams = [],
                    TranscodingInfo = null,
                    Imported = true,
                    DateCreated = row.DateCreated,
                    IpAddress = string.Empty,
                };

                if (items.TryGetValue(row.ItemId, out var item))
                {
                    activity.Name = item.Name;
                    activity.RuntimeTicks = item.Duration;

                    if (item.Type == ItemTypes.Episode)
                    {
                        if (seriesByEpisode.TryGetValue(item.Id, out var series))
                        {
                            activity.SeriesId = series.Id;
                            activity.SeriesName = series.Name;
                        }
                        activity.SeasonId = seasonByEpisode.TryGetValue(item.Id, out string? seasonId) ? seasonId : item.ParentId;
                    }
                }
                else
                {
                    totals.NotInLibrary++;

                    // Keep series/episode names apart so Activity Migration can group the episodes under their series
                    if (string.Equals(row.ItemType, "Episode", StringComparison.OrdinalIgnoreCase) && row.ItemName != null)
                    {
                        Match match = EpisodeNamePattern.Match(row.ItemName);
                        if (match.Success)
                        {
                            activity.SeriesName = match.Groups["series"].Value.Trim();
                            activity.Name = match.Groups["name"].Value.Trim();
                        }
                    }
                }

                activities.Add(activity);
            }

            return activities;
        }

        /// <summary>A stable, UUID-formatted Activity Id for a plugin row, so re-imports update the row instead of duplicating it.</summary>
        private static string DeterministicActivityId(string serverId, long rowId)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes($"jellystat:playback-reporting:{serverId}:{rowId}"));
            byte[] bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 (name-based, SHA-1)
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
            return new Guid(bytes, bigEndian: true).ToString();
        }

        private sealed class ImportTotals
        {
            public int Read;
            public int Imported;
            public int Updated;
            public int NotInLibrary;
            public int UnknownUser;
            public int NoItemId;
            public int BadDate;
            public int NoRowId;
        }

        private sealed class PluginRow
        {
            public long RowId { get; init; }
            public DateTime DateCreated { get; init; }
            public bool HasValidDate { get; init; }
            public string? UserId { get; init; }
            public required string ItemId { get; init; }
            public string? ItemType { get; init; }
            public string? ItemName { get; init; }
            public required string PlayMethod { get; init; }
            public string? ClientName { get; init; }
            public string? DeviceName { get; init; }
            public long PlayDuration { get; init; }

            /// <summary>
            /// Reads columns by name: rowid, DateCreated, UserId, ItemId, ItemType, ItemName, PlaybackMethod, ClientName,
            /// DeviceName, PlayDuration.
            /// A missing item gives an empty ItemId (the caller skips those rows); an unreadable date becomes 1970-01-01
            /// so those rows are easy to find and delete. Returns null only without a rowid (the row's identity).
            /// </summary>
            public static PluginRow? Parse(Dictionary<string, string?> row)
            {
                if (!long.TryParse(row.GetValueOrDefault("rowid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long rowId)) return null;
                bool hasValidDate = TryParseDate(row.GetValueOrDefault("DateCreated"), out DateTime dateCreated);
                if (!hasValidDate) dateCreated = DateTime.UnixEpoch;

                long.TryParse(row.GetValueOrDefault("PlayDuration"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long duration);

                return new PluginRow
                {
                    RowId = rowId,
                    DateCreated = dateCreated,
                    HasValidDate = hasValidDate,
                    UserId = NullIfEmpty(row.GetValueOrDefault("UserId")),
                    ItemId = row.GetValueOrDefault("ItemId")?.Trim() ?? string.Empty,
                    ItemType = NullIfEmpty(row.GetValueOrDefault("ItemType")),
                    ItemName = NullIfEmpty(row.GetValueOrDefault("ItemName")),
                    PlayMethod = NormalizePlayMethod(row.GetValueOrDefault("PlaybackMethod")),
                    ClientName = NullIfEmpty(row.GetValueOrDefault("ClientName")),
                    DeviceName = NullIfEmpty(row.GetValueOrDefault("DeviceName")),
                    PlayDuration = Math.Max(0, duration),
                };
            }

            // The plugin writes server-local time with no offset. Read it as this process's local time
            // (set TZ to match the media server) and store it as UTC.
            private static bool TryParseDate(string? value, out DateTime result)
            {
                result = default;
                if (string.IsNullOrWhiteSpace(value)) return false;

                const DateTimeStyles styles = DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal;
                if (DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, styles, out result)
                    || DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, styles, out result))
                {
                    result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
                    return true;
                }
                return false;
            }

            // "Transcode (v:h264 a:aac)" -> "Transcode"
            private static string NormalizePlayMethod(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return "DirectPlay";
                int bracket = value.IndexOf('(');
                string method = (bracket > 0 ? value[..bracket] : value).Trim();
                return method.Length == 0 ? "DirectPlay" : method;
            }

            private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
