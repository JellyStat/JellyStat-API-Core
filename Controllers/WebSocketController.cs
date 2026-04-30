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
        public async Task<ActionResult> Connect([FromHeader(Name = "ServerId")] string ServerId)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return BadRequest("WebSocket request expected");

            }

            if (Global.APIInstanceManager.GetInstance(ServerId) == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                await HttpContext.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes("No Instance found for provided Server Id"));
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
