using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat.V1;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Jellystat_API_Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {

        private readonly IAPIInstanceManager _apiInstanceManager;
        private ApplicationDbContext context { get; set; }

        public WeatherForecastController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {

            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        [HttpGet("systeminfo", Name = "GetSytemInfo")]
        public BaseSystemInfo GetSytemInfo()
        {
            iAPI? api = _apiInstanceManager.GetAllInstances().FirstOrDefault();
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            BaseSystemInfo info = api.GetSystemInfo().Result;

            return info;
        }

        [HttpGet("GetItemsByParentId", Name = "GetItemsByParentId")]
        public async Task<IList<BaseItem>> GetItemsByParentId(string id)
        {


            iAPI? api = _apiInstanceManager.GetInstance("bc184f09a0214ed9ba21c6f5956de156");
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            IList<BaseItem> items = await api.GetItemsFromParentId(id: id);

            return items;
        }

        [HttpGet("GetSessions", Name = "GetSessions")]
        public async Task<IList<BaseSessionItem>> GetSessions()
        {


            iAPI? api = _apiInstanceManager.GetInstance("bc184f09a0214ed9ba21c6f5956de156");
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            IList<BaseSessionItem> items = api.GetSessionData();

            return items;
        }


        [HttpGet("StartSync", Name = "StartSync")]
        public async Task<bool> StartSync()
        {


            iAPI? api = _apiInstanceManager.GetInstance("bc184f09a0214ed9ba21c6f5956de156");
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            await api.syncTask.FullSync();

            return true;
        }

        [HttpPost("InsertActivity", Name = "InsertActivity")]
        public async Task<bool> InsertActivity([FromBody] List<ActivityV1> activity)
        {
            if (activity == null)
            {
                throw new Exception("Activity data is null");
            }

            iAPI? api = _apiInstanceManager.GetInstance("bc184f09a0214ed9ba21c6f5956de156");
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            try
            {

                await context.BulkInsertOrUpdateAsync(activity.Where(a => a.ServerId != null && a.ServerId.IsNotEmpty() && a.ServerId == api.config.Id).Select(a => a.ConvertData()).ToList());
                return true;

            }
            catch (Exception ex)
            {

                Debug.WriteLine($"Error inserting activity: {ex.Message}");
                Debug.WriteLine(ex);

                return false;

            }


        }
    }
}
