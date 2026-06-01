using Jellystat_API_Core.Classes.Tasks;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Models.WebSocket;

namespace Jellystat_API_Core.Classes.API.@interface
{
    public interface iAPI
    {
        public Server config { get; set; }
        public string Version { get; set; }
        public string UserAgent { get; set; }
        public HttpClient httpClient { get; set; }

        public List<BaseSessionItem> SessionItems { get; set; }

        public Action<WebSocketMessage>? OnMessageReceived { get; set; }
        public Action<string>? OnConnected { get; set; }
        public Action<string>? OnDisconnected { get; set; }
        public Action<string, Exception>? OnError { get; set; }

        public ActivityMonitor activityMonitorTask { get; set; }

        public SyncTask syncTask { get; set; }

        public List<Data.Schema.ExcludedUsers> excludedUsers { get; set; }
        public List<Data.Schema.ExcludedLibraries> excludedLibraries { get; set; }

        //Functions

        public Task<IList<BaseUser>> GetUsers();
        public Task<IList<BaseUser>> GetAdmins();
        public Task<BaseUser?> GetUserById(string id);
        public Task<IList<BaseItem>> GetItemsByIds(List<string> itemIds, int startIndex = 0, int increment = 200, int limit = 200, bool recursive = true);
        public Task<IList<BaseItem>> GetItemsFromParentId(string id, List<string>? itemIds = null, int startIndex = 0, int increment = 200, int limit = 200, bool recursive = true);
        public Task<IList<BaseLibrary>> GetLibraries();

        public Task<BaseSystemInfo> GetSystemInfo();

        public IList<BaseSessionItem> GetSessionData();
        //
        public Task Dispose();

        public Task<bool> AuthenticateByUsername(string username, string password);

        public Task<HttpResponseMessage> GetDeviceImage(string icon, Uri? iconUrl = null);

    }
}
