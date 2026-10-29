using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Microsoft.EntityFrameworkCore;
using T = Jellystat_API_Core.Services.Reports.Datasets.ReportFieldTypes;

namespace Jellystat_API_Core.Services.Reports.Datasets
{
    internal static class ReportScopeQueries
    {
        public static IQueryable<Activity> ScopedActivity(this ApplicationDbContext db, ReportScope scope)
        {
            var activity = db.Activity.Where(a => a.ServerId == scope.ServerId);
            return scope.ScopedUserId == null ? activity : activity.Where(a => a.UserId == scope.ScopedUserId);
        }
    }

    // ---------------------------------------------------------------- Playback activity

    public class ActivityReportRow
    {
        [ReportField("Activity Id", T.String, Groupable = false)] public string Id { get; set; } = "";
        [ReportField("Date", T.Date, Aggregatable = true)] public DateTime DateCreated { get; set; }
        [ReportField("User Id", T.String)] public string UserId { get; set; } = "";
        [ReportField("User", T.String)] public string UserName { get; set; } = "";
        [ReportField("Item Id", T.String)] public string ItemId { get; set; } = "";
        [ReportField("Title", T.String)] public string Name { get; set; } = "";
        [ReportField("Series", T.String)] public string? SeriesName { get; set; }
        [ReportField("Item Type", T.Enum)] public ItemTypes? ItemType { get; set; }
        [ReportField("Library Id", T.String)] public string? LibraryId { get; set; }
        [ReportField("Library", T.String)] public string? LibraryName { get; set; }
        [ReportField("Library Type", T.Enum)] public LibraryTypes? LibraryType { get; set; }
        [ReportField("Season #", T.Integer)] public int? SeasonNumber { get; set; }
        [ReportField("Episode #", T.Integer)] public int? EpisodeNumber { get; set; }
        [ReportField("Item Added", T.Date, Aggregatable = true)] public DateTime? ItemDateAdded { get; set; }
        [ReportField("Source Codec", T.String, Description = "Codec of the file on the server")] public string? SourceCodec { get; set; }
        [ReportField("Source Resolution", T.String)] public string? SourceResolution { get; set; }
        [ReportField("Client", T.String)] public string? Client { get; set; }
        [ReportField("Device", T.String)] public string? Device { get; set; }
        [ReportField("Play Method", T.String)] public string? PlayMethod { get; set; }
        [ReportField("Direct Play", T.Boolean)] public bool DirectPlay { get; set; }
        [ReportField("Container", T.String)] public string? Container { get; set; }
        [ReportField("IP Address", T.String)] public string? IpAddress { get; set; }
        [ReportField("Imported", T.Boolean)] public bool Imported { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long Duration { get; set; }
        [ReportField("Runtime", T.Duration, Groupable = false, Aggregatable = true)] public long Runtime { get; set; }
        [ReportField("Completion %", T.Number, Groupable = false, Aggregatable = true)] public double CompletionPercentage { get; set; }
    }

    public class ActivityReportDataset : ReportDataset<ActivityReportRow>
    {
        public override string Key => "activity";
        public override string Label => "Playback Activity";
        public override string Description => "One row per playback session, joined to its item and library.";

        protected override IQueryable<ActivityReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            return from a in db.ScopedActivity(scope)
                   join i in db.Items on a.ItemId equals i.Id into items
                   from i in items.DefaultIfEmpty()
                   join l in db.Libraries on new { a.ServerId, Id = i.LibraryId } equals new { l.ServerId, l.Id } into libs
                   from l in libs.DefaultIfEmpty()
                   select new ActivityReportRow
                   {
                       Id = a.Id,
                       DateCreated = a.DateCreated,
                       UserId = a.UserId,
                       UserName = a.UserName,
                       ItemId = a.ItemId,
                       Name = a.Name,
                       SeriesName = a.SeriesName,
                       ItemType = (ItemTypes?)i.Type,
                       LibraryId = i.LibraryId,
                       LibraryName = l.Name,
                       LibraryType = (LibraryTypes?)l.Type,
                       SeasonNumber = i.ParentIndex,
                       EpisodeNumber = i.Index,
                       ItemDateAdded = i.DateCreated,
                       SourceCodec = i.Codec,
                       SourceResolution = i.Resolution,
                       Client = a.Client,
                       Device = a.Device,
                       PlayMethod = a.PlayMethod,
                       DirectPlay = a.DirectPlay,
                       Container = a.Container,
                       IpAddress = a.IpAddress,
                       Imported = a.Imported,
                       Duration = a.Duration,
                       Runtime = a.RuntimeTicks / 10000000,
                       CompletionPercentage = a.RuntimeTicks == 0 ? 0 : (double)a.Duration * 10000000.0 / a.RuntimeTicks * 100.0
                   };
        }
    }

