using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Filters;
using Jellystat_API_Core.Services.WebSocket;
using Microsoft.AspNetCore.Mvc;

namespace Jellystat_API_Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebSocketController : ControllerBase
    {
        private readonly IWebSocketServerService _webSocketService;
        private readonly ILogger<WebSocketController> _logger;

        public WebSocketController(
            IWebSocketServerService webSocketService,
            ILogger<WebSocketController> logger)
        {
            _webSocketService = webSocketService;
            _logger = logger;
        }

        [HttpGet]
        [ValidateHeader("ServerId")] // Example: Validate a required header
        public async Task Connect()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Extract the validated header value
            var serverId = HttpContext.GetValidatedHeader("ServerId");

            if (serverId == null || Global.APIInstanceManager.GetInstance(serverId) == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                await HttpContext.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("No Instance found for provided Server Id"));
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            _logger.LogInformation("WebSocket connection established: {ConnectionId}, ClientId: {ClientId}",
                connectionId, serverId);

            await _webSocketService.HandleWebSocketAsync(webSocket, connectionId, serverId);
        }
    }
}
