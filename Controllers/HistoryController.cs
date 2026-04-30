using Gridify;
using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
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
        private ApplicationDbContext context { get; set; }

        public HistoryController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        [HttpGet("Activity", Name = "Activity")]
        [DefaultGridifyOrder(nameof(Activity.DateCreated), true)]
        public ActionResult<Paging<Activity>> GetActivity([FromQuery] GridifyQuery query, [FromHeader(Name = "ServerId")] string ServerId)
        {
            return context.Activity.Where(a => a.ServerId == ServerId).Gridify(query);
        }
    }
}
