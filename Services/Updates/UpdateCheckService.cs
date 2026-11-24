using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat.Helpers;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Jellystat_API_Core.Services.Updates
{
    public class ApiReleaseInfo
    {
        public string? Version { get; set; }
    }

    /// <summary>
    /// Checks GitHub for the latest UI and API releases on a timer and keeps the last good result in memory,
    /// so /Api/Version never calls GitHub itself. Failed checks back off (honouring Retry-After) instead of retrying on every request.
    /// </summary>
    public class UpdateCheckService : BackgroundService
    {
        private const string UIReleaseUrl = "https://raw.githubusercontent.com/JellyStat/Jellystat-UI/refs/heads/main/release.json";
        private const string ApiReleaseUrl = "https://raw.githubusercontent.com/JellyStat/JellyStat-API-Core/refs/heads/main/release.json";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan MaxJitter = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);

        private readonly HttpClient _httpClient;
        private readonly ILogger<UpdateCheckService> _logger;
        private int _consecutiveFailures;

        private record FetchResult<T>(T? Value, string? Error = null, TimeSpan? RetryAfter = null) where T : class;

        public UpdateCheckService(ILogger<UpdateCheckService> logger)
        {
            _logger = logger;

            // One long-lived client for the service's lifetime; pooled connections are recycled so DNS changes are picked up
            _httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellystat-API-Core/{CurrentApiVersion}");
        }

        /// <summary>The version of the running API, from AssemblyVersion in the csproj.</summary>
        public static Version CurrentApiVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0, 0);

        /// <summary>Last successfully fetched UI release, or null if no check has succeeded yet.</summary>
        public UIReleaseInfo? LatestUi { get; private set; }

        /// <summary>Last successfully fetched API version (from release.json, written by CI after the image is pushed), or null if no check has succeeded yet.</summary>
        public Version? LatestApi { get; private set; }

        /// <summary>Fetches the UI release.json now, bypassing the timer, and refreshes the stored result on success.</summary>
        public async Task<UIReleaseInfo?> FetchLatestUiAsync(CancellationToken cancellationToken = default)
        {
            var result = await FetchJsonAsync<UIReleaseInfo>(UIReleaseUrl, cancellationToken);
            if (result.Value != null) LatestUi = result.Value;
            return result.Value;
        }

        /// <summary>True only when the published version is higher than the running one, so local builds with a bumped version are never flagged.</summary>
        public static bool IsApiUpdateAvailable(Version? latestVersion)
        {
            return latestVersion != null && latestVersion > CurrentApiVersion;
        }

        public static string GetCurrentUITag()
        {
            string UITag = string.Empty;

            ////find io file tag
            List<string> filePaths = Directory.Exists(Global.WebUIDirectory)
                ? Directory
                    .EnumerateFiles(Global.WebUIDirectory, "*.emtag", SearchOption.TopDirectoryOnly)
                    .ToList()
                : new List<string>();

            if (filePaths.Count > 0)
            {
                UITag = Path.GetFileNameWithoutExtension(filePaths.First());
            }

            return UITag;
        }

        public static bool IsUIUpdateAvailable(string? latestTag)
        {
            if (latestTag.IsNullOrEmpty())
            {
                return false;
            }

            string currentUiTag = GetCurrentUITag();

            if (string.IsNullOrEmpty(currentUiTag))
            {
                return true;
            }

            return !string.Equals(currentUiTag, latestTag, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public static void ExtractNewUI(string zipPath, string targetDirectory)
        {
            // Ensure the target directory points to /app/wwwroot
            if (Directory.Exists(targetDirectory))
            {
                var di = new DirectoryInfo(targetDirectory);

                // 1. Force clear file attributes and delete files inside wwwroot
                foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                    file.Delete(); // Deletes individual files safely
                }

                // 2. Delete all subdirectories inside wwwroot
                foreach (var dir in di.GetDirectories())
                {
                    dir.Delete(true); // Deletes subfolders recursively
                }
            }
            else
            {
                // Fallback: If it somehow doesn't exist, create it
                Directory.CreateDirectory(targetDirectory);
            }

            // 3. Extract the zip file contents directly into the empty wwwroot folder
            ZipFile.ExtractToDirectory(zipPath, targetDirectory, true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    TimeSpan delay = await CheckAsync(stoppingToken);
                    await Task.Delay(delay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        public override void Dispose()
        {
            _httpClient.Dispose();
            base.Dispose();
        }

        /// <summary>Runs one check and returns how long to wait before the next.</summary>
        private async Task<TimeSpan> CheckAsync(CancellationToken stoppingToken)
        {
            var uiTask = FetchJsonAsync<UIReleaseInfo>(UIReleaseUrl, stoppingToken);
            var apiTask = FetchJsonAsync<ApiReleaseInfo>(ApiReleaseUrl, stoppingToken);
            await Task.WhenAll(uiTask, apiTask);

            var ui = uiTask.Result;
            var api = apiTask.Result;

            Version? apiVersion = null;
            string? apiError = api.Error;
            if (api.Value != null && !Version.TryParse(api.Value.Version, out apiVersion))
            {
                apiError = $"Invalid version '{api.Value.Version}' in {ApiReleaseUrl}";
            }

            // Keep the last good value for whichever fetch failed
            if (ui.Value != null) LatestUi = ui.Value;
            if (apiVersion != null) LatestApi = apiVersion;

            if (ui.Value != null && apiVersion != null)
            {
                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation("Update check recovered after {Failures} failed attempt(s)", _consecutiveFailures);
                }
                _consecutiveFailures = 0;
                return CheckInterval + TimeSpan.FromSeconds(Random.Shared.NextDouble() * MaxJitter.TotalSeconds);
            }

            _consecutiveFailures++;

            // Exponential backoff: 30m, 1h, 2h, ... capped at 24h. A longer Retry-After from GitHub wins.
            double factor = Math.Pow(2, Math.Min(_consecutiveFailures - 1, 10));
            TimeSpan backoff = TimeSpan.FromTicks(Math.Min(InitialBackoff.Ticks * (long)factor, MaxBackoff.Ticks));
            TimeSpan? retryAfter = new[] { ui.RetryAfter, api.RetryAfter }.Max();
            if (retryAfter > backoff)
            {
                backoff = retryAfter.Value < MaxBackoff ? retryAfter.Value : MaxBackoff;
            }

            _logger.LogWarning("Update check failed ({Errors}); retrying in {Delay}",
                string.Join("; ", new[] { ui.Error, apiError }.Where(e => e != null)), backoff);

            return backoff;
        }

        private async Task<FetchResult<T>> FetchJsonAsync<T>(string url, CancellationToken cancellationToken) where T : class
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null);
                    return new FetchResult<T>(null, $"HTTP {(int)response.StatusCode} from {url}", retryAfter);
                }

                return new FetchResult<T>(await response.Content.ReadAsJsonAsync<T>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new FetchResult<T>(null, $"{ex.GetType().Name}: {ex.Message} ({url})");
            }
        }
    }
}
