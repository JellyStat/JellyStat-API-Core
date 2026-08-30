using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Jellyfin;
using Jellystat_API_Core.Models.WebSocket;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

namespace Jellystat_API_Core.Services.WebSocket
{
    public class ManagedWebSocketClient : IDisposable
    {
        private readonly WebSocketClientConfig _config;
        private readonly ILogger<ManagedWebSocketClient>? _logger;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private Task? _keepAliveTask;
        private int _reconnectAttempts;
        private bool _disposed;

        public string ClientId => _config.Id;
        public WebSocketClientState State { get; private set; } = WebSocketClientState.Disconnected;

        public event EventHandler<WebSocketMessage>? OnMessageReceived;
        public event EventHandler<string>? OnConnected;
        public event EventHandler<string>? OnDisconnected;
        public event EventHandler<(string ClientId, Exception Error)>? OnError;

        public ManagedWebSocketClient(WebSocketClientConfig config, ILogger<ManagedWebSocketClient>? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
        }

        public async Task<bool> ConnectAsync()
        {
            if (State == WebSocketClientState.Connected || State == WebSocketClientState.Connecting)
            {
                _logger?.LogWarning("Client {ClientId} is already connected or connecting", ClientId);
                return false;
            }

            try
            {
                State = WebSocketClientState.Connecting;
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();

                if (_config.Headers != null)
                {
                    foreach (var header in _config.Headers)
                    {
                        _webSocket.Options.SetRequestHeader(header.Key, header.Value);
                    }
                }

                _webSocket.Options.KeepAliveInterval = _config.KeepAliveInterval;

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                await _webSocket.ConnectAsync(new Uri(_config.Url), _cancellationTokenSource.Token);

                State = WebSocketClientState.Connected;
                _reconnectAttempts = 0;

                _receiveTask = Task.Run(ReceiveLoopAsync);
                _keepAliveTask = Task.Run(KeepAliveLoopAsync);

                OnConnected?.Invoke(this, ClientId);
                _logger?.LogInformation("Client {ClientId} connected to {Url}", ClientId, _config.Url);

                return true;
            }
            catch (Exception ex)
            {
                State = WebSocketClientState.Failed;
                _logger?.LogError(ex, "Failed to connect client {ClientId} to {Url}", ClientId, _config.Url);
                OnError?.Invoke(this, (ClientId, ex));

                if (_config.AutoReconnect && _reconnectAttempts < _config.MaxReconnectAttempts)
                {
                    await ReconnectAsync();
                }

                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested disconnect", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during disconnect for client {ClientId}", ClientId);
                }
            }

            State = WebSocketClientState.Disconnected;
            OnDisconnected?.Invoke(this, ClientId);
            _logger?.LogInformation("Client {ClientId} disconnected", ClientId);
        }

        public async Task<bool> SendAsync(string message)
        {
            if (State != WebSocketClientState.Connected || _webSocket?.State != WebSocketState.Open)
            {
                _logger?.LogWarning("Cannot send message on client {ClientId} - not connected", ClientId);
                return false;
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cancellationTokenSource?.Token ?? CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message on client {ClientId}", ClientId);
                OnError?.Invoke(this, (ClientId, ex));
                return false;
            }
        }

        public async Task<bool> SendBinaryAsync(byte[] data)
        {
            if (State != WebSocketClientState.Connected || _webSocket?.State != WebSocketState.Open)
            {
                _logger?.LogWarning("Cannot send binary data on client {ClientId} - not connected", ClientId);
                return false;
            }

            try
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, _cancellationTokenSource?.Token ?? CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send binary data on client {ClientId}", ClientId);
                OnError?.Invoke(this, (ClientId, ex));
                return false;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 4];

            try
            {
                while (_webSocket?.State == WebSocketState.Open && !(_cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                {
                    using var messageStream = new MemoryStream();
                    WebSocketReceiveResult result;
                    WebSocketMessageType messageType = WebSocketMessageType.Text;

                    // Keep reading fragments until we get the complete message
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource?.Token ?? CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync();

                            if (_config.AutoReconnect && _reconnectAttempts < _config.MaxReconnectAttempts)
                            {
                                await ReconnectAsync();
                            }
                            return;
                        }

                        messageType = result.MessageType;
                        messageStream.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage);

                    // Now we have the complete message
                    var completeMessageData = messageStream.ToArray();

                    var message = new WebSocketMessage
                    {
                        ClientId = ClientId,
                        ReceivedAt = DateTime.UtcNow,
                        IsBinary = messageType == WebSocketMessageType.Binary
                    };

                    if (messageType == WebSocketMessageType.Text)
                    {
                        message.Message = Encoding.UTF8.GetString(completeMessageData);
                    }
                    else
                    {
                        message.BinaryData = completeMessageData;
                    }

                    OnMessageReceived?.Invoke(this, message);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Receive loop cancelled for client {ClientId}", ClientId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in receive loop for client {ClientId}", ClientId);
                OnError?.Invoke(this, (ClientId, ex));

                if (_config.AutoReconnect && _reconnectAttempts < _config.MaxReconnectAttempts)
                {
                    await ReconnectAsync();
                }
            }
        }

        private async Task ReconnectAsync()
        {
            _reconnectAttempts++;
            State = WebSocketClientState.Reconnecting;

            _logger?.LogInformation("Attempting to reconnect client {ClientId} (attempt {Attempt}/{Max})",
                ClientId, _reconnectAttempts, _config.MaxReconnectAttempts);

            await Task.Delay(_config.ReconnectDelay);
            await ConnectAsync();
        }

        private async Task KeepAliveLoopAsync()
        {
            try
            {
                while (State == WebSocketClientState.Connected && !(_cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                {
                    await Task.Delay(_config.KeepAliveInterval, _cancellationTokenSource?.Token ?? CancellationToken.None);

                    if (State != WebSocketClientState.Connected || _webSocket?.State != WebSocketState.Open)
                    {
                        break;
                    }

                    try
                    {
                        var keepAliveMessage = new EventMessage<object?>(
                            Data: null,
                            MessageType: MessageTypes.KeepAlive
                        );

                        var json = JsonConvert.SerializeObject(keepAliveMessage);

                        bool sent = await SendAsync(json);

                        if (!sent)
                        {
                            _logger?.LogWarning("Failed to send keepalive message for client {ClientId}", ClientId);
                        }
                        else
                        {
                            _logger?.LogDebug("Keepalive message sent for client {ClientId}", ClientId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error sending keepalive message for client {ClientId}", ClientId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Keepalive loop cancelled for client {ClientId}", ClientId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in keepalive loop for client {ClientId}", ClientId);
            }
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(5));
            _keepAliveTask?.Wait(TimeSpan.FromSeconds(5));

            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();

            _disposed = true;
        }
    }
}
