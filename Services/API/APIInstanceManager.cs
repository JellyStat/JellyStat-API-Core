using Jellystat_API_Core.Classes.API;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Jellystat_API_Core.Services.API
{
    public class APIInstanceManager : IAPIInstanceManager
    {
        private readonly ConcurrentDictionary<string, iAPI> _instances;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public APIInstanceManager(IServiceScopeFactory serviceScopeFactory)
        {
            _instances = new ConcurrentDictionary<string, iAPI>();
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task RegisterInstanceAsync(Server config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(Server));

            iAPI instance;

            iAPI? existingInstance = GetInstance(config.Id);
            if (existingInstance != null)
            {
                await existingInstance.Dispose();
            }

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                List<Data.Schema.ExcludedUsers> excludedUsers = await context.ExcludedUsers.Where(x => x.ServerId == config.Id).ToListAsync();
                List<Data.Schema.ExcludedLibraries> excludedLibraries = await context.ExcludedLibraries.Where(x => x.ServerId == config.Id).ToListAsync();


                switch (config.Type)
                {
                    case ServerTypes.Jellyfin:
                        instance = new JellyfinAPI(config, excludedUsers, excludedLibraries, _serviceScopeFactory);
                        break;
                    case ServerTypes.Emby:
                        instance = new JellyfinAPI(config, excludedUsers, excludedLibraries, _serviceScopeFactory);
                        break;
                    default:
                        throw new NotSupportedException($"Server type {config.Type} is not supported.");
                }
            }

            _instances.AddOrUpdate(instance.config.Id, instance, (key, oldValue) => instance);
        }


        public iAPI? GetInstance(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
                return null;

            _instances.TryGetValue(serverId, out var instance);
            return instance;
        }

        public async Task<bool> RemoveInstanceAsync(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
                return false;

            iAPI? existingInstance = GetInstance(serverId);
            if (existingInstance != null)
            {
                await existingInstance.Dispose();
            }


            return _instances.TryRemove(serverId, out _);
        }

        public IEnumerable<iAPI> GetAllInstances()
        {
            return _instances.Values;
        }

        public bool HasInstance(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
                return false;

            return _instances.ContainsKey(serverId);
        }

        public async Task ClearAsync()
        {
            foreach (var instance in _instances.Values)
            {
                await instance.Dispose();
            }

            _instances.Clear();
        }
    }
}
