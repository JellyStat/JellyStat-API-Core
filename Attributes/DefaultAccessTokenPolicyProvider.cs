using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Jellystat_API_Core.Attributes
{
    public class DefaultAccessTokenPolicyProvider : DefaultAuthorizationPolicyProvider, IAuthorizationPolicyProvider
    {
        public DefaultAccessTokenPolicyProvider(IOptions<AuthorizationOptions> options)
            : base(options) { }

        // Cannot override GetDefaultPolicyAsync because it is not virtual.
        // Instead, hide the base method using 'new' keyword.
        public new Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        {
            // Return the AccessTokenOnly policy as the default
            return base.GetPolicyAsync("AccessTokenOnly");
        }
    }
}
