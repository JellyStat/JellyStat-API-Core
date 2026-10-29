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

        /// <summary>Most card-grid pictures fetched for one element; a grid of hundreds of posters stops here.</summary>
        public const int MaxCardImages = 300;

        public async Task ResolveAsync(ReportRenderModel model, ReportScope scope, CancellationToken cancellationToken = default)
        {
            var api = _apiInstanceManager.GetInstance(scope.ServerId);
            if (api == null) return;
            var jobs = new List<(string Key, MediaRequest Request, int Width, string Name)>();
            foreach (var e in model.Definition.Pages.SelectMany(p => p.Elements))
            {
                if (e.Hidden) continue;
                // Twice the drawn width keeps PNG exports at scale 2 sharp
                if (e.Type == ReportElementTypes.Image)
                {
                    if (e.Media == null && (string.IsNullOrEmpty(e.Src) || e.Src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))) continue;
                    var request = e.Media != null ? FromBinding(e.Media, model) : FromUrl(e.Src!, api);
                    if (request != null) jobs.Add((e.Id, request, Math.Clamp((int)Math.Ceiling(e.Width * 2), 100, 2000), e.Name ?? e.Id));
                }
                else if (e.Type == ReportElementTypes.Cards && e.Cards is { ImagePosition: not "none" } o && !string.IsNullOrWhiteSpace(o.ImageField))
                {
                    var data = ReportTokenResolver.FindData(model, e.DataSourceId);
                    var column = data?.Columns.FirstOrDefault(c => c.Name.Equals(o.ImageField, StringComparison.OrdinalIgnoreCase));
                    if (data == null || column == null) continue;
                    var grid = CardGrid.Of(o, e.Width - 2 * e.Style.Padding);
                    double drawn = o.ImagePosition == "left" ? grid.CardWidth * o.ImageSize : grid.CardWidth;
                    int width = Math.Clamp((int)Math.Ceiling(drawn * 2), 100, 2000);
                    bool user = o.ImageKind.Equals("user", StringComparison.OrdinalIgnoreCase);
                    bool backdrop = !user && o.ImageType.Equals("Backdrop", StringComparison.OrdinalIgnoreCase);
                    int total = Math.Min(ReportPaginator.TotalRows(e, model), MaxCardImages);
                    for (int i = 0; i < total; i++)
                    {
                        string? id = data.Rows[i].GetValueOrDefault(column.Name)?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(id)) jobs.Add(($"{e.Id}#{i}", new MediaRequest(user, backdrop, id), width, e.Name ?? e.Id));
                    }
                }
            }

            // A few at a time: card grids can need many pictures, but the media server shouldn't be flooded
            using var gate = new SemaphoreSlim(6);
            var results = await Task.WhenAll(jobs.Select(async job =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return (job.Key, Uri: await FetchAsync(api, job.Request, job.Width, job.Name, cancellationToken));
                }
                finally
                {
                    gate.Release();
                }
            }));
            foreach (var (key, uri) in results)
                if (uri != null) model.Images[key] = uri;
        }

        private async Task<string?> FetchAsync(Classes.API.@interface.iAPI api, MediaRequest request, int width, string name, CancellationToken cancellationToken)
        {
            try
            {
                using var response = request.User ? await api.GetUserImage(request.Id, width)
                    : request.Backdrop ? await api.GetBackdropImage(request.Id, width)
                    : await api.GetItemImage(request.Id, width);
                string? type = response.Content.Headers.ContentType?.MediaType;
                if (!response.IsSuccessStatusCode || type == null || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                if (response.Content.Headers.ContentLength > MaxImageBytes) return null;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length > MaxImageBytes) return null;
                return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Report image {Element} could not be fetched", name);
                return null;
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
