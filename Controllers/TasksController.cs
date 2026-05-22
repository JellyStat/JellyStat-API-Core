using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class TasksController : Controller
    {
        private readonly ApplicationDbContext context;
        private readonly IAPIInstanceManager _apiInstanceManager;

        public TasksController(ApplicationDbContext context, IAPIInstanceManager apiIntanceManager)
        {
            this.context = context;
            _apiInstanceManager = apiIntanceManager;
        }


        //Tasks
        [HttpGet("StartSync", Name = "StartSync")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> StartSync(string ServerId)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found");
            }

            await api.syncTask.FullSync();

            return Ok();
        }

        [HttpGet("StartPartialSync", Name = "StartPartialSync")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> StartPartialSync(string ServerId)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found");
            }

            await api.syncTask.PartialSync();
            return Ok();
        }

    }
}
