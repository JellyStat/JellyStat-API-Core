using Jellystat_API_Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Extensions
{
    public static class DatabaseExtensions
    {
        /// <summary>
        /// Applies pending migrations and creates the database if it doesn't exist.
        /// Use with caution in production environments.
        /// </summary>
        public static async Task MigrateDatabaseAsync(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
                logger.LogError(ex, "An error occurred while migrating the database.");
                throw;
            }
        }

        /// <summary>
        /// Checks if the database can be connected to.
        /// </summary>
        public static async Task<bool> CanConnectAsync(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                return await context.Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }
    }
}