    // ---------------------------------------------------------------- Library items

    public class ItemReportRow
    {
        [ReportField("Item Id", T.String, Groupable = false)] public string Id { get; set; } = "";
        [ReportField("Name", T.String)] public string Name { get; set; } = "";
        [ReportField("Type", T.Enum)] public ItemTypes Type { get; set; }
        [ReportField("Library Id", T.String)] public string LibraryId { get; set; } = "";
        [ReportField("Library", T.String)] public string? LibraryName { get; set; }
        [ReportField("Parent Id", T.String)] public string? ParentId { get; set; }
        [ReportField("Series", T.String, Description = "For seasons and episodes")] public string? SeriesName { get; set; }
        [ReportField("Season #", T.Integer)] public int? SeasonNumber { get; set; }
        [ReportField("Episode #", T.Integer)] public int? EpisodeNumber { get; set; }
        [ReportField("Date Added", T.Date, Aggregatable = true)] public DateTime? DateAdded { get; set; }
        [ReportField("Runtime", T.Duration, Groupable = false, Aggregatable = true)] public long Runtime { get; set; }
        [ReportField("Size", T.Bytes, Groupable = false, Aggregatable = true)] public long Size { get; set; }
        [ReportField("Bitrate", T.Integer, Groupable = false, Aggregatable = true)] public long Bitrate { get; set; }
        [ReportField("Codec", T.String)] public string Codec { get; set; } = "";
        [ReportField("Resolution", T.String)] public string Resolution { get; set; } = "";
        [ReportField("Archived", T.Boolean)] public bool Archived { get; set; }
        [ReportField("Plays", T.Integer, Groupable = false, Aggregatable = true, Description = "Includes plays of child items (episodes of a series).")]
        public int PlayCount { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long TotalWatchTime { get; set; }
        [ReportField("Last Played", T.Date, Groupable = false, Aggregatable = true)] public DateTime? LastPlayed { get; set; }
    }

    public class ItemReportDataset : ReportDataset<ItemReportRow>
    {
        public override string Key => "items";
        public override string Label => "Library Items";
        public override string Description => "Movies, series, seasons, episodes and music, with play totals rolled up from child items.";

        protected override IQueryable<ItemReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            var activity = db.ScopedActivity(scope);
            var plays = from p in db.ItemsPath
                        join a in activity on p.DescendantId equals a.ItemId
                        select new { p.AncestorId, a.Duration, a.DateCreated };

            return from i in db.Items.Where(i => i.ServerId == scope.ServerId)
                   join l in db.Libraries on new { i.ServerId, Id = i.LibraryId } equals new { l.ServerId, l.Id } into libs
                   from l in libs.DefaultIfEmpty()
                   select new ItemReportRow
                   {
                       Id = i.Id,
                       Name = i.Name,
                       Type = i.Type,
                       LibraryId = i.LibraryId,
                       LibraryName = l.Name,
                       ParentId = i.ParentId,
                       SeriesName = (from p in db.ItemsPath
                                     join s in db.Items on p.AncestorId equals s.Id
                                     where p.DescendantId == i.Id && p.Depth > 0 && s.Type == ItemTypes.Series
                                     select s.Name).FirstOrDefault(),
                       SeasonNumber = i.ParentIndex,
                       EpisodeNumber = i.Index,
                       DateAdded = i.DateCreated,
                       Runtime = i.Duration / 10000000,
                       Size = i.Size,
                       Bitrate = i.Bitrate,
                       Codec = i.Codec,
                       Resolution = i.Resolution,
                       Archived = i.archived,
                       PlayCount = plays.Count(p => p.AncestorId == i.Id),
                       TotalWatchTime = plays.Where(p => p.AncestorId == i.Id).Sum(p => (long?)p.Duration) ?? 0,
                       LastPlayed = plays.Where(p => p.AncestorId == i.Id).Max(p => (DateTime?)p.DateCreated)
                   };
        }
    }

