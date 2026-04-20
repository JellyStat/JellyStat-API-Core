using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;
using Jellystat_API_Core.Services.WebSocket;

namespace Jellystat_API_Core.Examples
{
    /// <summary>
    /// Example usage of the WebSocket Client Service
    /// This demonstrates how to use the service in your application
    /// </summary>
    public class WebSocketServiceExample
    {
        private readonly IWebSocketClientService _webSocketService;
        private readonly ILogger<WebSocketServiceExample> _logger;

        public WebSocketServiceExample(
            IWebSocketClientService webSocketService,
            ILogger<WebSocketServiceExample> logger)
        {
            _webSocketService = webSocketService;
            _logger = logger;
        }



        /// <summary>
        /// Example: Connect to a Jellyfin server via WebSocket
        /// </summary>
        public async Task<bool> ConnectToJellyfinServer(string serverId, string serverUrl, string apiKey)
        {
            if (_webSocketService.ClientExists(serverId))
            {
                _logger.LogWarning("Client with ID {ServerId} already exists", serverId);
                return false;
            }
            var config = new WebSocketClientConfig
            {
                Id = serverId,
                Url = serverUrl.Replace("http", "ws") + "/socket",
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", $"MediaBrowser Token=\"{apiKey}\"" },
                    { "User-Agent", $"Jellystat" }
                },
                AutoReconnect = true,
                ReconnectDelay = TimeSpan.FromSeconds(5),
                MaxReconnectAttempts = 10,
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            return await _webSocketService.AddClientAsync(config);
        }

        /// <summary>
        /// Example: Connect to multiple servers
        /// </summary>
        public async Task ConnectToMultipleServers(Dictionary<string, (string Url, string ApiKey)> servers)
        {
            foreach (var kvp in servers)
            {
                var serverId = kvp.Key;
                var (url, apiKey) = kvp.Value;

                _logger.LogInformation("Connecting to server {ServerId}...", serverId);
                await ConnectToJellyfinServer(serverId, url, apiKey);
            }
        }

        /// <summary>
        /// Example: Send a command to a specific server
        /// </summary>
        public async Task<bool> SendCommandToServer(string serverId, string command)
        {
            if (!_webSocketService.ClientExists(serverId))
            {
                _logger.LogWarning("Server {ServerId} not found", serverId);
                return false;
            }

            var state = _webSocketService.GetClientState(serverId);
            if (state != WebSocketClientState.Connected)
            {
                _logger.LogWarning("Server {ServerId} is not connected (State: {State})", serverId, state);
                return false;
            }

            return await _webSocketService.SendMessageAsync(serverId, command);
        }

        /// <summary>
        /// Example: Broadcast a message to all connected servers
        /// </summary>
        public async Task BroadcastToAllServers(string message)
        {
            var clientIds = _webSocketService.GetAllClientIds();

            foreach (var clientId in clientIds)
            {
                var state = _webSocketService.GetClientState(clientId);
                if (state == WebSocketClientState.Connected)
                {
                    await _webSocketService.SendMessageAsync(clientId, message);
                    _logger.LogInformation("Sent message to {ClientId}", clientId);
                }
            }
        }

        /// <summary>
        /// Example: Get status of all clients
        /// </summary>
        public Dictionary<string, WebSocketClientState> GetAllClientStatuses()
        {
            var clientIds = _webSocketService.GetAllClientIds();
            var statuses = new Dictionary<string, WebSocketClientState>();

            foreach (var clientId in clientIds)
            {
                statuses[clientId] = _webSocketService.GetClientState(clientId);
            }

            return statuses;
        }

        /// <summary>
        /// Example: Reconnect failed or disconnected clients
        /// </summary>
        public async Task ReconnectFailedClients()
        {
            var clientIds = _webSocketService.GetAllClientIds();

            foreach (var clientId in clientIds)
            {
                var state = _webSocketService.GetClientState(clientId);

                if (state == WebSocketClientState.Disconnected || state == WebSocketClientState.Failed)
                {
                    _logger.LogInformation("Attempting to reconnect client {ClientId}", clientId);
                    await _webSocketService.ConnectClientAsync(clientId);
                }
            }
        }

        /// <summary>
        /// Example: Cleanup - disconnect all clients
        /// </summary>
        public async Task DisconnectAll()
        {
            _logger.LogInformation("Disconnecting all WebSocket clients...");
            await _webSocketService.DisconnectAllAsync();
        }

        /// <summary>
        /// Example: Remove a specific server
        /// </summary>
        public async Task<bool> RemoveServer(string serverId)
        {
            _logger.LogInformation("Removing server {ServerId}...", serverId);
            return await _webSocketService.RemoveClientAsync(serverId);
        }
    }
}
