using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.WebSocket;

namespace Jellystat_API_Core.Examples
{
    public class APIInstanceManagerExample
    {
        private readonly IAPIInstanceManager _apiManager;
        private readonly IWebSocketClientService _websocketService;

        public APIInstanceManagerExample(IAPIInstanceManager apiManager, IWebSocketClientService websocketService)
        {
            _apiManager = apiManager;
            _websocketService = websocketService;
        }

        public void RegisterMultipleServers()
        {
            // Example: Register multiple Jellyfin servers
            var server1 = new Server
            {
                Id = "server-1",
                Name = "Production Server",
                URL = "http://prod-server:8096",
                APIKey = "your-api-key-1"
            };

            var server2 = new Server
            {
                Id = "server-2",
                Name = "Development Server",
                URL = "http://dev-server:8096",
                APIKey = "your-api-key-2"
            };

            // Register instances
            _apiManager.RegisterInstanceAsync(server1);
            _apiManager.RegisterInstanceAsync(server2);
        }

        public async Task<IList<BaseUser>> GetUsersFromSpecificServer(string serverId)
        {
            // Retrieve the API instance for a specific server
            var apiInstance = _apiManager.GetInstance(serverId);

            if (apiInstance == null)
            {
                throw new InvalidOperationException($"No API instance found for server ID: {serverId}");
            }

            // Use the instance to fetch data
            return await apiInstance.GetUsers();
        }



        public async Task RemoveServerAsync(string serverId)
        {
            // Remove a server instance
            var removed = await _apiManager.RemoveInstanceAsync(serverId);

            if (removed)
            {
                Console.WriteLine($"Server {serverId} removed successfully.");
            }
            else
            {
                Console.WriteLine($"Server {serverId} not found.");
            }
        }

        public bool IsServerRegistered(string serverId)
        {
            // Check if a server is registered
            return _apiManager.HasInstance(serverId);
        }
    }
}
