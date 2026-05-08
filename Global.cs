using Jellystat_API_Core.Converters.Newtonsoft;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.WebSocket;
using Newtonsoft.Json;

namespace Jellystat_API_Core
{
    /// <summary>
    /// Global static accessor for commonly used services.
    /// Initialize in Program.cs after building the application.
    /// </summary>
    public static class Global
    {
        /// <summary>
        /// Global database repository instance.
        /// Access the database context via Global.Database.db
        /// </summary>
        //public static iDatabaseRepository Database { get; set; } = null!;
        public static IWebSocketClientService WebSocketClientService { get; set; }
        public static IAPIInstanceManager APIInstanceManager { get; set; }
        public static IWebSocketServerService WebSocketServerService { get; set; }
        //public static IServiceProvider ServiceProvider { get; set; } = null!;

        public static readonly JsonSerializerSettings jsonSettings = new()
        {
            ContractResolver = new JsonDefaultContractResolver()
        };

        public static double CompletionThreshold = 90;
        public static int NewWatchThresholdHours = 1;
        public static int MinimumDurationThreshold = 1;
        public static string JwtKey = "A_super_secret_key_that_is_32_chars";
        public static string JwtIssuer = "Jellystat";

        public static string UserAgent = "JellystatAPI/1.0.0";
    }
}
