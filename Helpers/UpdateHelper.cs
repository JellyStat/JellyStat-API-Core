using Newtonsoft.Json;
using Octokit;
using System.Diagnostics;

namespace Jellystat_API_Core.Helpers
{
    public static class UpdateHelper
    {

        public static async Task<string> GetLatestUITag()
        {
            try
            {
                Debug.WriteLine($"[Latest UI Tag] Fetching latest release from GitHub...");
                var client = new GitHubClient(new ProductHeaderValue("Jellystat-API-Core"));
                var repo = await client.Repository.Get("Jellystat", "Jellystat-API-UI");

                var latestRelease = await client.Repository.Release.GetLatest(repo.Id);
                string shortTag = latestRelease.TagName;

                Debug.WriteLine($"[Latest UI Tag]  {JsonConvert.SerializeObject(latestRelease)}");
                return shortTag;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Latest UI Tag] Error: {ex.Message}");
                return "";
            }
        }
    }
}
