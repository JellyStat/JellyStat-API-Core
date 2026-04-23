namespace Jellystat_API_Core.Services.WebSocket
{
    public interface IWebSocketServerService
    {
        Task HandleWebSocketAsync(System.Net.WebSockets.WebSocket webSocket, string connectionId, string serverId);
        Task BroadcastMessageAsync(string message, string? serverId = null);
        Task BroadcastObjectAsync(object obj, string? serverId = null);
        IEnumerable<string> GetConnectedClients();
        int GetConnectionCount();
        bool IsClientConnected(string connectionId);
    }
}
