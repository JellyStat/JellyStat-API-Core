using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi.Extensions;

namespace Jellystat_API_Core.Attributes
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IMemoryCache _cache;

        public PermissionAuthorizationHandler(ApplicationDbContext dbContext, IMemoryCache cache)
        {
            _dbContext = dbContext;
            _cache = cache;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var token = context.User.FindFirst("jti")?.Value;
            if (string.IsNullOrEmpty(token))
                return;

            var cacheKey = $"permissions_{token}";
            List<string> permissionKeys;

            var username = context.User.FindFirst("username")?.Value;
            string? serverId = context.User.FindFirst("serverId")?.Value;

            // Try to get permissions from cache
            if (!_cache.TryGetValue(cacheKey, out permissionKeys))
            {

                if (string.IsNullOrEmpty(username))
                    return;


                // Fetch permissions from DB
                permissionKeys = await (
                      from up in _dbContext.UserPermissions
                      join p in _dbContext.Permissions on up.PermissionId equals p.Id
                      join u in _dbContext.LocalUsers on up.UserId equals u.Id
                      where u.Username == username && p.Active
                      select p.PermissionKey
                  ).ToListAsync();


                if (permissionKeys.IsEmpty() && serverId != null && Global.APIInstanceManager.HasInstance(serverId))
                {
                    permissionKeys = [PermissionTypes.CanView.GetDisplayName()];
                }

                // Cache permissions for 5 minutes sliding expiration
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));
                _cache.Set(cacheKey, permissionKeys, cacheEntryOptions);
            }



            // Check if required permission exists
            if (permissionKeys.Contains(requirement.Permission) || permissionKeys.Contains(PermissionTypes.Administrator.ToString()))
                context.Succeed(requirement);
        }
    }
}