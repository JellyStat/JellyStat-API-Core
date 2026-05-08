using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Services.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellystat_API_Core.Controllers
{
    [Authorize]
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
        public async Task<ActionResult> Connect(string? ServerId)
        {
            bool isAdmin = HttpContext.User.IsAdministrator();
            if (!isAdmin || ServerId == null)
            {
                ServerId = HttpContext.User.GetServerId();
            }
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return BadRequest("WebSocket request expected");

            }

            if (Global.APIInstanceManager.GetInstance(ServerId) == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var connectionId = Guid.NewGuid().ToString();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            _logger.LogInformation("WebSocket connection established: {ConnectionId}, ClientId: {ClientId}",
                connectionId, ServerId);

            await _webSocketService.HandleWebSocketAsync(webSocket, connectionId, ServerId);

            return Ok();
        }
    }
}
