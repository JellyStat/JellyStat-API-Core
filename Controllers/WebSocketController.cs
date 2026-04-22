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
        public async Task Connect()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            _logger.LogInformation("WebSocket connection established: {ConnectionId}", connectionId);

            await _webSocketService.HandleWebSocketAsync(webSocket, connectionId);
        }
    }
}
