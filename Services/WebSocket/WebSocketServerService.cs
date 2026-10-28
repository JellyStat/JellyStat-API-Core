using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.WebSocket;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Jellystat_API_Core.Services.WebSocket
{


    public class WebSocketServerService : IWebSocketServerService
    {
        private readonly ConcurrentDictionary<string, (string ServerId, System.Net.WebSockets.WebSocket WebSocket, string? ScopedUserId)> _connections;
        private readonly ILogger<WebSocketServerService> _logger;

        public WebSocketServerService(ILogger<WebSocketServerService> logger)
        {
            _connections = new ConcurrentDictionary<string, (string ServerId, System.Net.WebSockets.WebSocket WebSocket, string? ScopedUserId)>();
            _logger = logger;
        }

        public async Task HandleWebSocketAsync(System.Net.WebSockets.WebSocket webSocket, string connectionId, string serverId, string? scopedUserId = null)
        {
            if (!_connections.TryAdd(connectionId, (serverId, webSocket, scopedUserId)))
            {
                _logger.LogWarning("Connection {ConnectionId} already exists", connectionId);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Duplicate connection", CancellationToken.None);
                return;
            }

            _logger.LogInformation("WebSocket connection {ConnectionId} established. Total connections: {Count}",
                connectionId, _connections.Count);

            try
            {
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                while (!result.CloseStatus.HasValue)
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogInformation("Received message from {ConnectionId}: {Message}", connectionId, message);

                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        _logger.LogInformation("Received binary message from {ConnectionId}, length: {Length}",
                            connectionId, result.Count);
                    }

                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }

                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
                _logger.LogInformation("WebSocket connection {ConnectionId} closed normally", connectionId);
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "WebSocket error for connection {ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WebSocket connection {ConnectionId}", connectionId);
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                _logger.LogInformation("WebSocket connection {ConnectionId} removed. Total connections: {Count}",
                    connectionId, _connections.Count);

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Server error", CancellationToken.None);
                }

                webSocket.Dispose();
            }
        }

        public async Task BroadcastMessageAsync(string message, string? serverId)
        {
            var tasks = new List<Task>();
            var messageBytes = Encoding.UTF8.GetBytes(message);

            foreach (var connection in _connections.Where(c => serverId == null || c.Value.ServerId == serverId))
            {
                if (connection.Value.WebSocket.State == WebSocketState.Open)
                {
                    tasks.Add(SendBytesAsync(connection.Key, connection.Value.WebSocket, messageBytes));
                }
            }

            await Task.WhenAll(tasks);
        }

        public async Task BroadcastObjectAsync<T>(JellystatWebsocketMessage<T> obj, string? serverId = null)
        {
            var json = JsonConvert.SerializeObject(obj, Global.jsonSettings);
            await BroadcastMessageAsync(json, serverId);
        }


        public async Task BroadcastSessionsAsync(IList<BaseSessionItem> sessions, string serverId)
        {
            // Serialise once per distinct audience (everything, or one user's sessions) rather than per connection
            var payloads = new Dictionary<string, byte[]>();
            byte[] PayloadFor(string? userId)
            {
                string cacheKey = userId ?? string.Empty;
                if (!payloads.TryGetValue(cacheKey, out byte[]? bytes))
                {
                    IList<BaseSessionItem> visible = userId == null
                        ? sessions
                        : sessions.Where(s => s.UserId == userId).ToList();
                    var message = new JellystatWebsocketMessage<IList<BaseSessionItem>>
                    {
                        Type = WebsocketServerTypes.Sessions,
                        Data = visible
                    };
                    bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, Global.jsonSettings));
                    payloads[cacheKey] = bytes;
                }
                return bytes;
            }

            var tasks = new List<Task>();
            foreach (var connection in _connections.Where(c => c.Value.ServerId == serverId))
            {
                if (connection.Value.WebSocket.State == WebSocketState.Open)
                {
                    tasks.Add(SendBytesAsync(connection.Key, connection.Value.WebSocket, PayloadFor(connection.Value.ScopedUserId)));
                }
            }

            await Task.WhenAll(tasks);
        }

        public IEnumerable<string> GetConnectedClients()
        {
            return _connections
                .Where(c => c.Value.WebSocket.State == WebSocketState.Open)
                .Select(c => c.Key)
                .ToList();
        }

        public int GetConnectionCount()
        {
            return _connections.Count(c => c.Value.WebSocket.State == WebSocketState.Open);
        }

        public bool IsClientConnected(string connectionId)
        {
            return _connections.TryGetValue(connectionId, out var webSocket) &&
                   webSocket.WebSocket.State == WebSocketState.Open;
        }

        private async Task SendBytesAsync(string connectionId, System.Net.WebSockets.WebSocket webSocket, byte[] messageBytes)
        {
            try
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(messageBytes, 0, messageBytes.Length),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "Failed to send message to {ConnectionId}", connectionId);
                _connections.TryRemove(connectionId, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to {ConnectionId}", connectionId);
            }
        }


    }
}
