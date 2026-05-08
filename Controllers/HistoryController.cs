using EFCore.BulkExtensions;
using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class HistoryController : Controller
    {
        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ApplicationDbContext context;

        public HistoryController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        [HttpGet("Activity", Name = "Activity")]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public ActionResult<Paging<Activity>> GetActivity([FromQuery] GridifyQuery query, string? ServerId)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            return context.Activity.Where(a => a.ServerId == ServerId).Gridify(query);
        }

        [HttpDelete("Activity", Name = "Activity")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteActivityAsync(string ServerId, List<string> ids)
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
    }
}