    // ---------------------------------------------------------------- Users

    public class UserReportRow
    {
        [ReportField("User Id", T.String, Groupable = false)] public string Id { get; set; } = "";
        [ReportField("User", T.String)] public string Username { get; set; } = "";
        [ReportField("Plays", T.Integer, Groupable = false, Aggregatable = true)] public int PlayCount { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long TotalWatchTime { get; set; }
        [ReportField("Last Seen", T.Date, Groupable = false, Aggregatable = true)] public DateTime? LastActivity { get; set; }
        [ReportField("First Seen", T.Date, Groupable = false, Aggregatable = true)] public DateTime? FirstActivity { get; set; }
        [ReportField("Items Watched", T.Integer, Groupable = false, Aggregatable = true, Description = "Distinct items played")] public int ItemsWatched { get; set; }
        [ReportField("Last Client", T.String)] public string? LastClient { get; set; }
    }

    public class UserReportDataset : ReportDataset<UserReportRow>
    {
        public override string Key => "users";
        public override string Label => "Users";
        public override string Description => "Media server users with their play totals.";

        protected override IQueryable<UserReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            var activity = db.ScopedActivity(scope);
            var users = db.Users.Where(u => u.ServerId == scope.ServerId);
            if (scope.ScopedUserId != null) users = users.Where(u => u.Id == scope.ScopedUserId);

            return users.Select(u => new UserReportRow
            {
                Id = u.Id,
                Username = u.Username,
                PlayCount = activity.Count(a => a.UserId == u.Id),
                TotalWatchTime = activity.Where(a => a.UserId == u.Id).Sum(a => (long?)a.Duration) ?? 0,
                LastActivity = activity.Where(a => a.UserId == u.Id).Max(a => (DateTime?)a.DateCreated),
                FirstActivity = activity.Where(a => a.UserId == u.Id).Min(a => (DateTime?)a.DateCreated),
                ItemsWatched = activity.Where(a => a.UserId == u.Id).Select(a => a.ItemId).Distinct().Count(),
                LastClient = activity.Where(a => a.UserId == u.Id).OrderByDescending(a => a.DateCreated).Select(a => a.Client).FirstOrDefault()
            });
        }
    }

    // ---------------------------------------------------------------- Libraries

    public class LibraryReportRow
    {
        [ReportField("Library Id", T.String, Groupable = false)] public string Id { get; set; } = "";
        [ReportField("Library", T.String)] public string Name { get; set; } = "";
        [ReportField("Type", T.Enum)] public LibraryTypes Type { get; set; }
        [ReportField("Archived", T.Boolean)] public bool Archived { get; set; }
        [ReportField("Items", T.Integer, Groupable = false, Aggregatable = true)] public int ItemCount { get; set; }
        [ReportField("Size", T.Bytes, Groupable = false, Aggregatable = true)] public long TotalSize { get; set; }
        [ReportField("Plays", T.Integer, Groupable = false, Aggregatable = true)] public int PlayCount { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long TotalWatchTime { get; set; }
    }

    public class LibraryReportDataset : ReportDataset<LibraryReportRow>
    {
        public override string Key => "libraries";
        public override string Label => "Libraries";
        public override string Description => "Libraries with item counts, size and play totals.";

        protected override IQueryable<LibraryReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            var activity = db.ScopedActivity(scope);
            var libraryPlays = from a in activity
                               join i in db.Items on a.ItemId equals i.Id
                               select new { i.LibraryId, a.Duration };

