using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi;
using System.Text.Json;

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

            var permissionsClaimJson = context.User.FindFirst("permissions")?.Value;
            if (string.IsNullOrWhiteSpace(permissionsClaimJson))
                return;

            List<string>? claimPermissions;
            try
            {
                claimPermissions = JsonSerializer.Deserialize<List<string>>(permissionsClaimJson);
            }
            catch
            {
                return;
            }

            if (claimPermissions == null)
                return;

            var normalizedClaimPermissions = claimPermissions
                .Select(NormalizePermissionKey)
                .Where(p => p != null)
                .ToHashSet(StringComparer.Ordinal);

            if (normalizedClaimPermissions.Count == 0)
                return;

            var administratorPermission = PermissionTypes.Administrator.ToString();
            var canViewPermission = PermissionTypes.CanView.ToString();

            if (normalizedClaimPermissions.SetEquals([canViewPermission]))
            {
                if (requirement.Permission == canViewPermission)
                {
                    context.Succeed(requirement);
                }

                return;
            }

            if (!normalizedClaimPermissions.Contains(administratorPermission))
                return;

            var username = context.User.FindFirst("username")?.Value;
            if (string.IsNullOrEmpty(username))
                return;

            var cacheKey = $"permissions_{token}";
            List<string> permissionKeys;

            if (!_cache.TryGetValue(cacheKey, out permissionKeys))
            {
                permissionKeys = await (
                      from up in _dbContext.UserPermissions
                      join p in _dbContext.Permissions on up.PermissionId equals p.Id
                      join u in _dbContext.LocalUsers on up.UserId equals u.Id
                      where u.Username == username && p.Active
                      select p.PermissionKey
                  ).ToListAsync();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));
                _cache.Set(cacheKey, permissionKeys, cacheEntryOptions);
            }

            var normalizedDbPermissions = permissionKeys
                .Select(NormalizePermissionKey)
                .Where(p => p != null)
                .ToHashSet(StringComparer.Ordinal);

            if (normalizedDbPermissions.Contains(requirement.Permission) || normalizedDbPermissions.Contains(administratorPermission))
                context.Succeed(requirement);
        }

        private static string? NormalizePermissionKey(string? permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                return null;

            if (Enum.TryParse<PermissionTypes>(permission, true, out var parsedPermission))
                return parsedPermission.ToString();

            foreach (var permissionType in Enum.GetValues<PermissionTypes>())
            {
                if (string.Equals(permissionType.GetDisplayName(), permission, StringComparison.OrdinalIgnoreCase))
                    return permissionType.ToString();
            }

            return null;
        }
    }
}
