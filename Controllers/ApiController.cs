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
                    Resolution = item.Resolution
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
        public ActionResult<List<string>> GetBackups()
        {
            List<string> filePaths = Directory
                        .EnumerateFiles(Global.BackupDirectory, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => Global.BackupExtensions.Contains(Path.GetExtension(f)))
                        .ToList();

            return filePaths.Select(fp => Path.GetFileName(fp)).ToList();
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

            if (libraries.Any())
            {
                context.Libraries.RemoveRange(libraries);
                await context.SaveChangesAsync();
                return Ok();
            }

            List<string> linkedIds = await context.ItemsPath.Where(p => p.AncestorId == Id).Select(p => p.DescendantId).ToListAsync();
            if (linkedIds.Count() < 1)
            {
                return NotFound();
            }

            if (linkedIds.Count() == 1)
            {
                await context.Items.Where(i => i.Id == Id && i.ServerId == ServerId && i.archived).ExecuteDeleteAsync();
                return Ok();
            }

            List<string> episodeIds = await context.ItemsPath.Where(p => linkedIds.Contains(p.AncestorId) && !linkedIds.Contains(p.DescendantId)).Select(p => p.AncestorId).ToListAsync();

            linkedIds.AddRange(episodeIds);
            await context.Items.Where(i => linkedIds.Contains(i.Id) && i.ServerId == ServerId && i.archived).ExecuteDeleteAsync();

            return Ok();
        }
    }
}
