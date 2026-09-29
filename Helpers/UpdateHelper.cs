using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Jellystat.Helpers;
using System.Diagnostics;

namespace Jellystat_API_Core.Helpers
{
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
    }
}