            return db.Libraries.Where(l => l.ServerId == scope.ServerId).Select(l => new LibraryReportRow
            {
                Id = l.Id,
                Name = l.Name,
                Type = l.Type,
                Archived = l.archived,
                ItemCount = db.Items.Count(i => i.ServerId == l.ServerId && i.LibraryId == l.Id),
                TotalSize = db.Items.Where(i => i.ServerId == l.ServerId && i.LibraryId == l.Id).Sum(i => (long?)i.Size) ?? 0,
                PlayCount = libraryPlays.Count(p => p.LibraryId == l.Id),
                TotalWatchTime = libraryPlays.Where(p => p.LibraryId == l.Id).Sum(p => (long?)p.Duration) ?? 0
            });
        }
    }

    // ---------------------------------------------------------------- Streams (transcoding)

    public class StreamReportRow
    {
        [ReportField("Activity Id", T.String, Groupable = false)] public string Id { get; set; } = "";
        [ReportField("Date", T.Date, Aggregatable = true)] public DateTime DateCreated { get; set; }
        [ReportField("User Id", T.String)] public string UserId { get; set; } = "";
        [ReportField("User", T.String)] public string UserName { get; set; } = "";
        [ReportField("Title", T.String)] public string Name { get; set; } = "";
        [ReportField("Series", T.String)] public string? SeriesName { get; set; }
        [ReportField("Library", T.String)] public string? LibraryName { get; set; }
        [ReportField("Client", T.String)] public string? Client { get; set; }
        [ReportField("Device", T.String)] public string? Device { get; set; }
        [ReportField("Play Method", T.String)] public string? PlayMethod { get; set; }
        [ReportField("Direct Play", T.Boolean)] public bool DirectPlay { get; set; }
        [ReportField("Source Codec", T.String)] public string? SourceCodec { get; set; }
        [ReportField("Source Resolution", T.String)] public string? SourceResolution { get; set; }
        [ReportField("Source Container", T.String)] public string? SourceContainer { get; set; }
        [ReportField("Video Direct", T.Boolean, Description = "Video was not transcoded")] public bool? VideoDirect { get; set; }
        [ReportField("Audio Direct", T.Boolean, Description = "Audio was not transcoded")] public bool? AudioDirect { get; set; }
        [ReportField("Stream Video Codec", T.String)] public string? StreamVideoCodec { get; set; }
        [ReportField("Stream Audio Codec", T.String)] public string? StreamAudioCodec { get; set; }
        [ReportField("Stream Container", T.String)] public string? StreamContainer { get; set; }
        [ReportField("Stream Width", T.Integer)] public int? StreamWidth { get; set; }
        [ReportField("Stream Height", T.Integer)] public int? StreamHeight { get; set; }
        [ReportField("Stream Bitrate", T.Integer, Groupable = false, Aggregatable = true)] public int? StreamBitrate { get; set; }
        [ReportField("Audio Channels", T.Integer)] public int? AudioChannels { get; set; }
        [ReportField("Transcode Reason", T.String, Description = "The first reason the media server gave")] public string? TranscodeReason { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long Duration { get; set; }
    }

    public class StreamReportDataset : ReportDataset<StreamReportRow>
    {
        public override string Key => "streams";
        public override string Label => "Streams & Transcoding";
        public override string Description => "Playback sessions with what was streamed: direct play vs transcode, codecs, resolution and reasons.";

