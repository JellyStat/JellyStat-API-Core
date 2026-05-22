using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Models.Jellystat.V1;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ApplicationDbContext context;

        public ApiController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("UpdateConfig", Name = "UpdateConfig")]
        public async Task<ActionResult<Models.Jellystat.Server?>> AddOrUpdateServer([FromBody] AddServer server)
        {
            ServerManagerHelper serverHelper = new ServerManagerHelper();

            if (Uri.TryCreate(server.URL, UriKind.Absolute, out Uri? uri))
            {
                server.URL = uri.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                return BadRequest("Invalid URL");
            }

            if (Uri.TryCreate(server.ExternalURL, UriKind.Absolute, out Uri? uriExt))
            {
                server.ExternalURL = uriExt.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                return BadRequest("Invalid External URL");
            }



            BaseSystemInfo? systemInfo = await serverHelper.GetSystemInfo(server.Type, server.URL, server.APIKey);

            if (systemInfo == null)
            {

                return BadRequest("Could not connect to server with the provided information");
            }

            Models.Jellystat.Server _server = new Models.Jellystat.Server
            {
                Id = systemInfo.Id,
                URL = server.URL,
                ExternalURL = server.ExternalURL,
                APIKey = server.APIKey,
                Name = systemInfo.ServerName,
                Type = server.Type,
            };

            Models.Jellystat.Server? result = await serverHelper.UpdateConfig(_server, context);

            if (result == null)
            {
                return BadRequest("There was an error updating the config");
            }

            return result;
        }

        [HttpGet("RecentlyAdded", Name = "RecentlyAdded")]

        [DefaultGridifyOrder(nameof(ItemsWithParentData.DateCreated), true)]
        public ActionResult<Paging<ItemsWithParentData>> GetRecentlyAdded([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }


            return context.ItemsWithParentData.Where(i => i.ServerId == ServerId).Gridify(query);
        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpGet("TrackedUsers", Name = "TrackedUsers")]
        [DefaultGridifyOrder(nameof(TrackedUsers.Username))]
        public ActionResult<Paging<TrackedUsers>> GetTrackedUsers([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            return context.Users
                    .Where(u => u.ServerId == ServerId)
                    .GroupJoin(
                        context.ExcludedUsers,
                        u => u.Id,
                        t => t.UserId,
                        (u, t) => new { User = u, ExcludedUsers = t })
                    .SelectMany(
                        x => x.ExcludedUsers.DefaultIfEmpty(),
                        (x, t) => new TrackedUsers
                        {
                            Id = x.User.Id,
                            Username = x.User.Username,
                            ServerId = x.User.ServerId,
                            ImageTag = x.User.ImageTag,
                            Tracked = t == null
                        })
                    .Gridify(query);

        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("TrackedUsers", Name = "TrackedUsers")]
        public async Task<ActionResult<List<TrackedUsers>>> SetTrackedUsers([FromBody] List<TrackedUsers> TrackedUsers)
        {

            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    await context.BulkDeleteAsync(TrackedUsers.Where(u => u.Tracked).Select(u => new ExcludedUsers { UserId = u.Id, ServerId = u.ServerId }));
                    await context.BulkInsertOrUpdateAsync(TrackedUsers.Where(u => !u.Tracked).Select(u => new ExcludedUsers { ServerId = u.ServerId, UserId = u.Id }));
                    await transaction.CommitAsync();
                    return Ok(TrackedUsers);

                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine(ex);
                    return Problem("Error occurred while setting tracked users");
                }
            }

        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpGet("TrackedLibraries", Name = "TrackedLibraries")]
        [DefaultGridifyOrder(nameof(TrackedLibraries.Name))]
        public ActionResult<Paging<TrackedLibraries>> GetTrackedLibraries([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            return context.Libraries
                    .Where(l => l.ServerId == ServerId)
                    .GroupJoin(
                        context.ExcludedLibraries,
                        l => l.Id,
                        t => t.LibraryId,
                        (l, t) => new { Library = l, ExcludedLibraries = t })
                    .SelectMany(
                        x => x.ExcludedLibraries.DefaultIfEmpty(),
                        (x, t) => new TrackedLibraries
                        {
                            Id = x.Library.Id,
                            ServerId = x.Library.ServerId,
                            Name = x.Library.Name,
                            Type = x.Library.Type,
                            ImageTag = x.Library.ImageTag,
                            ImageHash = x.Library.ImageHash,
                            archived = x.Library.archived,
                            Tracked = t == null
                        })
                    .Gridify(query);

        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("TrackedLibraries", Name = "TrackedLibraries")]
        public async Task<ActionResult<List<TrackedLibraries>>> SetTrackedLibraries([FromBody] List<TrackedLibraries> TrackedLibraries)
        {

            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    await context.BulkDeleteAsync(TrackedLibraries.Where(u => u.Tracked).Select(u => new ExcludedLibraries { LibraryId = u.Id, ServerId = u.ServerId }));
                    await context.BulkInsertOrUpdateAsync(TrackedLibraries.Where(u => !u.Tracked).Select(u => new ExcludedLibraries { ServerId = u.ServerId, LibraryId = u.Id }));
                    await transaction.CommitAsync();
                    return Ok(TrackedLibraries);

                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine(ex);
                    return Problem("Error occurred while setting tracked libraries");
                }
            }

        }

        [HttpGet("Users", Name = "Users")]
        [DefaultGridifyOrder(nameof(Users.Username))]
        public ActionResult<Paging<Users>> GetUsers([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            return context.Users.Where(u => u.ServerId == ServerId).Gridify(query);
        }

        [HttpGet("Libraries", Name = "Libraries")]
        [DefaultGridifyOrder(nameof(LibrariesWithStats.Name))]
        public ActionResult<Paging<LibrariesWithStats>> GetLibraries([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            var _query = context.Libraries
               .Where(i => i.ServerId == ServerId)
               .Select(library => new LibrariesWithStats
               {
                   // Stats fields - calculated from activity on items in this library
                   PlayCount = context.Activity
                       .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                       .Where(x => x.item.LibraryId == library.Id)
                       .Count(),

                   PlayDuration = (context.Activity
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

                   LatestActivity = context.Activity
                       .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                       .Where(x => x.item.LibraryId == library.Id)
                       .OrderByDescending(x => x.activity.DateCreated)
                       .Select(x => x.activity)
                       .FirstOrDefault(),

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

        [HttpGet("LibraryItems", Name = "LibraryItems")]

        [DefaultGridifyOrder(nameof(Items.Name))]
        public ActionResult<Paging<ItemsWithStats>> GetLibraryItems([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            var _query = context.ItemsWithParentData
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
                        .Count(),

                    PlayDuration = (context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .Select(activity => (long?)activity.Duration)
                        .Sum() ?? 0),

                    LatestActivity = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .OrderByDescending(activity => activity.DateCreated)
                        .FirstOrDefault(),

                    LatestActivityDate = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Activity,
                              path => path.DescendantId,
                              activity => activity.ItemId,
                              (path, activity) => activity)
                        .OrderByDescending(activity => activity.DateCreated)
                        .Select(activity => activity.DateCreated)
                        .FirstOrDefault(),

                    // Item fields
                    Parent = item.Parent,
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

        [HttpGet("MatchingItems", Name = "MatchingItems")]
        public ActionResult<Paging<ItemsWithParentData>> GetMatchingItems([FromQuery] GridifyQuery query, string Name, string? ServerId)
        {
            if (Name.Trim().Length < 3)
            {
                return BadRequest("Input needs to be at least 3 characters long");
            }

            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            double similarityThreshold = 0.3;
            var _query = context.ItemsWithParentData
                .Where(i => i.ServerId == ServerId && EF.Functions.TrigramsSimilarity(i.Name, Name) > similarityThreshold)
                .OrderByDescending(i => EF.Functions.TrigramsSimilarity(i.Name, Name));


            return _query.Gridify(query);
        }



        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("InsertActivity", Name = "InsertActivity")]
        public async Task<ActionResult> InsertActivity([FromBody] List<ActivityV1> activity, string? ServerId)
        {
            if (activity == null)
            {
                return BadRequest("Provided Activity list is empty");
            }

            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("Server Id not found");
            }

            try
            {

                await context.BulkInsertOrUpdateAsync(activity.Where(a => a.ServerId != null && a.ServerId.IsNotEmpty() && a.ServerId == api.config.Id).Select(a => a.ConvertData()).ToList());
                return Ok();

            }
            catch (Exception ex)
            {

                Debug.WriteLine($"Error inserting activity: {ex.Message}");
                Debug.WriteLine(ex);
                return Problem($"Error inserting activity: {ex.Message}");

            }


        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("SetLocalUserServer", Name = "SetLocalUserServer")]
        public async Task<ActionResult> SetLocalUserServer([FromBody] LocalUserDefaultServerModel model)
        {
            try
            {
                LocalUsers? user = await context.LocalUsers.Where(u => u.Id == model.UserId).FirstOrDefaultAsync();

                if (user == null)
                {
                    return NotFound("Invalid User Id");
                }

                bool ServerExists = _apiInstanceManager.HasInstance(model.ServerId);

                if (!ServerExists)
                {
                    return NotFound("Invalid Server Id");
                }
                user.ServerId = model.ServerId;
                await context.SaveChangesAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error Setting Local Users Server Id: {ex.Message}");
                return Problem(ex.Message);
            }


        }

    }
}
