using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.WebSocket;

namespace Jellystat_API_Core.Services.WebSocket
{
    public interface IWebSocketServerService
    {
        /// <param name="scopedUserId">When set, this connection only receives live sessions for that user.</param>
        Task HandleWebSocketAsync(System.Net.WebSockets.WebSocket webSocket, string connectionId, string serverId, string? scopedUserId = null);
        Task BroadcastMessageAsync(string message, string? serverId = null);
        Task BroadcastObjectAsync<T>(JellystatWebsocketMessage<T> obj, string? serverId = null);
        /// <summary>Sends live sessions to a server's connections, filtered per connection to what each may see.</summary>
        Task BroadcastSessionsAsync(IList<BaseSessionItem> sessions, string serverId);
        IEnumerable<string> GetConnectedClients();
        int GetConnectionCount();
        bool IsClientConnected(string connectionId);
    }
}
