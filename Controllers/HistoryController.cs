using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
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
    public class HistoryController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ApplicationDbContext context;

        public HistoryController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        [HttpGet("Activity", Name = "Activity")]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public async Task<ActionResult<Paging<ActivityModel>>> GetActivityAsync([FromQuery] GridifyQuery query, string? ServerId, bool GroupResults = true)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            if (GroupResults)
            {

                // 1. Get the IDs of the latest record for each group (Server-Side)
                // We use a subquery to find the specific ID that corresponds to the Max DateCreated
                var latestIdsQuery = context.Activity
                    .Where(a => a.ServerId == ServerId)
                    .GroupBy(a => new { a.UserId, a.ItemId })
                    .Select(g => g.OrderByDescending(x => x.DateCreated)
                                  .Select(x => x.Id)
                                  .FirstOrDefault());

                // 2. Build the query for the entities (Server-Side)
                // latestIdsQuery is still just an IQueryable of strings, so Gridify won't break
                var flatQuery = context.Activity
                    .Where(a => latestIdsQuery.Contains(a.Id))
                    .Join(context.Items,
                          activity => activity.ItemId,
                          item => item.Id,
                          (a, i) => new
                          {
                              // Include all fields you want to be able to filter/sort by
                              a.Id,
                              a.ServerId,
                              a.Name,
                              a.SeriesName,
                              a.UserId,
                              a.UserName,
                              a.Client,
                              a.Device,
                              a.DeviceId,
                              a.ItemId,
                              a.SeriesId,
                              a.SeasonId,
                              a.Duration,
                              a.DirectPlay,
                              a.PlayMethod,
                              a.MediaStreams,
                              a.TranscodingInfo,
                              a.Imported,
                              a.DateCreated,
                              a.Container,
                              a.IpAddress,
                              a.RuntimeTicks,
                              LibraryId = i.LibraryId // Joined field
                          });

                // 3. Apply Gridify
                // This handles all filtering/sorting on raw Activity fields
                var pagedActivities = flatQuery.Gridify(query);

                if (pagedActivities.Count == 0)
                    return new Paging<ActivityModel>(0, new List<ActivityModel>());

                // 4. Batch fetch history and items for the current page
                var pageActivities = pagedActivities.Data.ToList();
                var userIds = pageActivities.Select(a => a.UserId).Distinct().ToList();
                var itemIds = pageActivities.Select(a => a.ItemId).Distinct().ToList();

                var allHistory = await context.Activity
                    .AsNoTracking()
                    .Where(a => a.ServerId == ServerId && userIds.Contains(a.UserId) && itemIds.Contains(a.ItemId))
                    .ToListAsync();

                var itemsDict = await context.Items
                    .AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id);

                // 5. Final Mapping
                var resultData = pageActivities.Select(activity =>
                {
                    var history = allHistory
                        .Where(h => h.UserId == activity.UserId && h.ItemId == activity.ItemId)
                        .OrderByDescending(h => h.DateCreated)
                        .ToList();

                    itemsDict.TryGetValue(activity.ItemId, out var item);

                    return new ActivityModel
                    {
                        Id = activity.Id,
                        ServerId = ServerId,
                        Name = activity.Name,
                        SeriesName = activity.SeriesName,
                        UserId = activity.UserId,
                        UserName = activity.UserName,
                        Client = activity.Client,
                        Device = activity.Device,
                        DeviceId = activity.DeviceId,
                        ItemId = activity.ItemId,
                        SeriesId = activity.SeriesId,
                        SeasonId = activity.SeasonId,
                        Duration = activity.Duration,
                        DirectPlay = activity.DirectPlay,
                        PlayMethod = activity.PlayMethod,
                        DateCreated = activity.DateCreated,
                        Container = activity.Container,
                        IpAddress = activity.IpAddress,
                        RuntimeTicks = activity.RuntimeTicks,
                        Item = item,
                        LibraryId = item?.LibraryId ?? "N/A",
                        PlayCount = history.Count,
                        PlayDuration = history.Sum(h => h.Duration),
                        GroupedResults = history.Select(h => new ActivityModel
                        {
                            Id = h.Id,
                            ServerId = ServerId,
                            Name = h.Name,
                            SeriesName = h.SeriesName,
                            UserId = h.UserId,
                            UserName = h.UserName,
                            Client = h.Client,
                            Device = h.Device,
                            DeviceId = h.DeviceId,
                            ItemId = h.ItemId,
                            SeriesId = h.SeriesId,
                            SeasonId = h.SeasonId,
                            Duration = h.Duration,
                            DirectPlay = h.DirectPlay,
                            PlayMethod = h.PlayMethod,
                            DateCreated = h.DateCreated,
                            Container = h.Container,
                            IpAddress = h.IpAddress,
                            RuntimeTicks = h.RuntimeTicks,
                            Item = item,
                            LibraryId = item?.LibraryId ?? "N/A",
                            PlayCount = 1,
                            PlayDuration = h.Duration,
                        }).ToList()
                    };
                }).ToList();

                return new Paging<ActivityModel>(pagedActivities.Count, resultData);

                //return context.Activity
                //     .Where(a => a.ServerId == ServerId)
                //     .Join(context.Items,
                //           activity => activity.ItemId,
                //           item => item.Id,
                //           (activity, item) => new { activity, item })
                //     .AsEnumerable() // Switch to client-side evaluation
                //                     // Group by the unique pair of User and Item
                //     .GroupBy(joined => new { joined.activity.UserId, joined.activity.ItemId })
                //     .Select(g => new
                //     {
                //         group = g,
                //         latest = g.OrderByDescending(x => x.activity.DateCreated).First()
                //     })
                //     .Select(x => new ActivityModel
                //     {
                //         Id = x.latest.activity.Id,
                //         ServerId = x.latest.activity.ServerId,
                //         Name = x.latest.activity.Name,
                //         SeriesName = x.latest.activity.SeriesName,
                //         UserId = x.group.Key.UserId, // From Group Key
                //         UserName = x.latest.activity.UserName,
                //         Client = x.latest.activity.Client,
                //         Device = x.latest.activity.Device,
                //         DeviceId = x.latest.activity.DeviceId,
                //         ItemId = x.group.Key.ItemId, // From Group Key
                //         SeriesId = x.latest.activity.SeriesId,
                //         SeasonId = x.latest.activity.SeasonId,
                //         Duration = x.latest.activity.Duration,
                //         DirectPlay = x.latest.activity.DirectPlay,
                //         PlayMethod = x.latest.activity.PlayMethod,
                //         MediaStreams = x.latest.activity.MediaStreams,
                //         TranscodingInfo = x.latest.activity.TranscodingInfo,
                //         Imported = x.latest.activity.Imported,
                //         DateCreated = x.latest.activity.DateCreated,
                //         Container = x.latest.activity.Container,
                //         IpAddress = x.latest.activity.IpAddress,
                //         RuntimeTicks = x.latest.activity.RuntimeTicks,
                //         LibraryId = x.latest.item.LibraryId,
                //         Item = x.latest.item,
                //         PlayCount = x.group.Count(),
                //         PlayDuration = x.group.Sum(a => a.activity.Duration),

                //         // Map the rest of the group to your nested list
                //         GroupedResults = x.group.OrderByDescending(y => y.activity.DateCreated)
                //                           .Select(y => new ActivityModel
                //                           {
                //                               Id = y.activity.Id,
                //                               ServerId = y.activity.ServerId,
                //                               Name = y.activity.Name,
                //                               SeriesName = y.activity.SeriesName,
                //                               UserId = y.activity.UserId, // From Group Key
                //                               UserName = y.activity.UserName,
                //                               Client = y.activity.Client,
                //                               Device = y.activity.Device,
                //                               DeviceId = y.activity.DeviceId,
                //                               ItemId = y.activity.ItemId, // From Group Key
                //                               SeriesId = y.activity.SeriesId,
                //                               SeasonId = y.activity.SeasonId,
                //                               Duration = y.activity.Duration,
                //                               DirectPlay = y.activity.DirectPlay,
                //                               PlayMethod = y.activity.PlayMethod,
                //                               MediaStreams = y.activity.MediaStreams,
                //                               TranscodingInfo = y.activity.TranscodingInfo,
                //                               Imported = y.activity.Imported,
                //                               DateCreated = y.activity.DateCreated,
                //                               Container = y.activity.Container,
                //                               IpAddress = y.activity.IpAddress,
                //                               RuntimeTicks = y.activity.RuntimeTicks,
                //                               LibraryId = y.item.LibraryId,
                //                               Item = y.item,
                //                               PlayCount = 1,
                //                               PlayDuration = y.activity.Duration
                //                           }).ToList()
                //     })
                //     .AsQueryable() // Convert back to IQueryable for Gridify
                //     .Gridify(query);
            }
            return context.Activity.Where(a => a.ServerId == ServerId)
                    .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                    .Select(a => new ActivityModel
                    {
                        Id = a.activity.Id,
                        ServerId = a.activity.ServerId,
                        Name = a.activity.Name,
                        SeriesName = a.activity.SeriesName,
                        UserId = a.activity.UserId,
                        UserName = a.activity.UserName,
                        Client = a.activity.Client,
                        Device = a.activity.Device,
                        DeviceId = a.activity.DeviceId,
                        ItemId = a.activity.ItemId,
                        SeriesId = a.activity.SeriesId,
                        SeasonId = a.activity.SeasonId,
                        Duration = a.activity.Duration,
                        DirectPlay = a.activity.DirectPlay,
                        PlayMethod = a.activity.PlayMethod,
                        MediaStreams = a.activity.MediaStreams,
                        TranscodingInfo = a.activity.TranscodingInfo,
                        Imported = a.activity.Imported,
                        DateCreated = a.activity.DateCreated,
                        Container = a.activity.Container,
                        IpAddress = a.activity.IpAddress,
                        RuntimeTicks = a.activity.RuntimeTicks,
                        LibraryId = a.item.LibraryId,
                        Item = a.item,
                        PlayDuration = a.activity.Duration,
                        PlayCount = 1
                    })
                .Gridify(query);
        }

        [HttpDelete("Activity", Name = "Activity")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteActivityAsync(string ServerId, List<string> ids)
        {
            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    await context.BulkDeleteAsync(context.Activity.Where(a => ids.Contains(a.Id) && a.ServerId == ServerId).ToList());
                    await transaction.CommitAsync();
                    return Ok();

                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    System.Diagnostics.Debug.WriteLine(ex);
                    return Problem("Error occurred while deleting activity");
                }
            }
        }
    }
}