        protected override IQueryable<StreamReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            return from a in db.ScopedActivity(scope)
                   join i in db.Items on a.ItemId equals i.Id into items
                   from i in items.DefaultIfEmpty()
                   join l in db.Libraries on new { a.ServerId, Id = i.LibraryId } equals new { l.ServerId, l.Id } into libs
                   from l in libs.DefaultIfEmpty()
                   select new StreamReportRow
                   {
                       Id = a.Id,
                       DateCreated = a.DateCreated,
                       UserId = a.UserId,
                       UserName = a.UserName,
                       Name = a.Name,
                       SeriesName = a.SeriesName,
                       LibraryName = l.Name,
                       Client = a.Client,
                       Device = a.Device,
                       PlayMethod = a.PlayMethod,
                       DirectPlay = a.DirectPlay,
                       SourceCodec = i.Codec,
                       SourceResolution = i.Resolution,
                       SourceContainer = a.Container,
                       // TranscodingInfo is a jsonb column; empty for direct plays
                       VideoDirect = (bool?)a.TranscodingInfo!.IsVideoDirect,
                       AudioDirect = (bool?)a.TranscodingInfo!.IsAudioDirect,
                       StreamVideoCodec = a.TranscodingInfo!.VideoCodec,
                       StreamAudioCodec = a.TranscodingInfo!.AudioCodec,
                       StreamContainer = a.TranscodingInfo!.Container,
                       StreamWidth = (int?)a.TranscodingInfo!.Width,
                       StreamHeight = (int?)a.TranscodingInfo!.Height,
                       StreamBitrate = (int?)a.TranscodingInfo!.Bitrate,
                       AudioChannels = (int?)a.TranscodingInfo!.AudioChannels,
                       TranscodeReason = a.TranscodingInfo!.TranscodeReasons[0],
                       Duration = a.Duration
                   };
        }
    }

    // ---------------------------------------------------------------- Genres

    /// <summary>An item and one genre it has, including genres inherited from its series/season.</summary>
    public class ReportItemGenre
    {
        public string ItemId { get; set; } = "";
        public string Genre { get; set; } = "";
    }

    public class GenreReportRow
    {
        [ReportField("Genre", T.String)] public string Genre { get; set; } = "";
        [ReportField("Date", T.Date, Aggregatable = true)] public DateTime DateCreated { get; set; }
        [ReportField("User Id", T.String)] public string UserId { get; set; } = "";
        [ReportField("User", T.String)] public string UserName { get; set; } = "";
        [ReportField("Item Id", T.String)] public string ItemId { get; set; } = "";
        [ReportField("Title", T.String)] public string Name { get; set; } = "";
        [ReportField("Series", T.String)] public string? SeriesName { get; set; }
        [ReportField("Client", T.String)] public string? Client { get; set; }
        [ReportField("Watch Time", T.Duration, Groupable = false, Aggregatable = true)] public long Duration { get; set; }
    }

    public class GenreReportDataset : ReportDataset<GenreReportRow>
    {
        public override string Key => "genres";
        public override string Label => "Playback by Genre";
        public override string Description => "One row per playback session and genre; episodes count towards their series' genres. Group by Genre for totals.";

        protected override IQueryable<GenreReportRow> Build(ApplicationDbContext db, ReportScope scope)
        {
            // Genres are a jsonb array on items. Expand them in SQL, through ItemsPath so episodes inherit the series' genres.
            var itemGenres = db.Database.SqlQuery<ReportItemGenre>($@"
                SELECT DISTINCT p.""DescendantId"" AS ""ItemId"", g.value AS ""Genre""
                FROM ""ItemsPath"" p
                JOIN ""Items"" i ON i.""Id"" = p.""AncestorId""
                CROSS JOIN LATERAL jsonb_array_elements_text(i.""Genres"") AS g(value)
                WHERE i.""ServerId"" = {scope.ServerId}");

            return from a in db.ScopedActivity(scope)
                   join g in itemGenres on a.ItemId equals g.ItemId
                   select new GenreReportRow
                   {
                       Genre = g.Genre,
                       DateCreated = a.DateCreated,
                       UserId = a.UserId,
                       UserName = a.UserName,
                       ItemId = a.ItemId,
                       Name = a.Name,
                       SeriesName = a.SeriesName,
                       Client = a.Client,
                       Duration = a.Duration
                   };
        }
    }

    public interface IReportDatasetRegistry
    {
        IReadOnlyList<IReportDataset> All { get; }
        IReportDataset? Get(string key);
    }

    public class ReportDatasetRegistry : IReportDatasetRegistry
    {
        private readonly Dictionary<string, IReportDataset> _datasets;

        public ReportDatasetRegistry()
        {
            All = [new ActivityReportDataset(), new StreamReportDataset(), new GenreReportDataset(), new ItemReportDataset(), new UserReportDataset(), new LibraryReportDataset()];
            _datasets = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<IReportDataset> All { get; }
        public IReportDataset? Get(string key) => _datasets.GetValueOrDefault(key);
    }
}
