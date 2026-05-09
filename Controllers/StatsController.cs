using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
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
        [DefaultGridifyOrder(new[] { nameof(ItemsWithStats.PlayCount), nameof(ItemsWithStats.LastPlayedDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ItemsWithStats> GetItemStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            var _query = context.Items
                .Where(i => i.ServerId == ServerId)
                .Select(item => new ItemsWithStats
                {
                    // Stats fields - calculated from descendant activity via ItemsPath
                    PlayCount = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Count(),

                    PlayDuration = (context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Select(activity => (long?)activity.Duration)
                        .Sum() ?? 0),

                    LastPlayedDate = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Select(activity => (DateTime?)activity.DateCreated)
                        .Max(),

                    // Item fields
                    Name = item.Name,
                    ServerId = item.ServerId,
                    Id = item.Id,
                    LibraryId = item.LibraryId,
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
                    ParentId = item.ParentId,
                    ParentIndex = item.ParentIndex,
                    Index = item.Index,
                    archived = item.archived
                });

            return _query.Gridify(query);
        }

        [HttpGet("MostPopularItems", Name = "MostPopularItems")]
        [DefaultGridifyOrder(new[] { nameof(ItemsWithStats.PlayCount), nameof(ItemsWithStats.LastPlayedDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ItemsWithStats> GetMostPopularItems([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddDays(-days);
            var _query = context.Items
                .Where(i => i.ServerId == ServerId)
                .Select(item => new ItemsWithStats
                {
                    // Stats fields - calculated from descendant activity via ItemsPath
                    // PlayCount represents unique user count
                    PlayCount = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Select(activity => activity.UserId)
                        .Distinct()
                        .Count(),

                    PlayDuration = (context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Select(activity => (long?)activity.Duration)
                        .Sum() ?? 0),

                    LastPlayedDate = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Where(activity => activity.DateCreated >= cutoffDate)
                        .Select(activity => (DateTime?)activity.DateCreated)
                        .Max(),

                    // Item fields
                    Name = item.Name,
                    ServerId = item.ServerId,
                    Id = item.Id,
                    LibraryId = item.LibraryId,
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
                    ParentId = item.ParentId,
                    ParentIndex = item.ParentIndex,
                    Index = item.Index,
                    archived = item.archived
                });

            return _query.Gridify(query);
        }

        [HttpGet("LibraryStats", Name = "LibraryStats")]
        [DefaultGridifyOrder(new[] { nameof(LibrariesWithStats.PlayCount), nameof(LibrariesWithStats.LastPlayedDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<LibrariesWithStats> GetLibraryStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            DateTime cutoffDate = days == 0
                ? DateTime.MinValue
                : DateTime.UtcNow.AddDays(-days);

            var _query = context.Libraries
                 .Where(i => i.ServerId == ServerId)
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
                         .Where(item => item.LibraryId == library.Id)
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
                         .ToList(),

                     LastPlayedDate = context.Activity
                         .Where(activity => activity.DateCreated >= cutoffDate)
                         .Join(context.Items,
                               activity => activity.ItemId,
                               item => item.Id,
                               (activity, item) => new { activity, item })
                         .Where(x => x.item.LibraryId == library.Id)
                         .Select(x => (DateTime?)x.activity.DateCreated)
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
        [DefaultGridifyOrder(new[] { nameof(ClientsWithStats.PlayCount), nameof(ClientsWithStats.LastPlayedDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<ClientsWithStats> GetMostUsedClients([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
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
                    LastPlayedDate = group.Select(a => (DateTime?)a.DateCreated).Max()
                })
                .Gridify(query);
        }

        [HttpGet("UserStats", Name = "UserStats")]
        [DefaultGridifyOrder(new[] { nameof(UsersWithStats.PlayCount), nameof(UsersWithStats.LastPlayedDate) }, new[] { true, true })]
        [DefaultGridifyPageSize(5)]
        public Paging<UsersWithStats> GetUserStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
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
                    LastPlayedDate = group.Select(a => (DateTime?)a.DateCreated).Max()

                })
                .Gridify(query);
        }

        [HttpGet("TranscodeStats", Name = "TranscodeStats")]
        [DefaultGridifyOrder(nameof(UsersWithStats.PlayCount), true)]
        [DefaultGridifyPageSize(5)]
        public Paging<TranscodesWithStats> GetTranscodeStats([FromQuery] GridifyQuery query, string? ServerId, int days = 30)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
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

    }

}
