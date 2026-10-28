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
        /// <summary>Signing key for JWTs. Set at startup from JWT_KEY, or a random per-run key when that is unset.</summary>
        public static string JwtKey = string.Empty;
        public static string JwtIssuer = "Jellystat";

        public static string UserAgent = "JellystatAPI/1.0.0";

        public static bool IsConfigured = false;
        public static bool AllowUnauthenticatedPasswordReset = false;

        /// <summary>MaxMind GeoLite web service credentials (JS_GEOLITE_ACCOUNT_ID / JS_GEOLITE_LICENSE_KEY). IP lookup is disabled when either is missing.</summary>
        public static string? GeoLiteAccountId { get; set; }
        public static string? GeoLiteLicenseKey { get; set; }

        public static readonly string BackupDirectory = Path.Combine(AppContext.BaseDirectory, "backups");
        public static string WebUIDirectory { get; set; } = string.Empty;
        public static readonly HashSet<string> BackupExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bac", ".json", ".jsonl" };

    }
}
