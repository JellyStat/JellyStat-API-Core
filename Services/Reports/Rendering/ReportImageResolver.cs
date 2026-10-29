using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.API;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    public interface IReportImageResolver
    {
        /// <summary>
        /// Fetches the images a server-side render needs into model.Images (as data: URIs), keyed by element id.
        /// Only the report's own media server is ever contacted; other URLs are left alone and render as placeholders.
        /// </summary>
        Task ResolveAsync(ReportRenderModel model, ReportScope scope, CancellationToken cancellationToken = default);
    }

    public partial class ReportImageResolver : IReportImageResolver
    {
        private const int MaxImageBytes = 10 * 1024 * 1024;

        // Jellyfin /Items/{id}/Images/Primary, Emby /emby/Items/..., and the same for users
        [GeneratedRegex(@"/(?:emby/)?(Items|Users)/([^/]+)/Images/(Primary|Backdrop)", RegexOptions.IgnoreCase)]
        private static partial Regex MediaPath();

        // Jellystat's own proxy: /Proxy/Images/Items/Primary?Id=..., /Proxy/Images/User/Primary?Id=...
        [GeneratedRegex(@"/Proxy/Images/(Items|User)/(Primary|Backdrop)$", RegexOptions.IgnoreCase)]
        private static partial Regex ProxyPath();

        private readonly IAPIInstanceManager _apiInstanceManager;
        private readonly ILogger<ReportImageResolver> _logger;

        public ReportImageResolver(IAPIInstanceManager apiInstanceManager, ILogger<ReportImageResolver> logger)
        {
            _apiInstanceManager = apiInstanceManager;
            _logger = logger;
        }

        private record MediaRequest(bool User, bool Backdrop, string Id);

        public async Task ResolveAsync(ReportRenderModel model, ReportScope scope, CancellationToken cancellationToken = default)
        {
            var api = _apiInstanceManager.GetInstance(scope.ServerId);
            foreach (var e in model.Definition.Pages.SelectMany(p => p.Elements))
            {
                if (e.Type != ReportElementTypes.Image || e.Hidden) continue;
                if (e.Media == null && (string.IsNullOrEmpty(e.Src) || e.Src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))) continue;

                var request = e.Media != null ? FromBinding(e.Media, model) : FromUrl(e.Src!, api);
                if (request == null || api == null) continue;

                try
                {
                    // Twice the element width keeps PNG exports at scale 2 sharp
                    int width = Math.Clamp((int)Math.Ceiling(e.Width * 2), 100, 2000);
                    using var response = request.User ? await api.GetUserImage(request.Id, width)
                        : request.Backdrop ? await api.GetBackdropImage(request.Id, width)
                        : await api.GetItemImage(request.Id, width);
                    string? type = response.Content.Headers.ContentType?.MediaType;
                    if (!response.IsSuccessStatusCode || type == null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (response.Content.Headers.ContentLength > MaxImageBytes) continue;
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length > MaxImageBytes) continue;
                    model.Images[e.Id] = $"data:{type};base64,{Convert.ToBase64String(bytes)}";
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Report image {Element} could not be fetched", e.Name ?? e.Id);
                }
            }
        }

        /// <summary>The media id comes from a parameter, the first row of a data source, or is fixed.</summary>
        public static string? BindingId(ReportImageBinding binding, ReportRenderModel model)
        {
            object? value = null;
            if (!string.IsNullOrWhiteSpace(binding.Parameter))
            {
                var key = model.Parameters.Keys.FirstOrDefault(k => k.Equals(binding.Parameter, StringComparison.OrdinalIgnoreCase));
                value = key != null ? model.Parameters[key] : null;
                // Multi-select lookups render as "a, b": use the first
                if (value is string s && s.Contains(", ")) value = s.Split(", ")[0];
            }
            else if (!string.IsNullOrWhiteSpace(binding.DataSourceId))
            {
                var data = ReportTokenResolver.FindData(model, binding.DataSourceId);
                var column = data?.Columns.FirstOrDefault(c => c.Name.Equals(binding.Field ?? "", StringComparison.OrdinalIgnoreCase));
                value = data != null && column != null && data.Rows.Count > 0 ? data.Rows[0].GetValueOrDefault(column.Name) : null;
            }
            else
            {
                value = binding.Id;
            }
            string? id = value?.ToString()?.Trim();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        private static MediaRequest? FromBinding(ReportImageBinding binding, ReportRenderModel model)
        {
            string? id = BindingId(binding, model);
            if (id == null) return null;
            bool user = binding.Type.Equals("user", StringComparison.OrdinalIgnoreCase);
            return new MediaRequest(user, !user && binding.ImageType.Equals("Backdrop", StringComparison.OrdinalIgnoreCase), id);
        }

        private static MediaRequest? FromUrl(string src, Classes.API.@interface.iAPI? api)
        {
            if (!Uri.TryCreate(src, UriKind.RelativeOrAbsolute, out var uri)) return null;
            if (!uri.IsAbsoluteUri) uri = new Uri(new Uri("http://localhost"), uri);

            var proxy = ProxyPath().Match(uri.AbsolutePath);
            if (proxy.Success)
            {
                // Any host: this is Jellystat's own endpoint, and it's always answered from the report's server
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string? id = query["Id"] ?? query["id"];
                if (string.IsNullOrWhiteSpace(id)) return null;
                bool user = proxy.Groups[1].Value.Equals("User", StringComparison.OrdinalIgnoreCase);
                return new MediaRequest(user, proxy.Groups[2].Value.Equals("Backdrop", StringComparison.OrdinalIgnoreCase), id);
            }

            // A direct media server URL is only followed when it points at the report's own server
            if (api == null || !IsServerUrl(uri, api.config.URL) && !IsServerUrl(uri, api.config.ExternalURL)) return null;
            var media = MediaPath().Match(uri.AbsolutePath);
            if (!media.Success) return null;
            bool isUser = media.Groups[1].Value.Equals("Users", StringComparison.OrdinalIgnoreCase);
            return new MediaRequest(isUser, media.Groups[3].Value.Equals("Backdrop", StringComparison.OrdinalIgnoreCase), Uri.UnescapeDataString(media.Groups[2].Value));
        }

        private static bool IsServerUrl(Uri uri, string? serverUrl) =>
            !string.IsNullOrWhiteSpace(serverUrl)
            && Uri.TryCreate(serverUrl, UriKind.Absolute, out var server)
            && uri.Scheme.Equals(server.Scheme, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals(server.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == server.Port;
    }
}
