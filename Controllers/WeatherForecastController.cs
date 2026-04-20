using EFCore.BulkExtensions;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Examples;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat.V1;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.WebSocket;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Jellystat_API_Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly ILogger<WebSocketServiceExample> _loggerWS;
        private readonly IWebSocketClientService _websocketService;
        private readonly IAPIInstanceManager _apiInstanceManager;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, ILogger<WebSocketServiceExample> loggerWS, IAPIInstanceManager apiInstanceManager, IWebSocketClientService websocketService)
        {
            _logger = logger;
            _loggerWS = loggerWS;
            _websocketService = websocketService;
            _apiInstanceManager = apiInstanceManager;
        }

        [HttpGet("weather", Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> GetWeatherForecast()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
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

        [HttpGet("GetLatestActivity", Name = "GetLatestActivity")]
        public async Task<IList<Data.Schema.Activity>> GetLatestActivity()
        {


            iAPI? api = _apiInstanceManager.GetInstance("bc184f09a0214ed9ba21c6f5956de156");
            if (api == null)
            {
                throw new Exception("API instance not found");
            }

            IList<Data.Schema.Activity> items = await api.activityMonitorTask.GetLatestActivity();

            return items;
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
                await Global.Database.context.BulkInsertOrUpdateAsync(activity.Where(a => a.ServerId != null && a.ServerId.IsNotEmpty() && a.ServerId == api.config.Id).Select(a => a.ConvertData()).ToList());
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
