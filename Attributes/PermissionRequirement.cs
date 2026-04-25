using Microsoft.AspNetCore.Authorization;

namespace Jellystat_API_Core.Attributes
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }
        public PermissionRequirement(string permission) => Permission = permission;
    }
}