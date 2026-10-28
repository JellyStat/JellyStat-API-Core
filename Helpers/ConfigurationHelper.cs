using Jellystat_API_Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Helpers
{
    public static class ConfigurationHelper
    {
        public static async Task CheckIsConfiguredAsync(ApplicationDbContext context)
        {
            bool hasLocalUsers = await context.LocalUsers.AnyAsync();
            Global.HasLocalUsers = hasLocalUsers;
            Global.IsConfigured = hasLocalUsers && await context.Servers.AnyAsync();
        }
    }
}
