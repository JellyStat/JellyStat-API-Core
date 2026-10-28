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
using Jellystat_API_Core.Models.Jellystat.Helpers;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Reflection;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ApplicationDbContext context;
        private readonly ILogger<ApiController> _logger;

        public ApiController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context, ILogger<ApiController> logger)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
            _logger = logger;
        }


        [AllowAnonymous]
        [HttpPost("UpdateConfig", Name = "UpdateConfig")]
        public async Task<ActionResult<Models.Jellystat.Server?>> AddOrUpdateServer([FromBody] AddServer server)
        {
            bool isConfigured = Global.IsConfigured;
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true;
            bool isAdmin = Authorized && HttpContext.User.IsAdministrator();

            if (isConfigured && !isAdmin)
            {
                return Unauthorized();
            }
            ServerManagerHelper serverHelper = new ServerManagerHelper();

            if (Uri.TryCreate(server.URL, UriKind.Absolute, out Uri? uri))
            {
                server.URL = uri.GetLeftPart(UriPartial.Authority);
            }
            else
            {
                return BadRequest("Invalid URL");
            }

            if (server.ExternalURL.IsNotEmpty())
            {
                if (Uri.TryCreate(server.ExternalURL, UriKind.Absolute, out Uri? uriExt))
                {
                    server.ExternalURL = uriExt.GetLeftPart(UriPartial.Authority);
                }
                else
                {
                    return BadRequest("Invalid External URL");
                }
            }



            // Editing an existing server: a blank API key keeps the stored one
            var editingServer = string.IsNullOrWhiteSpace(server.Id)
                ? null
                : await context.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == server.Id);

            if (!string.IsNullOrWhiteSpace(server.Id) && editingServer == null)
            {
                return NotFound("Server not found");
            }

            if (editingServer != null && string.IsNullOrWhiteSpace(server.APIKey))
            {
                server.APIKey = editingServer.APIKey;
            }

            BaseSystemInfo? systemInfo = await serverHelper.GetSystemInfo(server.Type, server.URL, server.APIKey);

            if (systemInfo == null)
            {

                return BadRequest("Could not connect to server with the provided information");
            }

            if (editingServer != null && systemInfo.Id != editingServer.Id)
            {
                return BadRequest("The provided URL and API key belong to a different server");
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

            // Keep the settings that are not part of this form (sync schedules, remote auth)
            var existingServer = editingServer ?? await context.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == systemInfo.Id);
            if (existingServer != null)
            {
                _server.TaskSettings = existingServer.TaskSettings;
                _server.AllowRemoteAuth = existingServer.AllowRemoteAuth;
            }

            Models.Jellystat.Server? result = await serverHelper.UpdateConfig(_server, context);

            if (result == null)
            {
                return BadRequest("There was an error updating the config");
            }

            return result;
        }

        //public class RecentlyAddedGroup
        //{
        //    public required Items Item { get; set; }
        //    public List<Items> Children { get; set; } = [];
        //}

        [HttpGet("RecentlyAdded", Name = "RecentlyAdded")]

        [DefaultGridifyOrder(nameof(Items.DateCreated), true)]
        public async Task<ActionResult<Paging<RecentlyAdded>>> GetRecentlyAddedAsync([FromQuery] GridifyQuery query, string? ServerId, bool grouped = false)
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

            if (!grouped)
            {
                var paged = context.ItemsWithParentData
                           .Where(i => i.ServerId == ServerId)
                           .Gridify(query);

                var pagedData = paged.Data
                    .Select(x => new RecentlyAdded(x))
                    .ToList();

                return Ok(new Paging<RecentlyAdded>(paged.Count, pagedData));
            }

            var baseQuery = context.Items
                .AsNoTracking()
                .Where(x => x.ServerId == ServerId
                    && x.Type != ItemTypes.Unknown
                    && x.Type != ItemTypes.Series).ApplyFiltering(query);

            var groupedQuery = baseQuery
                .GroupBy(x => new
                {
                    GroupId = !string.IsNullOrEmpty(x.ParentId) ? x.ParentId! : x.Id,
                    CreatedDate = x.DateCreated.HasValue ? x.DateCreated.Value.Date : DateTime.MinValue
                })
                .Select(g => new
                {
                    GroupId = g.Key.GroupId,
                    DateCreated = g.Key.CreatedDate,
                    Count = g.Count(),
                    LatestDateCreated = g.Key.CreatedDate,
                    RepresentativeItemId = g
                        .OrderByDescending(x => x.DateCreated)
                        .ThenBy(x => x.Id)
                        .Select(x => x.Id)
                        .FirstOrDefault()
                })
                .OrderByDescending(x => x.LatestDateCreated)
                .ThenBy(x => x.GroupId);

            var totalCount = await groupedQuery.CountAsync();

            var pagedGroups = await groupedQuery
                .ApplyPaging(query)
                .ToListAsync();

            if (pagedGroups.Count == 0)
            {
                return Ok(new Paging<RecentlyAdded>(0, []));
            }

            var displayItemIds = pagedGroups
                .Select(g => g.Count > 1 ? g.GroupId : g.RepresentativeItemId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            var displayItems = await context.ItemsWithParentData
                .AsNoTracking()
                .Where(x => x.ServerId == ServerId && displayItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            var multiItemGroupIds = pagedGroups
                .Where(g => g.Count > 1)
                .Select(g => g.GroupId)
                .Distinct()
                .ToList();

            var childLookup = multiItemGroupIds.Count == 0
                ? Enumerable.Empty<Items>().ToLookup(_ => string.Empty)
                : (await baseQuery
                    .Where(x => multiItemGroupIds.Contains(!string.IsNullOrEmpty(x.ParentId) ? x.ParentId! : x.Id))
                    .OrderByDescending(x => x.DateCreated)
                    .ToListAsync())
                    .ToLookup(x => !string.IsNullOrEmpty(x.ParentId) ? x.ParentId! : x.Id);

            var data = pagedGroups
                .Select(g =>
                {
                    var displayItemId = g.Count > 1 ? g.GroupId : g.RepresentativeItemId;

                    if (string.IsNullOrEmpty(displayItemId) || !displayItems.TryGetValue(displayItemId, out var item))
                    {
                        return null;
                    }

                    return new RecentlyAdded(item,
                        g.Count > 1
                            ? childLookup[g.GroupId].ToList()
                            : new List<Items>());
                })
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();

            return Ok(new Paging<RecentlyAdded>(totalCount, data));
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
                            PlayCount = context.Activity
                                 .Where(a => a.UserId == x.User.Id)
                                 .Count(),

                            PlayDuration = (context.Activity
                                .Where(a => a.UserId == x.User.Id)
                                .Select(a => (long?)a.Duration)
                                .Sum() ?? 0),


                            LatestActivity = context.Activity
                                .Where(a => a.UserId == x.User.Id)
                                .OrderByDescending(a => a.DateCreated)
                                .Select(a => a)
                                .FirstOrDefault(),


                            LatestActivityDate = context.Activity
                                .Where(a => a.UserId == x.User.Id)
                                .OrderByDescending(a => a.DateCreated)
                                .Select(a => a.DateCreated)
                                .FirstOrDefault(),

                            Item = context.Items
                                .Where(it => it.Id == context.Activity
                                    .Where(a => a.UserId == x.User.Id)
                                    .OrderByDescending(a => a.DateCreated)
                                    .Select(a => a.ItemId)
                                    .FirstOrDefault())
                                .FirstOrDefault(),

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
        public async Task<ActionResult> SetTrackedUsers([FromBody] List<TrackedUsers> TrackedUsers)
        {

            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    List<ExcludedUsers> addToList = TrackedUsers.Where(u => !u.Tracked).Select(u => new ExcludedUsers { UserId = u.Id, ServerId = u.ServerId }).ToList();
                    List<ExcludedUsers> removeFromList = TrackedUsers.Where(u => u.Tracked).Select(u => new ExcludedUsers { UserId = u.Id, ServerId = u.ServerId }).ToList();

                    if (removeFromList.IsNotEmpty())
                    {
                        await context.BulkDeleteAsync(TrackedUsers.Where(u => u.Tracked).Select(u => new ExcludedUsers { UserId = u.Id, ServerId = u.ServerId }));
                    }
                    if (addToList.IsNotEmpty())
                    {
                        await context.BulkInsertOrUpdateAsync(TrackedUsers.Where(u => !u.Tracked).Select(u => new ExcludedUsers { ServerId = u.ServerId, UserId = u.Id }));
                    }
                    await transaction.CommitAsync();

                    List<string> uniqueServerIds = TrackedUsers
                        .Select(t => t.ServerId)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct()
                        .ToList();

                    foreach (var serverId in uniqueServerIds)
                    {
                        iAPI? api = _apiInstanceManager.GetInstance(serverId);
                        if (api == null) continue;

                        List<ExcludedUsers> exUsers = api.excludedUsers ?? new List<ExcludedUsers>();

                        // Remove users that should no longer be excluded for this server
                        var removeIds = removeFromList
                            .Where(r => r.ServerId == serverId)
                            .Select(r => r.UserId)
                            .ToHashSet();

                        if (removeIds.Count > 0)
                        {
                            exUsers.RemoveAll(e => removeIds.Contains(e.UserId));
                        }

                        // Add users that should be excluded but are not present yet
                        var existingIds = exUsers.Select(e => e.UserId).ToHashSet();
                        var toAdd = addToList
                            .Where(a => a.ServerId == serverId && !existingIds.Contains(a.UserId))
                            .ToList();

                        if (toAdd.Count > 0)
                        {
                            exUsers.AddRange(toAdd);
                        }

                        // Ensure the api instance has the updated reference
                        api.excludedUsers = exUsers;
                    }

                    return Ok();

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
                    List<ExcludedLibraries> addToList = TrackedLibraries.Where(u => !u.Tracked).Select(u => new ExcludedLibraries { LibraryId = u.Id, ServerId = u.ServerId }).ToList();
                    List<ExcludedLibraries> removeFromList = TrackedLibraries.Where(u => u.Tracked).Select(u => new ExcludedLibraries { LibraryId = u.Id, ServerId = u.ServerId }).ToList();

                    if (removeFromList.IsNotEmpty())
                    {
                        await context.BulkDeleteAsync(removeFromList);
                    }
                    if (addToList.IsNotEmpty())
                    {
                        await context.BulkInsertOrUpdateAsync(addToList);
                    }

                    await transaction.CommitAsync();

                    List<string> uniqueServerIds = TrackedLibraries
                        .Select(t => t.ServerId)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct()
                        .ToList();

                    foreach (var serverId in uniqueServerIds)
                    {
                        iAPI? api = _apiInstanceManager.GetInstance(serverId);
                        if (api == null) continue;

                        List<ExcludedLibraries> exLibs = api.excludedLibraries ?? new List<ExcludedLibraries>();

                        var removeIds = removeFromList
                            .Where(r => r.ServerId == serverId)
                            .Select(r => r.LibraryId)
                            .ToHashSet();

                        if (removeIds.Count > 0)
                        {
                            exLibs.RemoveAll(e => removeIds.Contains(e.LibraryId));
                        }

                        var existingIds = exLibs.Select(e => e.LibraryId).ToHashSet();
                        var toAdd = addToList
                            .Where(a => a.ServerId == serverId && !existingIds.Contains(a.LibraryId))
                            .ToList();

                        if (toAdd.Count > 0)
                        {
                            exLibs.AddRange(toAdd);
                        }

                        api.excludedLibraries = exLibs;
                    }

                    return Ok();

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

            return context.Users.Where(u => u.ServerId == ServerId && !context.ExcludedUsers
                   .Any(el => el.ServerId == ServerId && el.UserId == u.Id)).Gridify(query);
        }


        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpGet("LocalUsers", Name = "LocalUsers")]
        [DefaultGridifyOrder(nameof(LocalUsers.Username))]
        public ActionResult<Paging<LocalUsers>> GetLocalUsers([FromQuery] GridifyQuery query)
        {
            Paging<LocalUsers> localUsers = context.LocalUsers.Gridify(query);
            localUsers.Data = localUsers.Data.Select(u =>
            {
                u.Password = string.Empty; // Clear the password before returning
                return u;
            }).ToList();
            return localUsers;
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
               .Where(l => l.ServerId == ServerId && !context.ExcludedLibraries
                   .Any(el => el.ServerId == ServerId && el.LibraryId == l.Id))
               .Select(library => new LibrariesWithStats
               {
                   // Stats fields - calculated from activity on items in this library
                   PlayCount = context.Activity
                       .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                       .Where(x => x.item.LibraryId == library.Id && x.item.ServerId == ServerId)
                       .Count(),

                   PlayDuration = (context.Activity
                       .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                       .Where(x => x.item.LibraryId == library.Id && x.item.ServerId == ServerId)
                       .Select(x => (long?)x.activity.Duration)
                       .Sum() ?? 0),

                   PlaybackDuration = (context.Items
                       .Where(item => item.LibraryId == library.Id && !item.archived && item.ServerId == ServerId)
                       .Select(item => (long?)item.Duration)
                       .Sum() ?? 0),

                   Size = (context.Items
                       .Where(item => item.LibraryId == library.Id && item.ServerId == ServerId && (item.Type != ItemTypes.Series && item.Type != ItemTypes.Season) && !item.archived)
                       .Select(item => (long?)item.Size)
                       .Sum() ?? 0),

                   TypeCounts = context.Items
                       .Where(item => item.LibraryId == library.Id && !item.archived && item.ServerId == ServerId)
                       .GroupBy(item => item.Type)
                       .Select(group => new TypeCountModel
                       {
                           Type = group.Key,
                           Count = group.Count()
                       })
                       .OrderBy(type => type.Type)
                       .ToList(),

                   LatestActivity = context.Activity
                       .Join(context.Items,
                             activity => activity.ItemId,
                             item => item.Id,
                             (activity, item) => new { activity, item })
                       .Where(x => x.item.LibraryId == library.Id && x.item.ServerId == ServerId)
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
                   archived = library.archived,
                   HasArchivedItems = context.Items
                       .Where(item => item.archived && item.LibraryId == library.Id && (item.ParentId == null || item.ParentId == "") && item.ServerId == ServerId).Any()
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

                    Library = context.Libraries.Where(l => l.Id == item.LibraryId && l.ServerId == ServerId).FirstOrDefault(),

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
                    archived = item.archived,
                    Codec = item.Codec,
                    Resolution = item.Resolution,
                    HasArchivedItems = context.ItemsPath
                        .Where(path => path.AncestorId == item.Id)
                        .Join(context.Items,
                              path => path.DescendantId,
                              item => item.Id,
                              (path, item) => item)
                        .Where(item => item.archived)
                        .Any()
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
                .Where(i => i.ServerId == ServerId && EF.Functions.TrigramsSimilarity(i.Name, Name) > similarityThreshold && i.archived != true)
                .OrderByDescending(i => EF.Functions.TrigramsSimilarity(i.Name, Name));


            return _query.Gridify(query);
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

        [HttpGet("AllowRemoteAuth", Name = "AllowRemoteAuth")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<bool>> ToggleAllowRemoteAuth(string ServerId)
        {
            var server = await context.Servers.FirstOrDefaultAsync(s => s.Id == ServerId);
            if (server == null)
            {
                return NotFound("Server not found");
            }

            server.AllowRemoteAuth = !server.AllowRemoteAuth;
            await context.SaveChangesAsync();

            iAPI? instance = _apiInstanceManager.GetInstance(ServerId);

            if (instance != null)
            {
                instance.config.AllowRemoteAuth = server.AllowRemoteAuth;
            }

            return Ok(server.AllowRemoteAuth);
        }

        [HttpGet("Logs", Name = "Logs")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        [DefaultGridifyOrder(nameof(Logs.DateCreated), true)]
        public ActionResult<Paging<Logs>> GetLogs([FromQuery] GridifyQuery query, string? ServerId)
        {
            if (!HttpContext.User.IsAdministrator() || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }

            return context.Logs.Where(l => l.ServerId == ServerId || l.ServerId == null).Gridify(query);
        }

        [HttpGet("Backups", Name = "Backups")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public ActionResult<List<BackupModel>> GetBackups()
        {
            List<string> filePaths = Directory
                        .EnumerateFiles(Global.BackupDirectory, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => Global.BackupExtensions.Contains(Path.GetExtension(f)))
                        .ToList();



            return filePaths.Select(fp => new BackupModel(Path.GetFileName(fp), System.IO.File.GetCreationTime(fp), new FileInfo(fp).Length)).OrderByDescending(b => b.Date).ToList();
        }

        [HttpDelete("Backups", Name = "Backups")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public ActionResult DeleteBackup(string FileName)
        {
            List<string> filePaths = Directory
                        .EnumerateFiles(Global.BackupDirectory, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => Global.BackupExtensions.Contains(Path.GetExtension(f)))
                        .ToList();

            string? backupFilePath = filePaths.FirstOrDefault(f => Path.GetFileName(f).Equals(FileName, StringComparison.OrdinalIgnoreCase));

            if (backupFilePath == null || !System.IO.File.Exists(backupFilePath))
            {
                return NotFound("Backup file not found");
            }

            System.IO.File.Delete(backupFilePath);

            return Ok();
        }

        [HttpDelete("Archived", Name = "Archived")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteArchived(string Id, string ServerId)
        {
            List<Libraries> libraries = await context.Libraries.Where(l => l.ServerId == ServerId && l.Id == Id && l.archived).ToListAsync();


            //If delete on libary layer, cascade handles the linked items
            if (libraries.Any())
            {
                context.Libraries.RemoveRange(libraries);
                await context.SaveChangesAsync();
                return Ok();
            }

            bool libraryExists = await context.Libraries.AnyAsync(l => l.ServerId == ServerId && l.Id == Id);

            List<string> allIds = new List<string>();

            //season ids
            List<string> layer1Ids = new List<string>();
            //episode ids
            List<string> layer2Ids = new List<string>();

            //if library is not archived, check if there are any archived top layeritems in the library, if so delete them, ensuring to drill down to the lowest level and delete all linked items
            if (libraryExists)
            {
                bool libraryHasArchivedItems = await context.Items.AnyAsync(i => i.LibraryId == Id && i.ServerId == ServerId && i.archived && (i.ParentId == null || i.ParentId == ""));
                if (libraryHasArchivedItems)
                {
                    //gets top layer archived items
                    allIds = await context.Items.Where(i => i.LibraryId == Id && i.ServerId == ServerId && i.archived && (i.ParentId == null || i.ParentId == "")).Select(i => i.Id).ToListAsync();

                    //gets all linked items for the top layer archived items, even if they are not archived, as they will be deleted anyway
                    layer1Ids = await context.ItemsPath.Where(p => allIds.Contains(p.AncestorId)).Select(p => p.DescendantId).ToListAsync();
                    layer2Ids = await context.ItemsPath.Where(p => layer1Ids.Contains(p.AncestorId)).Select(p => p.DescendantId).ToListAsync();

                    allIds.AddRange(layer1Ids);
                    allIds.AddRange(layer2Ids);

                    await context.Items.Where(i => allIds.Contains(i.Id) && i.ServerId == ServerId).ExecuteDeleteAsync();
                }

                return Ok();
            }

            //handles cases where the id is not a library id
            bool itemExists = await context.Items.AnyAsync(i => i.Id == Id && i.ServerId == ServerId);
            if (!itemExists)
            {
                return NotFound();
            }

            bool isArchived = await context.Items.AnyAsync(i => i.Id == Id && i.ServerId == ServerId && i.archived);

            //IF Current item not archived, check if it has archived children and delete excluding parent id
            if (!isArchived)
            {
                layer1Ids = await context.ItemsPath.Where(p => p.AncestorId == Id && p.DescendantId != Id).Select(p => p.DescendantId).ToListAsync();
                if (layer1Ids.IsEmpty())
                {
                    return Ok();
                }

                layer2Ids = await context.ItemsPath.Where(p => layer1Ids.Contains(p.AncestorId) && !layer1Ids.Contains(p.DescendantId)).Select(p => p.AncestorId).ToListAsync();

                allIds.AddRange(layer1Ids);
                allIds.AddRange(layer2Ids);
                await context.Items.Where(i => allIds.Contains(i.Id) && i.ServerId == ServerId && i.archived).ExecuteDeleteAsync();

                return Ok();
            }

            //gets any linked items 1 layer deep
            layer1Ids = await context.ItemsPath.Where(p => p.AncestorId == Id && p.DescendantId != Id).Select(p => p.DescendantId).ToListAsync();

            //if no linked items
            if (layer1Ids.IsEmpty())
            {
                await context.Items.Where(i => i.Id == Id && i.ServerId == ServerId && i.archived).ExecuteDeleteAsync();
                return Ok();
            }

            //check a second layer of linked items, if there are no second layer linked items, delete all linked items and the parent item
            layer2Ids = await context.ItemsPath.Where(p => layer1Ids.Contains(p.AncestorId) && !layer1Ids.Contains(p.DescendantId)).Select(p => p.AncestorId).ToListAsync();

            //adds all found ids to a single list and deletes them all, including the parent item
            allIds.Add(Id);
            allIds.AddRange(layer1Ids);
            allIds.AddRange(layer2Ids);
            await context.Items.Where(i => allIds.Contains(i.Id) && i.ServerId == ServerId).ExecuteDeleteAsync();

            return Ok();
        }



        [HttpGet("Version", Name = "Version")]
        public async Task<ActionResult> GetVersion()
        {
            string version = UpdateHelper.CurrentApiVersion.ToString();
            string UITag = UpdateHelper.GetCurrentUITag();

            var latestUiTask = UpdateHelper.GetLatestUITag();
            var latestApiTask = UpdateHelper.GetLatestApiVersion();
            await Task.WhenAll(latestUiTask, latestApiTask);

            UIReleaseInfo? releaseInfo = latestUiTask.Result;
            Version? latestApiVersion = latestApiTask.Result;
            bool hasUpdate = false;

            if (releaseInfo != null && releaseInfo.Id != UITag)
            {
                hasUpdate = true;
            }

            return Ok(new
            {
                apiVersion = version,
                LatestApiVersion = latestApiVersion?.ToString() ?? string.Empty,
                ApiHasUpdate = UpdateHelper.IsApiUpdateAvailable(latestApiVersion),
                CurrentUiTag = UITag,
                LatestUiTag = releaseInfo?.Id ?? string.Empty,
                UIHasUpdate = hasUpdate
            });

        }

        [HttpGet("UpdateUI", Name = "UpdateUI")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> UpdateUI()
        {

            UIReleaseInfo? latestRelease = await UpdateHelper.GetLatestUITag();

            bool hasUpdate = await UpdateHelper.IsUIUpdateAvailable(latestRelease?.Id);

            string url = latestRelease?.Link ?? string.Empty;

            string fileName = Path.GetFileName(url);

            string hash = latestRelease?.Hash ?? string.Empty;

            if (!hasUpdate)
            {
                return NotFound("No UI update available.");
            }

            if (url.IsNullOrEmpty() || fileName.IsNullOrEmpty() || hash.IsNullOrEmpty())
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { UpdateAvailable = hasUpdate });
            }

            string uiDirectory = Global.WebUIDirectory;
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");

            try
            {
                Directory.CreateDirectory(uiDirectory);

                using HttpClient httpClient = new();

                using (var downloadStream = await httpClient.GetStreamAsync(url))
                {
                    using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await downloadStream.CopyToAsync(fileStream);
                }

                string downloadedFileHash = UpdateHelper.GetFileHash(tempZipPath);

                if (!downloadedFileHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Delete(tempZipPath);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { UpdateAvailable = hasUpdate, Message = "Downloaded file hash does not match expected hash." });
                }

                UpdateHelper.ExtractNewUI(tempZipPath, uiDirectory);

                System.IO.File.Delete(tempZipPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"UI update failed: '{uiDirectory}' is not writable in this container. Mount a writable UI directory volume and try again.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    UpdateAvailable = hasUpdate,
                    Message = $"UI update failed because '{uiDirectory}' is not writable in this container. Mount a writable UI directory volume and try again."
                });
            }

            return Ok();

        }

    }
}
