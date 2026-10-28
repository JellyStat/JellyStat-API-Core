using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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


            if (Task == TaskTypes.Restore || Task == TaskTypes.Backup)
            {
                GlobalTasks? globalTask = context.GlobalTasks.Where(gt => gt.Task == Task).FirstOrDefault();
                if (globalTask == null)
                {
                    return NotFound("Task Not Found");
                }

                TaskSettings taskSetting = new TaskSettings(globalTask.Task, globalTask.IntervalMinutes, globalTask.Enabled);
                await _taskSchedulerService.EnqueueTaskAsync(null, taskSetting);
                return Ok();

            }

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

        [HttpGet("StopTask", Name = "StopTask")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> StopTask(string? ServerId, TaskTypes Task)
        {
            bool cancelled = await _taskSchedulerService.StopRunningTaskAsync(ServerId, Task);
            if (!cancelled)
            {
                return NotFound("No matching running task found");
            }

            return Ok();
        }


        [HttpPost("UpdateTask", Name = "UpdateTask")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> UpdateTask(string? ServerId, [FromBody] TaskSettings taskSetting)
        {
            // Global tasks (e.g. Backup) live in the GlobalTasks table, not on a server's TaskSettings
            bool isGlobalTask = await context.GlobalTasks.AnyAsync(gt => gt.Task == taskSetting.Task);
            if (isGlobalTask)
            {
                if (taskSetting.IntervalMinutes < 0)
                {
                    return BadRequest("Interval must be zero or greater");
                }

                await context.GlobalTasks
                    .Where(gt => gt.Task == taskSetting.Task)
                    .ExecuteUpdateAsync(gt => gt
                        .SetProperty(p => p.IntervalMinutes, taskSetting.IntervalMinutes)
                        .SetProperty(p => p.Enabled, taskSetting.Enabled));

                await _taskSchedulerService.RegisterGlobalTasks();
                return Ok(true);
            }

            if (string.IsNullOrWhiteSpace(ServerId))
            {
                return BadRequest("ServerId is required for server tasks");
            }

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
                return Ok(true);
            }
            return BadRequest("Failed to update task");
        }


        //Restore
        [HttpGet("RestoreTask", Name = "RestoreTask")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> RestoreTask(string FileName)
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

            TaskSettings restoreTask = new TaskSettings(TaskTypes.Restore, 0, true, new List<string> { backupFilePath });
            await _taskSchedulerService.EnqueueTaskAsync(null, restoreTask);
            return Ok();
        }

        [HttpPost("UploadBackup", Name = "UploadBackup")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        [RequestSizeLimit(500_000_000)] // allow up to 500 MB
        [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]

        public async Task<ActionResult> UploadBackup()
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest("Request must be multipart/form-data");
            }

            IFormFile? file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file was uploaded");
            }

            string extension = Path.GetExtension(file.FileName);
            if (!Global.BackupExtensions.Contains(extension))
            {
                return BadRequest("Only .bac, .json, and .jsonl files are allowed");
            }

            Directory.CreateDirectory(Global.BackupDirectory);

            string fileName = Path.GetFileName(file.FileName);
            string backupFilePath = Path.Combine(Global.BackupDirectory, fileName);

            await using FileStream stream = System.IO.File.Create(backupFilePath);
            await file.CopyToAsync(stream);

            return Ok(new { fileName, filePath = backupFilePath });
        }

        [HttpGet("DownloadBackup", Name = "DownloadBackup")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public ActionResult DownloadBackup(string FileName)
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

            string contentType = "application/octet-stream";
            return PhysicalFile(backupFilePath, contentType, Path.GetFileName(backupFilePath));
        }

        [HttpGet("GlobalTasks", Name = "GlobalTasks")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public ActionResult<List<TaskSettings>> GetGlobalTasks()
        {
            List<GlobalTasks> tasks = context.GlobalTasks.ToList();
            return tasks.Select(gt =>
            {
                return new TaskSettings(gt.Task, gt.IntervalMinutes, gt.Enabled);
            }).ToList();
        }

    }
}
