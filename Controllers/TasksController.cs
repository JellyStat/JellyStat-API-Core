using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.Tasks;
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
        private readonly ITaskSchedulerService _taskSchedulerService;

        public TasksController(ApplicationDbContext context, IAPIInstanceManager apiIntanceManager, ITaskSchedulerService taskSchedulerService)
        {
            this.context = context;
            _apiInstanceManager = apiIntanceManager;
            _taskSchedulerService = taskSchedulerService;
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

            TaskSettings? task = api.config.TaskSettings.FirstOrDefault(t => t.Task == TaskTypes.FullSync);
            if (task == null)
            {
                return NotFound("Task not found");
            }
            await _taskSchedulerService.EnqueueTaskAsync(api.config, task);
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

            TaskSettings? task = api.config.TaskSettings.FirstOrDefault(t => t.Task == TaskTypes.PartialSync);
            if (task == null)
            {
                return NotFound("Task not found");
            }
            await _taskSchedulerService.EnqueueTaskAsync(api.config, task);
            return Ok();
        }

    }
}
