using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.WebSocket;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace Jellystat_API_Core.Services.WebSocket
{
    public class WebSocketClientService : IWebSocketClientService, IDisposable
    {
        private readonly ConcurrentDictionary<string, ManagedWebSocketClient> _clients;
        private readonly ILogger<WebSocketClientService> _logger;
        private readonly ILogger<ManagedWebSocketClient> _clientLogger;
        private bool _disposed;

        public WebSocketClientService(
            ILogger<WebSocketClientService> logger,
            ILogger<ManagedWebSocketClient> clientLogger)
        {
            _clients = new ConcurrentDictionary<string, ManagedWebSocketClient>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientLogger = clientLogger ?? throw new ArgumentNullException(nameof(clientLogger));
        }

        public async Task<bool> AddClientAsync(WebSocketClientConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(config.Id))
                throw new ArgumentException("Client ID cannot be empty", nameof(config.Id));

            if (_clients.ContainsKey(config.Id))
            {
                _logger.LogWarning("Client with ID {ClientId} already exists", config.Id);
                return false;
            }

            try
            {
                var client = new ManagedWebSocketClient(config, _clientLogger);

                // Wire up per-client event handlers from config
                if (config.OnMessageReceived != null)
                {
                    client.OnMessageReceived += (sender, message) => config.OnMessageReceived(message);
                }

                if (config.OnConnected != null)
                {
                    client.OnConnected += (sender, clientId) => config.OnConnected(clientId);
                }

                if (config.OnDisconnected != null)
                {
                    client.OnDisconnected += (sender, clientId) => config.OnDisconnected(clientId);
                }

                if (config.OnError != null)
                {
                    client.OnError += (sender, error) => config.OnError(error.ClientId, error.Error);
                }

                if (_clients.TryAdd(config.Id, client))
                {
                    _logger.LogInformation("Added WebSocket client {ClientId}", config.Id);

                    if (config.AutoReconnect)
                    {
                        await client.ConnectAsync();
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add WebSocket client {ClientId}", config.Id);
                return false;
            }
        }

        public async Task<bool> RemoveClientAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            if (_clients.TryRemove(clientId, out var client))
            {
                await client.DisconnectAsync();
                client.Dispose();
                _logger.LogInformation("Removed WebSocket client {ClientId}", clientId);
                return true;
            }

            _logger.LogWarning("Client {ClientId} not found for removal", clientId);
            return false;
        }

        public async Task<bool> SendMessageAsync(string clientId, string message)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            if (!_clients.TryGetValue(clientId, out var client))
            {
                _logger.LogWarning("Client {ClientId} not found", clientId);
                return false;
            }

            return await client.SendAsync(message);
        }

        public async Task<bool> SendObjectAsync(string clientId, object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            var message = JsonConvert.SerializeObject(obj);
            var sendTasks = _clients.Values.Select(client => client.SendAsync(message));
            var results = await Task.WhenAll(sendTasks);
            return results.All(result => result);
        }

        public async Task<bool> SendBinaryAsync(string clientId, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (!_clients.TryGetValue(clientId, out var client))
            {
                _logger.LogWarning("Client {ClientId} not found", clientId);
                return false;
            }

            return await client.SendBinaryAsync(data);
        }

        public async Task<bool> ConnectClientAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            if (!_clients.TryGetValue(clientId, out var client))
            {
                _logger.LogWarning("Client {ClientId} not found", clientId);
                return false;
            }

            return await client.ConnectAsync();
        }

        public async Task DisconnectClientAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            if (_clients.TryGetValue(clientId, out var client))
            {
                await client.DisconnectAsync();
            }
            else
            {
                _logger.LogWarning("Client {ClientId} not found for disconnection", clientId);
            }
        }

        public async Task DisconnectAllAsync()
        {
            _logger.LogInformation("Disconnecting all WebSocket clients");

            var disconnectTasks = _clients.Values.Select(client => client.DisconnectAsync());
            await Task.WhenAll(disconnectTasks);
        }

        public WebSocketClientState GetClientState(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

            return _clients.TryGetValue(clientId, out var client)
                ? client.State
                : WebSocketClientState.Disconnected;
        }

        public IEnumerable<string> GetAllClientIds()
        {
            return _clients.Keys.ToList();
        }

        public bool ClientExists(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return false;

            return _clients.ContainsKey(clientId);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            DisconnectAllAsync().Wait(TimeSpan.FromSeconds(10));

            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }

            _clients.Clear();
            _disposed = true;
        }


    }
}
