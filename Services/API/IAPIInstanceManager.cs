using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Models.Jellystat;

namespace Jellystat_API_Core.Services.API
{
    public interface IAPIInstanceManager
    {
        Task RegisterInstanceAsync(Server config);
        iAPI? GetInstance(string serverId);
        Task<bool> RemoveInstanceAsync(string serverId);
        IEnumerable<iAPI> GetAllInstances();
        bool HasInstance(string serverId);
        Task ClearAsync();
    }
}
