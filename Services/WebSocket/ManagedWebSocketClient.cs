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
        private bool _maxAttemptsReached;
        private bool _disposed;

        public string ClientId => _config.Id;
        private string LogPrefix => $"[Websocket] {ClientId} {_config.ServerName}";
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
                _logger?.LogWarning("{Prefix} connect requested while state is {State}", LogPrefix, State);
                return false;
            }

            try
            {
                _logger?.LogInformation("{Prefix} connecting to {Url}", LogPrefix, _config.Url);
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
                _maxAttemptsReached = false;

                _receiveTask = Task.Run(ReceiveLoopAsync);
                _keepAliveTask = Task.Run(KeepAliveLoopAsync);

                OnConnected?.Invoke(this, ClientId);
                _logger?.LogInformation("{Prefix} connected to {Url}", LogPrefix, _config.Url);

                return true;
            }
            catch (Exception ex)
            {
                State = WebSocketClientState.Failed;
                _logger?.LogError(ex, "{Prefix} failed to connect to {Url}", LogPrefix, _config.Url);
                OnError?.Invoke(this, (ClientId, ex));

                await HandleReconnectAsync("Connection failure");

                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            _logger?.LogInformation("{Prefix} disconnect requested while client state is {State} and socket state is {SocketState}",
                LogPrefix, State, _webSocket?.State);

            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested disconnect", CancellationToken.None);
                    _logger?.LogInformation("{Prefix} close frame sent with status {CloseStatus}", LogPrefix, WebSocketCloseStatus.NormalClosure);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "{Prefix} error during disconnect", LogPrefix);
                }
            }

            State = WebSocketClientState.Disconnected;
            OnDisconnected?.Invoke(this, ClientId);
            _logger?.LogInformation("{Prefix} disconnected", LogPrefix);
        }

        public async Task<bool> SendAsync(string message)
        {
            if (State != WebSocketClientState.Connected || _webSocket?.State != WebSocketState.Open)
            {
                _logger?.LogWarning("{Prefix} cannot send text message while client state is {State} and socket state is {SocketState}",
                    LogPrefix, State, _webSocket?.State);
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
                _logger?.LogError(ex, "{Prefix} failed to send text message", LogPrefix);
                OnError?.Invoke(this, (ClientId, ex));
                return false;
            }
        }

        public async Task<bool> SendBinaryAsync(byte[] data)
        {
            if (State != WebSocketClientState.Connected || _webSocket?.State != WebSocketState.Open)
            {
                _logger?.LogWarning("{Prefix} cannot send binary message while client state is {State} and socket state is {SocketState}",
                    LogPrefix, State, _webSocket?.State);
                return false;
            }

            try
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, _cancellationTokenSource?.Token ?? CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{Prefix} failed to send binary message", LogPrefix);
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
                            _logger?.LogWarning("{Prefix} remote endpoint requested close with status {CloseStatus} and description {CloseDescription}",
                                LogPrefix, result.CloseStatus, result.CloseStatusDescription);
                            await DisconnectAsync();

                            await HandleReconnectAsync("Remote endpoint close detected");
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
                _logger?.LogInformation("{Prefix} receive loop cancelled", LogPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{Prefix} receive loop failed", LogPrefix);
                OnError?.Invoke(this, (ClientId, ex));

                await HandleReconnectAsync("Receive loop error");
            }
        }

        private async Task HandleReconnectAsync(string reason)
        {
            if (!_config.AutoReconnect)
                return;

            if (_reconnectAttempts < _config.MaxReconnectAttempts)
            {
                await ReconnectAsync();
            }
            else
            {
                if (!_maxAttemptsReached)
                {
                    _maxAttemptsReached = true;
                    _logger?.LogWarning("{Prefix} {Reason} - reconnect limit reached ({Attempts}/{MaxAttempts}). Entering indefinite retry mode with 5-minute intervals",
                        LogPrefix, reason, _reconnectAttempts, _config.MaxReconnectAttempts);
                }
                await ReconnectAsync(fiveMinuteInterval: true);
            }
        }

        private async Task ReconnectAsync(bool fiveMinuteInterval = false)
        {
            _reconnectAttempts++;
            State = WebSocketClientState.Reconnecting;

            // Use 5-minute interval when max attempts reached, otherwise use configured delay
            TimeSpan delay = fiveMinuteInterval ? TimeSpan.FromMinutes(5) : _config.ReconnectDelay;

            if (fiveMinuteInterval)
            {
                _logger?.LogWarning("{Prefix} reconnection scheduled (indefinite retry mode) in 5 minutes", LogPrefix);
            }
            else
            {
                _logger?.LogWarning("{Prefix} connection dropped, reconnect attempt {Attempt}/{MaxAttempts} in {Delay}",
                    LogPrefix, _reconnectAttempts, _config.MaxReconnectAttempts, delay);
            }

            await Task.Delay(delay);
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
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "{Prefix} error sending keepalive message", LogPrefix);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("{Prefix} keepalive loop cancelled", LogPrefix);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{Prefix} keepalive loop failed", LogPrefix);
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
