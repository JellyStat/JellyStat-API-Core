using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;

namespace Jellystat_API_Core.Services.WebSocket
{
    public interface IWebSocketClientService
    {
        Task<bool> AddClientAsync(WebSocketClientConfig config);
        Task<bool> RemoveClientAsync(string clientId);
        Task<bool> SendMessageAsync(string clientId, string message);
        Task<bool> SendObjectAsync(string clientId, object obj);
        Task<bool> SendBinaryAsync(string clientId, byte[] data);
        Task<bool> ConnectClientAsync(string clientId);
        Task DisconnectClientAsync(string clientId);
        Task DisconnectAllAsync();
        WebSocketClientState GetClientState(string clientId);
        IEnumerable<string> GetAllClientIds();
        bool ClientExists(string clientId);
    }
}
