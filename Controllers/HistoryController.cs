using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
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
        public Paging<ActivityModel> GetActivityAsync([FromQuery] GridifyQuery query, string? ServerId, bool GroupResults = true)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            if (GroupResults)
            {

                var latestIdsQuery = context.Activity
              .Where(a => a.ServerId == ServerId)
              .GroupBy(a => new { a.UserId, a.ItemId })
              .Select(g => g.OrderByDescending(x => x.DateCreated)
                            .Select(x => x.Id)
                            .FirstOrDefault());

                return context.Activity
                                .Where(a => latestIdsQuery.Contains(a.Id))
                                .LeftJoin(context.Items,
                                      activity => activity.ItemId,
                                      item => item.Id,
                                      (activity, item) => new { activity, item })
                                .Select(x => new ActivityModel
                                {
                                    Id = x.activity.Id,
                                    ServerId = x.activity.ServerId,
                                    Name = x.activity.Name,
                                    SeriesName = x.activity.SeriesName,
                                    UserId = x.activity.UserId,
                                    UserName = x.activity.UserName,
                                    Client = x.activity.Client,
                                    Device = x.activity.Device,
                                    DeviceId = x.activity.DeviceId,
                                    ItemId = x.activity.ItemId,
                                    SeriesId = x.activity.SeriesId,
                                    SeasonId = x.activity.SeasonId,
                                    Duration = x.activity.Duration,
                                    DirectPlay = x.activity.DirectPlay,
                                    PlayMethod = x.activity.PlayMethod,
                                    MediaStreams = x.activity.MediaStreams,
                                    TranscodingInfo = x.activity.TranscodingInfo,
                                    Imported = x.activity.Imported,
                                    DateCreated = x.activity.DateCreated,
                                    Container = x.activity.Container,
                                    IpAddress = x.activity.IpAddress,
                                    RuntimeTicks = x.activity.RuntimeTicks,
                                    LibraryId = x.item.LibraryId,
                                    Item = x.item,
                                    PlayCount = context.Activity.Count(a => a.ItemId == x.activity.ItemId && a.UserId == x.activity.UserId),
                                    PlayDuration = context.Activity.Where(a => a.ItemId == x.activity.ItemId && a.UserId == x.activity.UserId).Sum(a => a.Duration),
                                    FullName = x.activity.SeriesName == null
                                                    ? x.activity.Name
                                                    : x.activity.SeriesName + " : " +
                                                    (x.item != null ? "S" + (x.item.ParentIndex == null
                                                          ? "??"
                                                          : x.item.ParentIndex < 10
                                                              ? "0" + Convert.ToString(x.item.ParentIndex)
                                                              : Convert.ToString(x.item.ParentIndex)) +
                                                      "E" +
                                                      (x.item.Index == null
                                                          ? "??"
                                                          : x.item.Index < 10
                                                              ? "0" + Convert.ToString(x.item.Index)
                                                              : Convert.ToString(x.item.Index)) +
                                                      " - " : string.Empty)
                                                       +
                                                      x.activity.Name,
                                    GroupedResults = context.Activity.Where(a => a.ItemId == x.activity.ItemId && a.UserId == x.activity.UserId).Select(h => new ActivityModel
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
                                        MediaStreams = h.MediaStreams,
                                        TranscodingInfo = h.TranscodingInfo,
                                        Imported = h.Imported,
                                        DateCreated = h.DateCreated,
                                        Container = h.Container,
                                        IpAddress = h.IpAddress,
                                        RuntimeTicks = h.RuntimeTicks,
                                        Item = x.item,
                                        LibraryId = x.item.LibraryId,
                                        PlayCount = 1,
                                        PlayDuration = h.Duration,
                                        FullName = h.SeriesName == null
                                                ? h.Name
                                                : h.SeriesName + " : " +
                                                (x.item != null ? "S" + (x.item.ParentIndex == null
                                                      ? "??"
                                                      : x.item.ParentIndex < 10
                                                          ? "0" + Convert.ToString(x.item.ParentIndex)
                                                          : Convert.ToString(x.item.ParentIndex)) +
                                                  "E" +
                                                  (x.item.Index == null
                                                      ? "??"
                                                      : x.item.Index < 10
                                                          ? "0" + Convert.ToString(x.item.Index)
                                                          : Convert.ToString(x.item.Index)) +
                                                  " - " : string.Empty)
                                                   +
                                                  h.Name,
                                    }).OrderByDescending(h => h.DateCreated).ToList()
                                })
                                .Gridify(query);
            }

            return context.Activity.Where(a => a.ServerId == ServerId)
                    .LeftJoin(context.Items,
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
                        PlayCount = 1,
                        FullName = a.activity.SeriesName == null
                                                ? a.activity.Name
                                                : a.activity.SeriesName + " : " +
                                                (a.item != null ? "S" + (a.item.ParentIndex == null
                                                      ? "??"
                                                      : a.item.ParentIndex < 10
                                                          ? "0" + Convert.ToString(a.item.ParentIndex)
                                                          : Convert.ToString(a.item.ParentIndex)) +
                                                  "E" +
                                                  (a.item.Index == null
                                                      ? "??"
                                                      : a.item.Index < 10
                                                          ? "0" + Convert.ToString(a.item.Index)
                                                          : Convert.ToString(a.item.Index)) +
                                                  " - " : string.Empty)
                                                   +
                                                  a.activity.Name,
                    })
                .Gridify(query);


        }

        /// <summary>
        /// Activity timeline for a single user: consecutive plays of the same title collapsed into runs.
        /// Administrators can request any user on the server; everyone else only gets their own timeline.
        /// When LibraryIds is omitted all (non excluded) libraries are included.
        /// </summary>
        [HttpGet("Timeline", Name = "Timeline")]
        public async Task<ActionResult<List<TimelineEntry>>> GetTimelineAsync(string? UserId, string? ServerId, [FromQuery] string[]? LibraryIds)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            string? userId;
            if (isAdmin)
            {
                if (string.IsNullOrWhiteSpace(UserId))
                {
                    return BadRequest("UserId is required.");
                }
                userId = UserId;
            }
            else
            {
                // Non admins always get their own timeline, matched on their Jellyfin username.
                string? username = HttpContext.User.GetUsername();
                userId = username == null
                    ? null
                    : await context.Users
                        .Where(u => u.ServerId == ServerId && u.Username.ToLower() == username.ToLower())
                        .Select(u => u.Id)
                        .FirstOrDefaultAsync();

                if (userId == null)
                {
                    return NotFound("No user found for the current login on this server.");
                }
            }

            var libraries = await context.Libraries
                .Where(l => l.ServerId == ServerId && !context.ExcludedLibraries
                    .Any(el => el.ServerId == ServerId && el.LibraryId == l.Id))
                .Select(l => new { l.Id, l.Type })
                .ToListAsync();

            if (LibraryIds is { Length: > 0 })
            {
                libraries = libraries.Where(l => LibraryIds.Contains(l.Id)).ToList();
            }

            var libraryTypes = libraries.ToDictionary(l => l.Id, l => l.Type);
            var libraryIdList = libraryTypes.Keys.ToList();

            var rows = (await context.Activity
                .Where(a => a.ServerId == ServerId && a.UserId == userId)
                .Join(context.Items.Where(i => i.ServerId == ServerId && libraryIdList.Contains(i.LibraryId)),
                      activity => activity.ItemId,
                      item => item.Id,
                      (activity, item) => new
                      {
                          activity.ItemId,
                          activity.SeriesId,
                          Title = activity.SeriesName ?? activity.Name,
                          activity.DateCreated,
                          activity.Duration,
                          ItemType = item.Type,
                          SeasonNumber = item.ParentIndex,
                          item.LibraryId
                      })
                .ToListAsync())
                .Select(r => new TimelineActivityRow(r.ItemId, r.SeriesId, r.Title, r.DateCreated, r.Duration, r.ItemType, r.SeasonNumber, r.LibraryId));

            var entries = TimelineBuilder.Build(rows, libraryTypes, ServerId);

            // Blurhash placeholders for the posters
            var itemIds = entries.Select(e => e.ItemId).Distinct().ToList();
            var hashes = await context.Items
                .Where(i => i.ServerId == ServerId && itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ImageHash })
                .ToListAsync();
            var hashLookup = hashes
                .GroupBy(h => h.Id)
                .ToDictionary(g => g.Key, g => g.First().ImageHash);

            foreach (var entry in entries)
            {
                if (hashLookup.TryGetValue(entry.ItemId, out var hash) && !string.IsNullOrEmpty(hash))
                {
                    entry.ImageHash = hash;
                }
            }

            return entries;
        }

        [HttpDelete("Activity", Name = "Activity")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteActivityAsync(string ServerId, [FromBody] List<string> ids)
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
        [PermissionAuthorize(PermissionTypes.Administrator)]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public ActionResult<Paging<Activity>> GetUnlinkedActivity([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            var unlinkedIds = context.Activity
           .Where(a => a.ServerId == ServerId)
           .Where(a => !context.Items.Any(i => i.Id == a.ItemId && i.ServerId == a.ServerId && !i.archived))
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
