using System.Security.Claims;
using ActivityEntity = Jellystat_API_Core.Data.Schema.Activity;
using UsersEntity = Jellystat_API_Core.Data.Schema.Users;

namespace Jellystat_API_Core.Extensions
{
    /// <summary>
    /// Limits queries to what the caller may see: everything for admins and server-wide API keys,
    /// only their own rows for remote (Jellyfin/Emby) logins. See ClaimsPrincipalExtensions.GetScopedUserId.
    /// </summary>
    public static class ActivityScopeExtensions
    {
        public static IQueryable<ActivityEntity> VisibleTo(this IQueryable<ActivityEntity> source, ClaimsPrincipal user)
        {
            string? userId = user.GetScopedUserId();
            return userId == null ? source : source.Where(a => a.UserId == userId);
        }

        public static IQueryable<UsersEntity> VisibleTo(this IQueryable<UsersEntity> source, ClaimsPrincipal user)
        {
            string? userId = user.GetScopedUserId();
            return userId == null ? source : source.Where(u => u.Id == userId);
        }
    }
}
