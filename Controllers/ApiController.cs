using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private ApplicationDbContext context { get; set; }

        public ApiController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }

        [HttpGet("Config", Name = "Config")]
        public ActionResult<Models.Jellystat.Server> GetConfig([FromHeader(Name = "ServerId")] string ServerId)
        {
            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            return api.config;
        }

        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("Config", Name = "Config")]
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
        public ActionResult<List<ItemsWithParentData>> GetRecentlyAdded([FromHeader(Name = "ServerId")] string ServerId, string? libraryId, int limit = 50)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            if (libraryId != null)
            {
                //List<ItemTypes> excludedTypes = new List<ItemTypes> { ItemTypes.Folder, ItemTypes.Season, ItemTypes.Series, ItemTypes.Unknown };
                return context.ItemsWithParentData.Where(i => i.DateCreated != null && i.LibraryId == libraryId && i.ServerId == ServerId).OrderByDescending(i => i.DateCreated).Take(limit).ToList();
            }

            return context.ItemsWithParentData.Where(i => i.DateCreated != null && i.ServerId == ServerId).OrderByDescending(i => i.DateCreated).Take(limit).ToList();
        }

        //[PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpGet("TrackedUsers", Name = "TrackedUsers")]
        [DefaultGridifyOrder(nameof(TrackedUsers.Username))]
        public ActionResult<Paging<TrackedUsers>> GetTrackedUsers([FromQuery] GridifyQuery query)
        {

            return context.Users
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

        //[PermissionAuthorize(PermissionTypes.Administrator)]
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

        //[PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpGet("TrackedLibraries", Name = "TrackedLibraries")]
        [DefaultGridifyOrder(nameof(TrackedLibraries.Name))]
        public ActionResult<Paging<TrackedLibraries>> GetTrackedLibraries([FromQuery] GridifyQuery query)
        {

            return context.Libraries
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

        //[PermissionAuthorize(PermissionTypes.Administrator)]
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
        public ActionResult<Paging<Users>> GetUsers([FromQuery] GridifyQuery query, [FromHeader(Name = "ServerId")] string ServerId)
        {
            return context.Users.Where(u => u.ServerId == ServerId).Gridify(query);
        }

        [HttpGet("Libraries", Name = "Libraries")]
        [DefaultGridifyOrder(nameof(Libraries.Name))]
        public ActionResult<Paging<Libraries>> GetLibraries([FromQuery] GridifyQuery query, [FromHeader(Name = "ServerId")] string ServerId)
        {
            return context.Libraries.Where(u => u.ServerId == ServerId).Gridify(query);
        }

        [HttpGet("LibraryItems", Name = "LibraryItems")]

        [DefaultGridifyOrder(nameof(Items.Name))]
        public ActionResult<Paging<ItemsWithStats>> GetLibraryItems([FromQuery] GridifyQuery query, [FromHeader(Name = "ServerId")] string ServerId)
        {
            return context.ItemsWithStats.Where(u => u.ServerId == ServerId).Gridify(query);
        }


    }
}
