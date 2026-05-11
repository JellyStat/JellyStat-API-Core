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
        public ActionResult<Paging<ActivityModel>> GetActivity([FromQuery] GridifyQuery query, string? ServerId, bool GroupResults = true)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            if (GroupResults)
            {

                var baseQuery = context.Activity
                                        .Where(a => a.ServerId == ServerId)
                                        .Join(context.Items,
                                              activity => activity.ItemId,
                                              item => item.Id,
                                              (activity, item) => new { activity, item });

                // 2. Perform Grouping and Deep Projection
#pragma warning disable CS8601 // Possible null reference assignment.
                var groupedQuery = baseQuery
                    .GroupBy(joined => new { joined.activity.UserId, joined.activity.ItemId })
                    .Select(g => new ActivityModel
                    {
                        // --- Aggregate Fields ---
                        PlayCount = g.Count(),
                        PlayDuration = g.Sum(x => x.activity.Duration),

                        // --- Header/Latest Record Fields ---
                        // We use FirstOrDefault on the sorted group to pick the 'Latest' metadata
                        Id = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Id).FirstOrDefault(),
                        ServerId = ServerId,
                        Name = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Name).FirstOrDefault(),
                        SeriesName = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.SeriesName).FirstOrDefault(),
                        UserId = g.Key.UserId,
                        UserName = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.UserName).FirstOrDefault(),
                        Client = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Client).FirstOrDefault(),
                        Device = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Device).FirstOrDefault(),
                        DeviceId = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.DeviceId).FirstOrDefault(),
                        ItemId = g.Key.ItemId,
                        SeriesId = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.SeriesId).FirstOrDefault(),
                        SeasonId = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.SeasonId).FirstOrDefault(),
                        Duration = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Duration).FirstOrDefault(),
                        DirectPlay = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.DirectPlay).FirstOrDefault(),
                        PlayMethod = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.PlayMethod).FirstOrDefault(),
                        MediaStreams = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.MediaStreams).FirstOrDefault(),
                        TranscodingInfo = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.TranscodingInfo).FirstOrDefault(),
                        Imported = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Imported).FirstOrDefault(),
                        DateCreated = g.Max(x => x.activity.DateCreated),
                        Container = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.Container).FirstOrDefault(),
                        IpAddress = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.IpAddress).FirstOrDefault(),
                        RuntimeTicks = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.activity.RuntimeTicks).FirstOrDefault(),
                        LibraryId = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.item.LibraryId).FirstOrDefault(),
                        Item = g.OrderByDescending(x => x.activity.DateCreated).Select(x => x.item).FirstOrDefault(),

                        // --- Fully Populated Nested List (Server-Side) ---
                        GroupedResults = g.OrderByDescending(y => y.activity.DateCreated)
                            .Select(y => new ActivityModel
                            {
                                Id = y.activity.Id,
                                ServerId = y.activity.ServerId,
                                Name = y.activity.Name,
                                SeriesName = y.activity.SeriesName,
                                UserId = y.activity.UserId,
                                UserName = y.activity.UserName,
                                Client = y.activity.Client,
                                Device = y.activity.Device,
                                DeviceId = y.activity.DeviceId,
                                ItemId = y.activity.ItemId,
                                SeriesId = y.activity.SeriesId,
                                SeasonId = y.activity.SeasonId,
                                Duration = y.activity.Duration,
                                DirectPlay = y.activity.DirectPlay,
                                PlayMethod = y.activity.PlayMethod,
                                MediaStreams = y.activity.MediaStreams,
                                TranscodingInfo = y.activity.TranscodingInfo,
                                Imported = y.activity.Imported,
                                DateCreated = y.activity.DateCreated,
                                Container = y.activity.Container,
                                IpAddress = y.activity.IpAddress,
                                RuntimeTicks = y.activity.RuntimeTicks,
                                LibraryId = y.item.LibraryId,
                                Item = y.item,
                                PlayCount = 1,
                                PlayDuration = y.activity.Duration
                            }).ToList()
                    })
                    .AsSplitQuery(); // Crucial: Executes the GroupedResults as a separate SQL command
#pragma warning restore CS8601 // Possible null reference assignment.

                // 3. Gridify handles Filtering/Sorting/Paging on the DB
                return groupedQuery.Gridify(query);


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
                        Item = a.item
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
