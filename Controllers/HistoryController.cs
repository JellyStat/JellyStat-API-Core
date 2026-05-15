using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat;
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
        private readonly ApplicationDbContext context;

        public HistoryController(ApplicationDbContext context)
        {
            this.context = context;
        }


        [HttpGet("Activity", Name = "Activity")]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public async Task<ActionResult<Paging<ActivityModel>>> GetActivityAsync([FromQuery] GridifyQuery query, string? ServerId, bool GroupResults = true)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
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

        [HttpGet("UnlinkedActivity", Name = "UnlinkedActivity")]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public ActionResult<Paging<Activity>> GetUnlinkedActivity([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            var unlinkedIds = context.Activity
           .Where(a => a.ServerId == ServerId)
           .Where(a => !context.Items.Any(i => i.Id == a.ItemId && i.ServerId == a.ServerId))
           .GroupBy(a => a.ItemId)
           .Select(g => g.Min(x => x.Id));

            return context.Activity
                .Where(a => unlinkedIds.Contains(a.Id))
                .Gridify(query);
        }

        [HttpPost("MigrateActivity", Name = "MigrateActivity")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> MigrateActivity([FromBody] MigrateActivity[] activities)
        {
            List<Activity> matchingActivities = await context.Activity.Where(a => activities.Select(ac => ac.Id).Contains(a.Id)).ToListAsync();

            var matchingIds = matchingActivities.Select(a => a.Id).ToHashSet();
            var matchingActivitiesDict = matchingActivities.ToDictionary(a => a.Id);

            var propertiesToCompare = typeof(MigrateActivity).GetProperties()
                .Where(p => p.Name is not ("Id" or "Success" or "ErrorMessage" or "Changes" or "hasChanges"))
                .ToList();


            foreach (var activity in activities)
            {
                if (!matchingIds.Contains(activity.Id))
                {
                    activity.Success = false;
                    activity.ErrorMessage = "Activity not found";
                }
                else
                {
                    var matchingActivity = matchingActivitiesDict[activity.Id];

                    foreach (var property in propertiesToCompare)
                    {
                        var activityValue = property.GetValue(activity);
                        if (activityValue == null)
                        {
                            activity.Success = false;
                            activity.ErrorMessage = $"'{property.Name}' cannot be null";
                            continue;
                        }
                        var matchingProperty = typeof(Activity).GetProperty(property.Name);
                        if (matchingProperty == null)
                        {
                            activity.Success = false;
                            activity.ErrorMessage = $"Unable to map '{property.Name}' to Activity";
                            continue;
                        }
                        var matchingValue = matchingProperty.GetValue(matchingActivity);

                        if (matchingValue == null)
                        {
                            continue;
                        }

                        if (matchingValue.ToString().Trim().IsNotEmpty() && activityValue.ToString().Trim().IsEmpty())
                        {
                            activity.Success = false;
                            activity.ErrorMessage = $"'{property.Name}' cannot be set to an empty string";
                            continue;
                        }

                        if (!Equals(activityValue, matchingValue))
                        {
                            activity.Changes.Add(new ChangeTracker { PropertyName = matchingProperty.Name, OldValue = matchingValue.ToString(), NewValue = activityValue.ToString() });
                        }
                    }
                }
            }

            List<string> newValues = activities
                                    .Where(a => a.hasChanges && a.Success)
                                    .SelectMany(a => a.Changes)
                                    .Select(c => c.NewValue)
                                    .Distinct()
                                    .ToList();

            HashSet<string> existingItemIds = await context.Items
                                                    .Where(i => newValues.Contains(i.Id))
                                                    .Select(i => i.Id)
                                                    .ToHashSetAsync();

            foreach (var activity in activities.Where(a => a.hasChanges && a.Success))
            {
                foreach (var change in activity.Changes)
                {
                    if (!existingItemIds.Contains(change.NewValue))
                    {
                        activity.Success = false;
                        activity.ErrorMessage = $"The new value '{change.NewValue}' for '{change.PropertyName}' does not exist in the Items table";
                        break;
                    }
                }
            }

            List<ChangeTracker> changeTrackers = activities
                                                .Where(a => a.hasChanges && a.Success)
                                                .SelectMany(a => a.Changes)
                                                .DistinctBy(ct => new { ct.PropertyName, ct.OldValue })
                                                .ToList();


            using (var transaction = context.Database.BeginTransaction())
            {
                try
                {
                    foreach (var changeTracker in changeTrackers)
                    {
                        var propertyInfo = typeof(Activity).GetProperty(changeTracker.PropertyName);
                        if (propertyInfo == null || !propertyInfo.CanWrite)
                        {
                            continue;
                        }

                        var sql = $"UPDATE \"Activity\" SET \"{changeTracker.PropertyName}\" = {{0}} WHERE \"{changeTracker.PropertyName}\" = {{1}}";
                        await context.Database.ExecuteSqlRawAsync(sql, changeTracker.NewValue, changeTracker.OldValue);
                    }

                    await transaction.CommitAsync();
                    return Ok(activities);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    System.Diagnostics.Debug.WriteLine(ex);
                    return Problem("Error occurred while migrating activity");
                }
            }



        }
    }
}
