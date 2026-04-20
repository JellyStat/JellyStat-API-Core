# WebSocket Client Service

A comprehensive WebSocket client management service for .NET 9 that allows you to manage multiple WebSocket client connections simultaneously.

## Features

- ✅ **Multiple Client Management**: Manage multiple WebSocket connections with unique identifiers
- ✅ **Auto-Reconnect**: Automatic reconnection with configurable retry attempts and delays
- ✅ **Event-Driven**: Subscribe to connection, disconnection, message, and error events
- ✅ **Binary & Text Support**: Send and receive both text and binary messages
- ✅ **Thread-Safe**: Built with concurrent collections for thread-safe operations
- ✅ **Dependency Injection Ready**: Registered as a singleton service
- ✅ **Logging Support**: Integrated with ILogger for comprehensive logging
- ✅ **Keep-Alive**: Configurable keep-alive intervals

## Project Structure

```
Services/
  WebSocket/
    ├── IWebSocketClientService.cs         # Service interface
    ├── WebSocketClientService.cs          # Main service implementation
    └── ManagedWebSocketClient.cs          # Individual WebSocket client wrapper

Models/
  WebSocket/
    ├── WebSocketClientConfig.cs           # Configuration model
    └── WebSocketMessage.cs                # Message model

Enums/
  └── WebSocketClientState.cs              # Client state enumeration

Controllers/
  └── WebSocketController.cs               # Example REST API controller
```

## Usage

### 1. Basic Setup (Already Configured)

The service is already registered in `Program.cs`:

```csharp
builder.Services.AddSingleton<IWebSocketClientService, WebSocketClientService>();
```

### 2. Inject the Service

```csharp
public class MyService
{
    private readonly IWebSocketClientService _webSocketService;

    public MyService(IWebSocketClientService webSocketService)
    {
        _webSocketService = webSocketService;
    }
}
```

### 3. Add a WebSocket Client

```csharp
var config = new WebSocketClientConfig
{
    Id = "jellyfin-server-1",
    Url = "wss://your-server.com/websocket",
    Headers = new Dictionary<string, string>
    {
        { "Authorization", "Bearer your-token" }
    },
    AutoReconnect = true,
    ReconnectDelay = TimeSpan.FromSeconds(5),
    MaxReconnectAttempts = 10,
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    // Configure per-client event handlers
    OnMessageReceived = (message) =>
    {
        Console.WriteLine($"[{message.ClientId}] Received: {message.Message}");
    },
    OnConnected = (clientId) =>
    {
        Console.WriteLine($"Client {clientId} connected");
    },
    OnDisconnected = (clientId) =>
    {
        Console.WriteLine($"Client {clientId} disconnected");
    },
    OnError = (clientId, error) =>
    {
        Console.WriteLine($"Error on client {clientId}: {error.Message}");
    }
};

await _webSocketService.AddClientAsync(config);
```

### 4. Per-Client Event Handlers

Each WebSocket client can have its own event handlers configured in the `WebSocketClientConfig`:

```csharp
var config = new WebSocketClientConfig
{
    Id = "jellyfin-server-1",
    Url = "wss://your-server.com/websocket",
    // Configure event handlers specific to this client
    OnMessageReceived = HandleMessage,
    OnConnected = HandleConnected,
    OnDisconnected = HandleDisconnected,
    OnError = HandleError
};

void HandleMessage(WebSocketMessage message)
{
    // Handle message from this specific client
}

void HandleConnected(string clientId)
{
    // Handle connection for this specific client
}

void HandleDisconnected(string clientId)
{
    // Handle disconnection for this specific client
}

void HandleError(string clientId, Exception error)
{
    // Handle errors for this specific client
}
```

### 5. Send Messages

```csharp
// Send text message
await _webSocketService.SendMessageAsync("jellyfin-server-1", "Hello, WebSocket!");

// Send binary data
byte[] binaryData = Encoding.UTF8.GetBytes("Binary message");
await _webSocketService.SendBinaryAsync("jellyfin-server-1", binaryData);
```

