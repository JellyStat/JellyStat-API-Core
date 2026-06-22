using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class StatsController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ApplicationDbContext context;

        public StatsController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }

        [HttpGet("ItemStats", Name = "ItemStats")]
        [DefaultGridifyOrder(new[] { nameof(ItemsWithStats.PlayCount), nameof(ItemsWithStats.LatestActivityDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ItemsWithStats> GetItemStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            var _query = context.ItemsWithParentData
                         .Where(i => i.ServerId == ServerId)
                         .GroupJoin(context.ItemsPath,
                             item => item.Id,
                             path => path.AncestorId,
                             (item, paths) => new { item, paths })
                         .SelectMany(
                             x => x.paths,
                             (x, path) => new { x.item, path })
                         .GroupJoin(context.Activity.Where(a => a.DateCreated >= cutoffDate),
                             x => x.path.DescendantId,
                             activity => activity.ItemId,
                             (x, activities) => new { x.item, activities })
                         .GroupBy(
                             x => x.item,
                             (item, group) => new ItemsWithStats
                             {
                                 Id = item.Id,
                                 ServerId = item.ServerId,
                                 LibraryId = item.LibraryId,
                                 ParentId = item.ParentId,
                                 Parent = item.Parent,
                                 Name = item.Name,
                                 DateCreated = item.DateCreated,
                                 MediaStreams = item.MediaStreams,
                                 Duration = item.Duration,
                                 Type = item.Type,
                                 ImageTag = item.ImageTag,
                                 ImageHash = item.ImageHash,
                                 Genres = item.Genres,
                                 Path = item.Path,
                                 Size = item.Size,
                                 Bitrate = item.Bitrate,
                                 ParentIndex = item.ParentIndex,
                                 Index = item.Index,
                                 archived = item.archived,
                                 Codec = item.Codec,
                                 Resolution = item.Resolution,

                                 Library = context.Libraries.FirstOrDefault(l => l.Id == item.LibraryId),
                                 PlayCount = group
                                     .SelectMany(g => g.activities)
                                     .Count(),

                                 PlayDuration = group
                                     .SelectMany(g => g.activities)
                                     .Sum(a => (long?)a.Duration) ?? 0,

                                 LatestActivityDate = group
                                     .SelectMany(g => g.activities)
                                     .Max(a => (DateTime?)a.DateCreated),

                                 LatestActivity = group
                                     .SelectMany(g => g.activities)
                                     .OrderByDescending(a => a.DateCreated)
                                     .FirstOrDefault()
                             });

            return _query.Gridify(query);
        }

        [HttpGet("MostPopularItems", Name = "MostPopularItems")]
        [DefaultGridifyOrder(new[] { nameof(ItemsWithStats.PlayCount), nameof(ItemsWithStats.LatestActivityDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ItemsWithStats> GetMostPopularItems([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            var _query = context.ItemsWithParentData
                        .Where(i => i.ServerId == ServerId)
                        .GroupJoin(context.ItemsPath,
                            item => item.Id,
                            path => path.AncestorId,
                            (item, paths) => new { item, paths })
                        .SelectMany(
                            x => x.paths,
                            (x, path) => new { x.item, path })
                        .GroupJoin(context.Activity.Where(a => a.DateCreated >= cutoffDate),
                            x => x.path.DescendantId,
                            activity => activity.ItemId,
                            (x, activities) => new { x.item, activities })
                        .GroupBy(
                            x => x.item,
                            (item, group) => new ItemsWithStats
                            {
                                Id = item.Id,
                                ServerId = item.ServerId,
                                LibraryId = item.LibraryId,
                                ParentId = item.ParentId,
                                Parent = item.Parent,
                                Name = item.Name,
                                DateCreated = item.DateCreated,
                                MediaStreams = item.MediaStreams,
                                Duration = item.Duration,
                                Type = item.Type,
                                ImageTag = item.ImageTag,
                                ImageHash = item.ImageHash,
                                Genres = item.Genres,
                                Path = item.Path,
                                Size = item.Size,
                                Bitrate = item.Bitrate,
                                ParentIndex = item.ParentIndex,
                                Index = item.Index,
                                archived = item.archived,
                                Codec = item.Codec,
                                Resolution = item.Resolution,

                                Library = context.Libraries.FirstOrDefault(l => l.Id == item.LibraryId),

                                PlayCount = group
                                    .SelectMany(g => g.activities)
                                    .Select(a => a.UserId)
                                    .Distinct()
                                    .Count(),

                                PlayDuration = group
                                    .SelectMany(g => g.activities)
                                    .Sum(a => (long?)a.Duration) ?? 0,

                                LatestActivityDate = group
                                    .SelectMany(g => g.activities)
                                    .Max(a => (DateTime?)a.DateCreated),

                                LatestActivity = group
                                    .SelectMany(g => g.activities)
                                    .OrderByDescending(a => a.DateCreated)
                                    .FirstOrDefault()
                            });

            return _query.Gridify(query);
        }

        [HttpGet("LibraryStats", Name = "LibraryStats")]
        [DefaultGridifyOrder(new[] { nameof(LibrariesWithStats.PlayCount), nameof(LibrariesWithStats.LatestActivityDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<LibrariesWithStats> GetLibraryStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                ? DateTime.MinValue
                : DateTime.UtcNow.AddDays(-days);

            var _query = context.Libraries
                 .Where(i => i.ServerId == ServerId && !context.ExcludedLibraries.Select(el => el.LibraryId).Contains(i.Id))
                 .Select(library => new LibrariesWithStats
                 {
                     PlayCount = context.Activity
                         .Where(activity => activity.DateCreated >= cutoffDate) // Simple comparison
                         .Join(context.Items,
                               activity => activity.ItemId,
                               item => item.Id,
                               (activity, item) => new { activity, item })
                         .Where(x => x.item.LibraryId == library.Id)
                         .Count(),

                     PlayDuration = (context.Activity
                         .Where(activity => activity.DateCreated >= cutoffDate)
                         .Join(context.Items,
                               activity => activity.ItemId,
                               item => item.Id,
                               (activity, item) => new { activity, item })
                         .Where(x => x.item.LibraryId == library.Id)
                         .Select(x => (long?)x.activity.Duration)
                         .Sum() ?? 0),

                     PlaybackDuration = (context.Items
                         .Where(item => item.LibraryId == library.Id)
                         .Select(item => (long?)item.Duration)
                         .Sum() ?? 0),

                     Size = (context.Items
                         .Where(item => item.LibraryId == library.Id && (item.Type != ItemTypes.Series && item.Type != ItemTypes.Season))
                         .Select(item => (long?)item.Size)
                         .Sum() ?? 0),

                     TypeCounts = context.Items
                         .Where(item => item.LibraryId == library.Id)
                         .GroupBy(item => item.Type)
                         .Select(group => new TypeCountModel
                         {
                             Type = group.Key,
                             Count = group.Count()
                         })
                         .OrderBy(type => type.Type)
                         .ToList(),

                     LatestActivity = context.Activity
                                      .Where(activity => context.Items
                                          .Where(item => item.LibraryId == library.Id)
                                          .Select(item => item.Id)
                                          .Contains(activity.ItemId))
                                      .OrderByDescending(a => a.DateCreated)
                                      .FirstOrDefault(),

                     LatestActivityDate = context.Activity
                                      .Where(activity => context.Items
                                          .Where(item => item.LibraryId == library.Id)
                                          .Select(item => item.Id)
                                          .Contains(activity.ItemId))
                                      .Select(a => (DateTime?)a.DateCreated)
                                      .Max(),


                     // Library fields
                     Name = library.Name,
                     ServerId = library.ServerId,
                     Id = library.Id,
                     Type = library.Type,
                     ImageTag = library.ImageTag,
                     ImageHash = library.ImageHash,
                     archived = library.archived
                 });

            return _query.Gridify(query);
        }

        [HttpGet("MostUsedClients", Name = "MostUsedClients")]
        [DefaultGridifyOrder(new[] { nameof(ClientsWithStats.PlayCount), nameof(ClientsWithStats.LatestActivityDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ClientsWithStats> GetMostUsedClients([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                                ? DateTime.MinValue
                                : DateTime.UtcNow.AddDays(-days);
            return context.Activity
                .Where(activity => activity.DateCreated >= cutoffDate && activity.ServerId == ServerId)
                .GroupBy(activity => activity.Client)
                .Select(group => new ClientsWithStats
                {
                    ClientName = group.Key,
                    PlayCount = group.Count(),
                    LatestActivity = group.OrderByDescending(a => a.DateCreated).FirstOrDefault(),
                    LatestActivityDate = group.Select(a => (DateTime?)a.DateCreated).Max()
                })
                .Gridify(query);
        }

        [HttpGet("UserStats", Name = "UserStats")]
        [DefaultGridifyOrder(new[] { nameof(UsersWithStats.PlayCount), nameof(UsersWithStats.LatestActivityDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<UsersWithStats> GetUserStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            return context.Activity
                .Where(activity => activity.DateCreated >= cutoffDate && activity.ServerId == ServerId)
                .GroupBy(activity => activity.UserId)
                .Select(group => new UsersWithStats
                {
                    Id = group.Key,
                    Username = context.Users.Where(u => u.Id == group.Key).Select(u => u.Username).FirstOrDefault() ?? "Unknown",
                    ServerId = ServerId,
                    ImageTag = context.Users.Where(u => u.Id == group.Key).Select(u => u.ImageTag).FirstOrDefault(),
                    PlayCount = group.Count(),
                    PlayDuration = (group.Select(a => (long?)a.Duration).Sum() ?? 0),
                    LatestActivity = group.OrderByDescending(a => a.DateCreated).FirstOrDefault(),
                    LatestActivityDate = group.Select(a => (DateTime?)a.DateCreated).Max()

                })
                .Gridify(query);
        }

        [HttpGet("TranscodeStats", Name = "TranscodeStats")]
        [DefaultGridifyOrder(nameof(UsersWithStats.PlayCount), true)]
        [DefaultGridifyPageSize(5)]
        public Paging<TranscodesWithStats> GetTranscodeStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            return context.Activity
                .Where(activity => activity.DateCreated >= cutoffDate && activity.ServerId == ServerId)
                .GroupBy(activity => activity.PlayMethod)
                .Select(group => new TranscodesWithStats
                {
                    Name = group.Key,
                    PlayCount = group.Count(),

                })
                .Gridify(query);
        }

        [HttpGet("StatTrends", Name = "StatTrends")]
        [DefaultGridifyPageSize(5)]
        public List<ChartStats> GetStatsByDay(string? ServerId, int days = 30, StatMetric metric = StatMetric.Date)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            string dateFormat = metric == StatMetric.Date ? "yyyy-MM-dd" : "";

            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            // Join activity -> items -> libraries, prepare a base projection
            var baseQuery = context.Activity
                .Where(activity => activity.DateCreated >= cutoffDate && activity.ServerId == ServerId)
                .Join(context.Items,
                      activity => activity.ItemId,
                      item => item.Id,
                      (activity, item) => new { activity, item })
                .Join(context.Libraries,
                      ai => ai.item.LibraryId,
                      lib => lib.Id,
                      (ai, lib) => new { ai.activity, ai.item, lib })
                .Select(x => new
                {
                    DateCreated = x.activity.DateCreated,
                    LibraryName = x.lib.Name,
                    Duration = (long?)x.activity.Duration ?? 0L
                });

            List<dynamic> grouped = null!;

            if (metric == StatMetric.Date)
            {
                grouped = baseQuery
                    .Select(x => new { Key = x.DateCreated.Date, x.LibraryName, x.Duration })
                    .GroupBy(x => x.Key)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Libraries = g.GroupBy(x => x.LibraryName)
                                     .Select(lg => new
                                     {
                                         LibraryName = lg.Key,
                                         ActivityCount = lg.Count(),
                                         PlayDuration = lg.Sum(y => y.Duration)
                                     })
                                     .ToList()
                    })
                    .AsEnumerable()
                    .Select(a => (dynamic)a)
                    .ToList();
            }
            else if (metric == StatMetric.Day)
            {
                grouped = baseQuery
                    .Select(x => new { Key = x.DateCreated.DayOfWeek, x.LibraryName, x.Duration })
                    .GroupBy(x => x.Key)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Libraries = g.GroupBy(x => x.LibraryName)
                                     .Select(lg => new
                                     {
                                         LibraryName = lg.Key,
                                         ActivityCount = lg.Count(),
                                         PlayDuration = lg.Sum(y => y.Duration)
                                     })
                                     .ToList()
                    })
                    .AsEnumerable()
                    .Select(a => (dynamic)a)
                    .ToList();
            }
            else // Hour
            {
                grouped = baseQuery
                    .Select(x => new { Key = x.DateCreated.Hour, x.LibraryName, x.Duration })
                    .GroupBy(x => x.Key)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Libraries = g.GroupBy(x => x.LibraryName)
                                     .Select(lg => new
                                     {
                                         LibraryName = lg.Key,
                                         ActivityCount = lg.Count(),
                                         PlayDuration = lg.Sum(y => y.Duration)
                                     })
                                     .ToList()
                    })
                    .AsEnumerable()
                    .Select(a => (dynamic)a)
                    .ToList();
            }

            // Map to ChartStats model: Key = date, Stats = list of ChartStat (Legend, Count, PlayDuration)
            var result = new List<ChartStats>();
            foreach (var day in grouped)
            {
                var cs = new ChartStats
                {
                    Key = day.Date.ToString(dateFormat)
                };

                foreach (var lib in day.Libraries)
                {
                    cs.Stats.Add(new ChartStat
                    {
                        Legend = lib.LibraryName ?? "Unknown",
                        Count = lib.ActivityCount,
                        PlayDuration = lib.PlayDuration
                    });
                }

                result.Add(cs);
            }

            return result;
        }

        [HttpGet("CodecStats", Name = "CodecStats")]
        public List<CountModel<string>> GetCodecStats(string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            List<ItemTypes> excludedTypes = new List<ItemTypes>() {
                 ItemTypes.Series,
                 ItemTypes.Season,
                 ItemTypes.Unknown,
                 ItemTypes.Trailer
            };

            return context.Items
                .Where(item => !excludedTypes.Contains(item.Type) && item.ServerId == ServerId)
                .GroupBy(item => item.Codec)
                .Select(group => new CountModel<string>
                {
                    Name = group.Key,
                    Count = group.Count(),

                })
                .ToList();
        }

        [HttpGet("ResolutionStats", Name = "ResolutionStats")]
        public List<CountModel<string>> GetResolutionStats(string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            List<ItemTypes> excludedTypes = new List<ItemTypes>() {
                 ItemTypes.Series,
                 ItemTypes.Season,
                 ItemTypes.Unknown,
                 ItemTypes.Trailer
            };

            return context.Items
                .Where(item => !excludedTypes.Contains(item.Type) && item.ServerId == ServerId)
                .GroupBy(item => item.Resolution)
                .Select(group => new CountModel<string>
                {
                    Name = group.Key,
                    Count = group.Count(),

                })
                .ToList();
        }

        [HttpGet("GenreStats", Name = "GenreStats")]
        [DefaultGridifyOrder(nameof(GenreStats.PlayCount), true)]
        [DefaultGridifyPageSize(5)]
        public IQueryable<GenreStats> GetGenreStats([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            return context.Activity
                  .Where(a => a.ServerId == ServerId)
                  .Join(
                      context.Items,
                      activity => activity.ItemId,
                      item => item.Id,
                      (activity, item) => new { activity, item })
                  .ApplyFiltering(query)
                  .Select(x => new
                  {
                      x.activity.Duration,
                      x.item.Genres
                  })
                  .AsEnumerable()
                  .Where(x => x.Genres.Count > 0)
                  .SelectMany(
                      x => x.Genres,
                      (x, genre) => new { Genre = genre, x.Duration })
                  .GroupBy(x => x.Genre)
                  .Select(g => new GenreStats
                  {
                      Name = g.Key,
                      PlayCount = g.Count(),
                      PlayDuration = g.Sum(x => (long?)x.Duration) ?? 0
                  })
                  .OrderBy(x => x.Name)
                  .AsQueryable();
        }

    }

}
