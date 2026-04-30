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


        [HttpGet("Libraries", Name = "Libraries")]
        public ActionResult<List<Libraries>> GetLibraries([FromHeader(Name = "ServerId")] string ServerId)
        {


            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            return context.Libraries.ToList();
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


    }
}
