using Jellystat_API_Core.Enums;
using Newtonsoft.Json;
using System.Security.Claims;

namespace Jellystat_API_Core.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Gets the value of a claim by claim type.
        /// </summary>
        /// <param name="principal">The claims principal</param>
        /// <param name="claimType">The type of claim to retrieve</param>
        /// <returns>The claim value if found, otherwise null</returns>
        public static string? GetClaimValue(this ClaimsPrincipal principal, string claimType)
        {
            return principal?.FindFirst(claimType)?.Value;
        }

        /// <summary>
        /// Gets the serverId claim value from the current user.
        /// </summary>
        /// <param name="principal">The claims principal</param>
        /// <returns>The serverId if found, otherwise null</returns>
        public static string GetServerId(this ClaimsPrincipal principal)
        {
            return principal.GetClaimValue("serverId") ?? "NotFound";
        }

        /// <summary>
        /// Gets the username claim value from the current user.
        /// </summary>
        /// <param name="principal">The claims principal</param>
        /// <returns>The username if found, otherwise null</returns>
        public static string? GetUsername(this ClaimsPrincipal principal)
        {
            return principal.GetClaimValue("username");
        }

        /// <summary>
        /// Gets the permissions claim value from the current user as a list of PermissionTypes.
        /// </summary>
        /// <param name="principal">The claims principal</param>
        /// <returns>A list of PermissionTypes if found and valid, otherwise an empty list</returns>
        public static List<PermissionTypes> GetPermissions(this ClaimsPrincipal principal)
        {
            var permissionsJson = principal.GetClaimValue("permissions");
            if (string.IsNullOrEmpty(permissionsJson))
            {
                return new List<PermissionTypes>();
            }

            try
            {
                var permissionKeys = JsonConvert.DeserializeObject<List<string>>(permissionsJson, Global.jsonSettings);
                if (permissionKeys == null)
                {
                    return new List<PermissionTypes>();
                }

                var permissions = new List<PermissionTypes>();
                foreach (var key in permissionKeys)
                {
                    if (Enum.TryParse<PermissionTypes>(key, out var permission))
                    {
                        permissions.Add(permission);
                    }
                }
                return permissions;
            }
            catch
            {
                return new List<PermissionTypes>();
            }
        }

        /// <summary>Claim holding the media server user id of a remote (Jellyfin/Emby) login.</summary>
        public const string UserIdClaim = "userId";

        // Matches no user: used when a non-admin token has no userId (e.g. issued before this claim existed),
        // so it sees nothing until the next refresh adds the claim.
        private const string NoUserScope = "__no_user__";

        /// <summary>
        /// The media server user id this caller is limited to, or null when they may see every user on their server.
        /// Admins and read-only API keys (created by an admin for a whole server, e.g. dashboards) are not limited;
        /// remote logins only ever see their own data.
        /// </summary>
        public static string? GetScopedUserId(this ClaimsPrincipal principal)
        {
            if (principal.IsAdministrator()) return null;
            if (principal.HasClaim(c => c.Type == Jellystat_API_Core.Attributes.ApiKeyDefaults.ApiKeyIdClaim)) return null;

            string? userId = principal.GetClaimValue(UserIdClaim);
            return string.IsNullOrEmpty(userId) ? NoUserScope : userId;
        }

        /// <summary>
        /// True for access tokens and API keys (which carry type=access). Refresh tokens have type=refresh and
        /// must only ever be accepted by /Auth/Refresh.
        /// </summary>
        public static bool IsAccessToken(this ClaimsPrincipal principal)
        {
            return principal.HasClaim(c => c.Type == "type" && c.Value == "access");
        }

        /// <summary>
        /// Checks if the current user has administrator permissions.
        /// </summary>
        /// <param name="principal">The claims principal</param>
        /// <returns>True if the user has Administrator permissions, otherwise false</returns>
        public static bool IsAdministrator(this ClaimsPrincipal principal)
        {
            var permissions = principal.GetPermissions();
            return permissions.Contains(PermissionTypes.Administrator);
        }
    }
}
