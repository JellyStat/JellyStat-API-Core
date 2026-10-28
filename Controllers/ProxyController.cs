using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Controllers
{
    //[Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ProxyController : Controller
    {
        // Jellyfin ids are 32 hex chars, Emby uses numeric item ids: neither needs '/', '.', '?' or '%'
        private static readonly Regex IdPattern = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);
        // Device icon file name, optionally with one extension: no path separators or ".."
        private static readonly Regex IconPattern = new(@"^[A-Za-z0-9_-]{1,64}(\.[A-Za-z0-9]{1,8})?$", RegexOptions.Compiled);
        // Server-relative icon path, e.g. /web/assets/img/devices/android.svg: must start with a single '/',
        // no "..", and no '?', '%', ':' or '\' that could change where the request ends up
        private static readonly Regex RelativeIconPathPattern = new(@"^/(?!/)(?!.*\.\.)[A-Za-z0-9_./-]{1,256}$", RegexOptions.Compiled);

        private const int MaxWidth = 4000;

        private readonly IAPIInstanceManager _apiInstanceManager;
        public ProxyController(IAPIInstanceManager apiInstanceManager)
        {

            _apiInstanceManager = apiInstanceManager;
        }

        [HttpGet("Images/Devices", Name = "Images/Devices")]
        public async Task<ActionResult> ProxyDeviceImageRequest(string ServerId, string? icon, string? iconUrl)
        {
            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            Uri? iconUri = null;
            if (!string.IsNullOrWhiteSpace(iconUrl))
            {
                if (RelativeIconPathPattern.IsMatch(iconUrl))
                {
                    // Fetched from the media server with its credentials. ToImageResult only passes images back,
                    // so a path like /Auth/Keys can't be used to read API data (it returns JSON and gets a 415).
                    iconUri = new Uri(iconUrl, UriKind.Relative);
                }
                // Anything else must be an absolute http(s) URL, fetched without server credentials.
                // (Checked after the relative pattern: on Linux "/path" would otherwise parse as an absolute file:// URI.)
                else if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out iconUri)
                    || (iconUri.Scheme != Uri.UriSchemeHttps && iconUri.Scheme != Uri.UriSchemeHttp))
                {
                    return BadRequest("iconUrl must be a server-relative path or an absolute http(s) URL");
                }
            }
            else if (icon == null || !IconPattern.IsMatch(icon))
            {
                return BadRequest("Invalid icon");
            }

            var response = await api.GetDeviceImage(icon, iconUri);
            return await ToImageResult(response, "image/svg+xml");
        }

        [HttpGet("Images/Items/Backdrop", Name = "Images/Items/Backdrop")]
        public async Task<ActionResult> ProxyItemBackdropImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            if (!IdPattern.IsMatch(Id ?? string.Empty))
            {
                return BadRequest("Invalid Id");
            }

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetBackdropImage(Id!, ClampWidth(Width), ClampQuality(Quality), ClampBlur(Blur));
            return await ToImageResult(response);
        }

        [HttpGet("Images/Items/Primary", Name = "Images/Items/Primary")]
        public async Task<ActionResult> ProxyItemPrimaryImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            if (!IdPattern.IsMatch(Id ?? string.Empty))
            {
                return BadRequest("Invalid Id");
            }

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetItemImage(Id!, ClampWidth(Width), ClampQuality(Quality), ClampBlur(Blur));
            return await ToImageResult(response);
        }

        [HttpGet("Images/User/Primary", Name = "Images/User/Primary")]
        public async Task<ActionResult> ProxyUserPrimaryImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            if (!IdPattern.IsMatch(Id ?? string.Empty))
            {
                return BadRequest("Invalid Id");
            }

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetUserImage(Id!, ClampWidth(Width), ClampQuality(Quality), ClampBlur(Blur));
            return await ToImageResult(response);
        }

        private static int ClampWidth(int width) => Math.Clamp(width, 1, MaxWidth);
        private static int ClampQuality(int quality) => Math.Clamp(quality, 1, 100);
        private static int ClampBlur(int blur) => Math.Clamp(blur, 0, 100);

        /// <summary>
        /// Passes an upstream image through, but only if it really is an image. The headers stop browsers
        /// from sniffing it into something else and sandbox SVGs so embedded script can't run on this origin.
        /// </summary>
        private async Task<ActionResult> ToImageResult(HttpResponseMessage response, string fallbackContentType = "application/octet-stream")
        {
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode);
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? fallbackContentType;
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var content = await response.Content.ReadAsByteArrayAsync();

                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                return File(content, contentType);
            }
        }

    }
}
