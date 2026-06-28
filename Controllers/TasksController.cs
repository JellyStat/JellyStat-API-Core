using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Helpers;
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
        [HttpGet("StartTask", Name = "StartTask")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> StartTask(string ServerId, TaskTypes Task)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found");
            }

            TaskSettings? task = api.config.TaskSettings.FirstOrDefault(t => t.Task == Task);
            if (task == null)
            {
                return NotFound("Task not found");
            }
            await _taskSchedulerService.EnqueueTaskAsync(api.config, task);
            return Ok();
        }


        [HttpPost("UpdateTask", Name = "UpdateTask")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> UpdateTask(string ServerId, [FromBody] TaskSettings taskSetting)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found");
            }

            TaskSettings[] taskSettings = api.config.TaskSettings.ToArray();

            TaskSettings? task = taskSettings.FirstOrDefault(t => t.Task == taskSetting.Task);
            if (task == null)
            {
                return NotFound("Task not found");
            }

            task.IntervalMinutes = taskSetting.IntervalMinutes;
            task.Enabled = taskSetting.Enabled;
            ServerManagerHelper serverHelper = new ServerManagerHelper();
            bool success = await serverHelper.UpdateTasksInDatabase(ServerId, taskSettings, context);
            if (success)
            {
                await _taskSchedulerService.RegisterTasksAsync(api.config, taskSettings.ToList());
                return Ok();
            }
            return BadRequest("Failed to update task");
        }


    }
}
