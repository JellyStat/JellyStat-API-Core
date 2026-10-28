using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat.Helpers;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Jellystat_API_Core.Helpers
{
    public class ApiReleaseInfo
    {
        public string? Version { get; set; }
    }

    public static class UpdateHelper
    {

        public static async Task<UIReleaseInfo?> GetLatestUITag()
        {
            try
            {
                HttpClient _httpClient = new HttpClient();

                string url = "https://raw.githubusercontent.com/JellyStat/Jellystat-UI/refs/heads/main/release.json";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var latestRelease = await response.Content.ReadAsJsonAsync<UIReleaseInfo>();

                return latestRelease;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Latest UI Tag] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>The version of the running API, from AssemblyVersion in the csproj.</summary>
        public static Version CurrentApiVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0, 0);

        /// <summary>Latest published API version, from release.json on the API repo's main branch (written by CI after the image is pushed).</summary>
        public static async Task<Version?> GetLatestApiVersion()
        {
            try
            {
                using HttpClient httpClient = new HttpClient();

                string url = "https://raw.githubusercontent.com/JellyStat/JellyStat-API-Core/refs/heads/main/release.json";

                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var latestRelease = await response.Content.ReadAsJsonAsync<ApiReleaseInfo>();

                return Version.TryParse(latestRelease?.Version, out var version) ? version : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Latest API Version] Error: {ex.Message}");
                return null;
            }
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

        public static async Task<bool> IsUIUpdateAvailable(string? latestTag)
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


            if (!string.Equals(currentUiTag, latestTag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public static string GetFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = System.IO.File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
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
    }
}
