using System.Diagnostics;

namespace Jellystat_API_Core.Models.WebSocket
{
    public class WebSocketClientConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public Dictionary<string, string>? Headers { get; set; }
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
        public int MaxReconnectAttempts { get; set; } = 10;
        public bool AutoReconnect { get; set; } = true;
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

        // Per-client event handlers
        public Action<WebSocketMessage>? OnMessageReceived { get; set; } = message => { Debug.WriteLine("Message received: " + message); };
        public Action<string>? OnConnected { get; set; } = clientId => { Debug.WriteLine("Client connected: " + clientId); };
        public Action<string>? OnDisconnected { get; set; } = clientId => { Debug.WriteLine("Client disconnected: " + clientId); };
        public Action<string, Exception>? OnError { get; set; } = (clientId, error) => { Debug.WriteLine($"Error on client {clientId}: {error}"); };

        public WebSocketClientConfig() { }
    }
}
