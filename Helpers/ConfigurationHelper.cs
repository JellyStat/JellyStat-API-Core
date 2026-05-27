using Jellystat_API_Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Helpers
{
    public static class ConfigurationHelper
    {
        public static async Task CheckIsConfiguredAsync(ApplicationDbContext context)
        {
            bool isConfigured = await context.LocalUsers.AnyAsync() && await context.Servers.AnyAsync();
            Global.IsConfigured = isConfigured;
        }
    }
}
