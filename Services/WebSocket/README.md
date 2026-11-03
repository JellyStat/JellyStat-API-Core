# WebSocket services

Two singleton services, both registered in `Program.cs` and also exposed as `Global.WebSocketClientService` and `Global.WebSocketServerService`:

| Service | Direction | Used for |
|---------|-----------|----------|
| `IWebSocketClientService` | API → Jellyfin/Emby | Listening to each media server's `/socket` for session and library events |
| `IWebSocketServerService` | Web UI → API | Pushing live sessions and task progress to connected UI clients |

## Files

```
Services/WebSocket/
  IWebSocketClientService.cs, WebSocketClientService.cs   # Outbound client manager
  ManagedWebSocketClient.cs                               # One outbound connection (reconnect, keep-alive)
  IWebSocketServerService.cs, WebSocketServerService.cs   # Inbound connections from the UI

Models/WebSocket/
  WebSocketClientConfig.cs       # Outbound connection settings and handlers
  WebSocketMessage.cs            # Message received from a media server
  JellystatWebsocketMessage.cs   # Envelope sent to the UI: { type, data }
  TaskQueueUpdate.cs

Enums/
  WebSocketClientState.cs        # Disconnected, Connecting, Connected, Reconnecting, Failed
  WebsocketServerTypes.cs        # Sessions, TaskStart, TaskUpdate, TaskLogUpdate, TaskEnd

Controllers/
  WebSocketController.cs         # GET /WebSocket: accepts UI connections
```

## Client: media server connections

`JellyfinAPI` and `EmbyAPI` add one client per configured server when they're created, keyed by the server id. The settings come from `Server.WebSocketConfig` (`Models/Jellystat/Server.cs`), which turns the server URL into `ws(s)://<server>/socket` and sends the API key in a `MediaBrowser Token` header. The API class then attaches its own handlers:

```csharp
if (!Global.WebSocketClientService.ClientExists(config.Id))
{
    WebSocketClientConfig webSocketConfig = config.WebSocketConfig;
    webSocketConfig.OnMessageReceived = OnMessageReceived;
    webSocketConfig.OnConnected = OnConnected;
    webSocketConfig.OnDisconnected = OnDisconnected;
    webSocketConfig.OnError = OnError;
    Global.WebSocketClientService.AddClientAsync(webSocketConfig);
}
```

Handlers are per client and set on the config. The service has no events of its own.

### IWebSocketClientService

| Method | Description |
|--------|-------------|
| `AddClientAsync(config)` | Adds a client; connects it straight away when `AutoReconnect` is on |
| `RemoveClientAsync(clientId)` | Disconnects and removes a client |
| `ConnectClientAsync(clientId)` | Connects an existing client |
| `DisconnectClientAsync(clientId)` / `DisconnectAllAsync()` | Closes one or all connections |
| `SendMessageAsync(clientId, text)` | Sends a text message |
| `SendObjectAsync(clientId, obj)` | Serialises an object to JSON and sends it |
| `SendBinaryAsync(clientId, bytes)` | Sends a binary message |
| `GetClientState(clientId)` | Returns a `WebSocketClientState` |
| `GetAllClientIds()` / `ClientExists(clientId)` | Lookups |

### WebSocketClientConfig

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Client id (the server id) |
| `Url` | `string` | `""` | `ws://` or `wss://` URL |
| `ServerName` | `string` | `""` | Server name, for logging |
| `Headers` | `Dictionary<string, string>?` | `null` | Headers sent when connecting |
| `AutoReconnect` | `bool` | `true` | Reconnect after the connection drops |
| `ReconnectDelay` | `TimeSpan` | 5 seconds | Wait between reconnect attempts |
| `MaxReconnectAttempts` | `int` | `10` | Attempts before the client is marked `Failed` |
| `KeepAliveInterval` | `TimeSpan` | 30 seconds | Keep-alive ping interval |
| `OnMessageReceived` | `Action<WebSocketMessage>?` | debug log | Message handler |
| `OnConnected` | `Action<string>?` | debug log | Called with the client id |
| `OnDisconnected` | `Action<string>?` | debug log | Called with the client id |
| `OnError` | `Action<string, Exception>?` | debug log | Called with the client id and the error |

## Server: UI connections

The UI connects to `GET /WebSocket`. The connection needs an access token; because browsers can't set headers on a WebSocket, the token can be passed as `?token=<jwt>`, which the JWT handler in `Program.cs` reads for `/WebSocket` paths only.

- Admins can pass `?ServerId=<id>` to choose a server. Everyone else is tied to the server in their token.
- Remote logins (users signed in through Jellyfin/Emby) only receive their own live sessions.

Messages are JSON in a `JellystatWebsocketMessage<T>` envelope, with `type` from `WebsocketServerTypes`:

- `Sessions`: live sessions, sent by `ActivityMonitor` through `BroadcastSessionsAsync`, filtered per connection
- `TaskStart`, `TaskUpdate`, `TaskLogUpdate`, `TaskEnd`: task progress, sent by the task scheduler and task logging through `BroadcastObjectAsync`

`BroadcastMessageAsync` and `BroadcastObjectAsync` send to every connection, or only to one server's connections when a `serverId` is passed. Messages the UI sends are logged and otherwise ignored.
