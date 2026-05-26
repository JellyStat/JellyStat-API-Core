using Jellystat_API_Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Helpers
{
    public class ConfigurationHelper
    {
        public async Task CheckIsConfiguredAsync(ApplicationDbContext context)
        {
            bool isConfigured = await context.LocalUsers.CountAsync() > 0 && await context.Servers.CountAsync() > 0;
            Global.IsConfigured = isConfigured;
        }
    }
}
