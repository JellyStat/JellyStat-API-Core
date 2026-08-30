using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Classes.Tasks;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.API.Jellyfin;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Models.WebSocket;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Jellystat_API_Core.Classes.API
{
    public class JellyfinAPI : iAPI
    {
        public Server config { get; set; }
        public string Version { get; set; }
        public string UserAgent { get; set; }

        public HttpClient httpClient { get; set; }

        public List<BaseSessionItem> SessionItems { get; set; }

        public ActivityMonitor activityMonitorTask { get; set; }

        public SyncTask syncTask { get; set; }

        public Action<WebSocketMessage>? OnMessageReceived { get; set; } = message =>
        {
            if (message == null || message.Message.IsEmpty())
            {
                return;
            }

            try
            {
                EventMessage<dynamic>? wsmessage = JsonConvert.DeserializeObject<EventMessage<dynamic>>(message.Message, Global.jsonSettings);

                if (wsmessage == null || wsmessage.Data == null)
                {
                    Debug.WriteLine($"[Websocket] Unavailable to deserialize message events or is null: {wsmessage}");
                    return;
                }

                if (wsmessage.MessageType == MessageTypes.Sessions)
                {
                    try
                    {

                        iAPI? instance = Global.APIInstanceManager.GetInstance(message.ClientId);
                        if (instance == null)
                        {
                            Debug.WriteLine($"[Websocket] No API instance found for client {message.ClientId}");
                            return;
                        }
                        List<SessionItem>? sessionItems = JsonConvert.DeserializeObject<List<SessionItem>>(wsmessage.Data!.ToString(), Global.jsonSettings);
                        List<SessionItem>? filteredSessionItems = sessionItems.Where(s => s.NowPlayingItem != null
                                                                                    && s.NowPlayingItem.Type != ItemTypes.Trailer
                                                                                    && !s.NowPlayingItem.Providers.Contains("prerolls.video")
                                                                                    && !instance.excludedUsers.Any(u => u.UserId == s.UserId)
                                                                                    ).ToList();

                        instance.SessionItems = filteredSessionItems.Cast<BaseSessionItem>().ToList();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Websocket] Error deserializing session data from client {message.ClientId}: {ex}");
                    }
                }
                else if (wsmessage.MessageType == MessageTypes.KeepAlive)
                {
                    //Debug.WriteLine($"[Websocket] KeepAlive message received from client {message.ClientId}");
                }
                else if (wsmessage.MessageType == MessageTypes.ForceKeepAlive)
                {
                    //Debug.WriteLine($"[Websocket] KeepAlive message received from client {message.ClientId}");
                }
                else
                {
                    Debug.WriteLine($"[Websocket] Unhandled Message Type Received: {message.Message}");
                }


            }
            catch (JsonReaderException jsonEx)
            {
                Debug.WriteLine($"[Websocket] Invalid JSON received from client {message.ClientId}: {jsonEx.Message}");
                Debug.WriteLine($"[Websocket] Message content: {message.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Websocket] Error processing message from client {message.ClientId}: {ex}");
            }
        };
        public Action<string>? OnConnected { get; set; } = clientId =>
        {
            Debug.WriteLine("[Websocket] Client connected: " + clientId);
            EventMessage<string> message = new EventMessage<string>(
                    Data: "0,1000",
                    MessageType: MessageTypes.SessionsStart
                );

            Debug.WriteLine($"Sending Message: {JsonConvert.SerializeObject(message)}");
            _ = Global.WebSocketClientService.SendMessageAsync(clientId, JsonConvert.SerializeObject(message));

        };
        public Action<string>? OnDisconnected { get; set; } = clientId => { Debug.WriteLine("[Websocket] Client disconnected: " + clientId); };
        public Action<string, Exception>? OnError { get; set; } = (clientId, error) => { Debug.WriteLine($"[Websocket] Error on client {clientId}: {error}"); };

        public List<Data.Schema.ExcludedUsers> excludedUsers { get; set; }
        public List<Data.Schema.ExcludedLibraries> excludedLibraries { get; set; }

        public JellyfinAPI(Server config, List<Data.Schema.ExcludedUsers> excludedUsers, List<Data.Schema.ExcludedLibraries> excludedLibraries, IServiceScopeFactory serviceScopeFactory)
        {
            this.config = config;
            this.excludedUsers = excludedUsers;
            this.excludedLibraries = excludedLibraries;
            Version = "1.0.0";
            UserAgent = Global.UserAgent;
            httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.URL),
                DefaultRequestHeaders =
                {
                    { "Authorization", String.Concat("MediaBrowser Token=","\"",this.config.APIKey,"\"") },
                    { "User-Agent", UserAgent }
                }
            };

            SessionItems = new List<BaseSessionItem>();


            if (!Global.WebSocketClientService.ClientExists(config.Id))
            {
                WebSocketClientConfig webSocketConfig = config.WebSocketConfig;
                webSocketConfig.OnMessageReceived = OnMessageReceived;
                webSocketConfig.OnConnected = OnConnected;
                webSocketConfig.OnDisconnected = OnDisconnected;
                webSocketConfig.OnError = OnError;
                Global.WebSocketClientService.AddClientAsync(webSocketConfig);
            }

            activityMonitorTask = new ActivityMonitor(this, serviceScopeFactory);
            activityMonitorTask.Start();

            syncTask = new SyncTask(this, serviceScopeFactory);

        }

        public JellyfinAPI(string URL, string APIKey)
        {
            this.config = new Server { Id = "", Name = "", URL = URL, APIKey = APIKey };
            this.excludedUsers = new List<Data.Schema.ExcludedUsers>();
            this.excludedLibraries = new List<Data.Schema.ExcludedLibraries>();
            Version = "1.0.0";
            UserAgent = Global.UserAgent;
            httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.URL),
                DefaultRequestHeaders =
                {
                    { "Authorization", String.Concat("MediaBrowser Token=","\"",this.config.APIKey,"\"") },
                    { "User-Agent", UserAgent }
                }
            };

            SessionItems = new List<BaseSessionItem>();
        }

        public async Task<IList<BaseUser>> GetUsers()
        {
            var response = await httpClient.GetAsync("/Users");
            response.EnsureSuccessStatusCode();

            var responseWrapper = await response.Content.ReadAsJsonAsync<IList<User>>();
            List<User> users = responseWrapper.ToListOrDefault();
            return users.Cast<BaseUser>().ToList();
        }

        public async Task<IList<BaseUser>> GetAdmins()
        {
            IList<BaseUser> users = await GetUsers();
            return users.Where(u => u.IsAdministrator).ToList();
        }

        public async Task<BaseUser?> GetUserById(string id)
        {
            IList<BaseUser> users = await GetUsers();
            BaseUser? user = users.FirstOrDefault(u => u.Id == id);
            return user;

        }

        public async Task<IList<BaseItem>> GetItemsByIds(List<string> itemIds, int startIndex = 0, int increment = 200, int limit = 200, bool recursive = true)
        {
            if (itemIds.IsEmpty())
            {
                throw new ArgumentException("itemIds list cannot be empty.", nameof(itemIds));

            }
            var queryParams = new List<string>
            {
                $"Ids={string.Join(",", itemIds.Select(Uri.EscapeDataString))}",
                $"fields=MediaSources,DateCreated,Genres,Path",
                $"isMissing=false",
                $"excludeLocationTypes=Virtual",
                $"StartIndex={startIndex}",
                $"Limit={limit}",
                $"Recursive={recursive.ToString().ToLower()}",
                "ExcludeItemTypes=Folder,Studio,Genre",
                "sortBy=DateCreated",
                "sortOrder=Descending"
            };

            var queryString = string.Join("&", queryParams);
            var response = await httpClient.GetAsync($"/Items?{queryString}");
            response.EnsureSuccessStatusCode();

            var responseWrapper = await response.Content.ReadAsJsonAsync<PaginatedResponse<Item>>();
            return responseWrapper.Items.Cast<BaseItem>().ToList();
        }


        public async Task<IList<BaseItem>> GetItemsFromParentId(string id, List<string>? itemIds = null, int startIndex = 0, int increment = 200, int limit = 200, bool recursive = true)
        {
            var queryParams = new List<string>
            {
                $"ParentId={Uri.EscapeDataString(id)}",
                $"fields=MediaSources,DateCreated,Genres,Path",
                $"isMissing=false",
                $"excludeLocationTypes=Virtual",
                $"StartIndex={startIndex}",
                $"Limit={limit}",
                $"Recursive={recursive.ToString().ToLower()}",
                "ExcludeItemTypes=Folder,Studio,Genre",
                "sortBy=DateCreated",
                "sortOrder=Descending"

            };

            if (itemIds != null && itemIds.Any())
            {
                queryParams.Add($"Ids={string.Join(",", itemIds.Select(Uri.EscapeDataString))}");
            }

            var queryString = string.Join("&", queryParams);
            var response = await httpClient.GetAsync($"/Items?{queryString}");
            response.EnsureSuccessStatusCode();

            var responseWrapper = await response.Content.ReadAsJsonAsync<PaginatedResponse<Item>>();
            return responseWrapper.Items.Cast<BaseItem>().ToList();
        }


        public async Task<IList<BaseLibrary>> GetLibraries()
        {
            var response = await httpClient.GetAsync("/Library/MediaFolders?isHidden=false");
            response.EnsureSuccessStatusCode();

            var responseWrapper = await response.Content.ReadAsJsonAsync<PaginatedResponse<Library>>();
            List<Library> libraries = responseWrapper.Items.ToListOrDefault();
            return libraries.Cast<BaseLibrary>().ToList();
        }

        public async Task<BaseSystemInfo> GetSystemInfo()
        {
            var response = await httpClient.GetAsync("/System/Info");
            response.EnsureSuccessStatusCode();

            var systemInfo = await response.Content.ReadAsJsonAsync<SystemInfo>();
            return systemInfo;
        }

        public IList<BaseSessionItem> GetSessionData()
        {
            return SessionItems;
        }

        public async Task Dispose()
        {
            await Global.WebSocketClientService.DisconnectClientAsync(config.Id);
            await Global.WebSocketClientService.RemoveClientAsync(config.Id);
            SessionItems.Clear();
            excludedUsers.Clear();
            excludedLibraries.Clear();
            await activityMonitorTask.StopAsync();
        }

        public async Task<bool> AuthenticateByUsername(string username, string password)
        {
            try
            {
                var authRequest = new
                {
                    Username = username,
                    Pw = password
                };

                var authHeader = $"MediaBrowser Client=\"{UserAgent}\", Device=\"JellystatAPI\", DeviceId=\"{Guid.NewGuid()}\", Version=\"{Version}\"";

                HttpClient _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(config.URL),
                    DefaultRequestHeaders =
                {
                    { "Authorization", authHeader },
                    { "User-Agent", UserAgent }
                }
                };
                var content = new StringContent(JsonConvert.SerializeObject(authRequest), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/Users/AuthenticateByName", content);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex}");
                return false;
            }
        }

        public async Task<HttpResponseMessage> GetDeviceImage(string? icon, Uri? iconUrl = null)
        {
            if (iconUrl != null)
            {
                return await httpClient.GetAsync(iconUrl);
            }
            if (icon != null)
            {
                icon = icon.ToLowerInvariant();
            }
            return await httpClient.GetAsync($"/web/assets/img/devices/{icon}");
        }
        public async Task<HttpResponseMessage> GetBackdropImage(string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            return await httpClient.GetAsync($"/Items/{Id}/Images/Backdrop?fillWidth={Width}&quality={Quality}&blur={Blur}");
        }

        public async Task<HttpResponseMessage> GetItemImage(string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            return await httpClient.GetAsync($"/Items/{Id}/Images/Primary?fillWidth={Width}&quality={Quality}&blur={Blur}");
        }

        public async Task<HttpResponseMessage> GetUserImage(string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            return await httpClient.GetAsync($"/Users/{Id}/Images/Primary?fillWidth={Width}&quality={Quality}&blur={Blur}");
        }
    }
}
