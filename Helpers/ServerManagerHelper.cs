using Jellystat_API_Core.Classes.API;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;
using Jellystat_API_Core.Models.Jellystat;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Jellystat_API_Core.Helpers
{
    public class ServerManagerHelper
    {
        public ServerManagerHelper() { }
        public async Task<Server?> UpdateConfig(Server server, ApplicationDbContext context)
        {
            try
            {
                await Global.APIInstanceManager.RegisterInstanceAsync(server);
                if (await context.Servers.Where(s => s.Id == server.Id).FirstOrDefaultAsync() != null)
                {

                    await context.Servers.Where(s => s.Id == server.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.URL, server.URL)
                        .SetProperty(p => p.ExternalURL, server.ExternalURL)
                        .SetProperty(p => p.APIKey, server.APIKey)
                        .SetProperty(p => p.Name, server.Name)
                        .SetProperty(p => p.Type, server.Type)
                        .SetProperty(p => p.TaskSettings, server.TaskSettings)
                    );
                }
                return Global.APIInstanceManager.GetInstance(server.Id)?.config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating server config: {ex.Message}");
                return null;
            }
        }

        public async Task<BaseSystemInfo?> GetSystemInfo(ServerTypes Type, string URL, string APIKey)
        {
            try
            {

                BaseSystemInfo? info = null;
                iAPI api;
                switch (Type)
                {
                    case ServerTypes.Jellyfin:
                        api = new JellyfinAPI(URL, APIKey);
                        info = await api.GetSystemInfo();
                        break;
                    case ServerTypes.Emby:
                        api = new JellyfinAPI(URL, APIKey);
                        info = await api.GetSystemInfo();
                        break;

                }

                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error testing server connection: {ex.Message}");
                return null;
            }
        }
    }
}