### 6. Manage Connections

```csharp
// Connect a client manually
await _webSocketService.ConnectClientAsync("jellyfin-server-1");

// Disconnect a specific client
await _webSocketService.DisconnectClientAsync("jellyfin-server-1");

// Disconnect all clients
await _webSocketService.DisconnectAllAsync();

// Remove a client (disconnects and removes from management)
await _webSocketService.RemoveClientAsync("jellyfin-server-1");
```

### 7. Query Client Status

```csharp
// Check if client exists
bool exists = _webSocketService.ClientExists("jellyfin-server-1");

// Get client state
WebSocketClientState state = _webSocketService.GetClientState("jellyfin-server-1");
// States: Disconnected, Connecting, Connected, Reconnecting, Failed

// Get all client IDs
IEnumerable<string> clientIds = _webSocketService.GetAllClientIds();
```

## REST API Examples

### Add a Client
```bash
POST /api/websocket/clients
Content-Type: application/json

{
  "id": "jellyfin-server-1",
  "url": "wss://your-server.com/websocket",
  "headers": {
    "Authorization": "Bearer your-token"
  },
  "autoReconnect": true,
  "reconnectDelay": "00:00:05",
  "maxReconnectAttempts": 10,
  "keepAliveInterval": "00:00:30"
}
```

### Get All Clients
```bash
GET /api/websocket/clients
```

### Get Client State
```bash
GET /api/websocket/clients/jellyfin-server-1/state
```

### Connect a Client
```bash
POST /api/websocket/clients/jellyfin-server-1/connect
```

### Send a Message
```bash
POST /api/websocket/clients/jellyfin-server-1/send
Content-Type: application/json

{
  "message": "Hello, WebSocket!"
}
```

### Disconnect a Client
```bash
POST /api/websocket/clients/jellyfin-server-1/disconnect
```

### Remove a Client
```bash
DELETE /api/websocket/clients/jellyfin-server-1
```

## Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | string | - | Unique identifier for the client |
| `Url` | string | - | WebSocket server URL (ws:// or wss://) |
| `Headers` | Dictionary<string, string> | null | Custom headers to send with connection |
| `AutoReconnect` | bool | true | Enable automatic reconnection |
| `ReconnectDelay` | TimeSpan | 5 seconds | Delay between reconnection attempts |
| `MaxReconnectAttempts` | int | 10 | Maximum number of reconnection attempts |
| `KeepAliveInterval` | TimeSpan | 30 seconds | Interval for keep-alive pings |
| `OnMessageReceived` | Action<WebSocketMessage> | null | Handler for received messages |
| `OnConnected` | Action<string> | null | Handler for connection events |
| `OnDisconnected` | Action<string> | null | Handler for disconnection events |
| `OnError` | Action<string, Exception> | null | Handler for error events |

## Client States

- **Disconnected**: Client is not connected
- **Connecting**: Client is establishing connection
- **Connected**: Client is successfully connected
- **Reconnecting**: Client is attempting to reconnect
- **Failed**: Connection attempt failed

## Thread Safety

The service uses `ConcurrentDictionary` for thread-safe operations and is designed to be used as a singleton service.

## Example: Managing Multiple Jellyfin Servers

```csharp
// Add multiple Jellyfin servers
var servers = new[]
{
    ("server-1", "wss://jellyfin1.example.com/socket"),
    ("server-2", "wss://jellyfin2.example.com/socket"),
    ("server-3", "wss://jellyfin3.example.com/socket")
};

foreach (var (id, url) in servers)
{
    await _webSocketService.AddClientAsync(new WebSocketClientConfig
    {
        Id = id,
        Url = url,
        AutoReconnect = true,
        MaxReconnectAttempts = 5
    });
}

// Listen for messages from all servers
_webSocketService.OnMessageReceived += (sender, message) =>
{
    _logger.LogInformation($"Server {message.ClientId}: {message.Message}");
};
```

## Cleanup

The service implements `IDisposable` and will automatically clean up all connections when the application shuts down.

## License

This code is part of the JellyStat-API-Core project.
