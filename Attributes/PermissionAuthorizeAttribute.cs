using Jellystat_API_Core.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Jellystat_API_Core.Attributes
{
    public class PermissionAuthorizeAttribute : AuthorizeAttribute
    {
        public PermissionAuthorizeAttribute(PermissionTypes permission)
        {
            Policy = $"Permission:{permission}";
        }
    }
}