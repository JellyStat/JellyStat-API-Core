using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Mvc;

namespace Jellystat_API_Core.Controllers
{
    //[Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ProxyController : Controller
    {
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

            Uri? iconUri = iconUrl != null ? (Uri.TryCreate(iconUrl, UriKind.RelativeOrAbsolute, out var result) ? result : null) : null;

            var response = await api.GetDeviceImage(icon, iconUri);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/svg+xml";
            return File(content, contentType);
        }

        [HttpGet("Images/Items/Backdrop", Name = "Images/Items/Backdrop")]
        public async Task<ActionResult> ProxyItemBackdropImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetBackdropImage(Id, Width, Quality, Blur);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return File(content, contentType);
        }

        [HttpGet("Images/Items/Primary", Name = "Images/Items/Primary")]
        public async Task<ActionResult> ProxyItemPrimaryImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {

            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetItemImage(Id, Width, Quality, Blur);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return File(content, contentType);
        }

        [HttpGet("Images/User/Primary", Name = "Images/User/Primary")]
        public async Task<ActionResult> ProxyUserPrimaryImageRequest(string ServerId, string Id, int Width = 800, int Quality = 100, int Blur = 0)
        {
            iAPI? api = _apiInstanceManager.GetInstance(ServerId);
            if (api == null)
            {
                return NotFound("API instance not found for provided Server Id");
            }

            var response = await api.GetUserImage(Id, Width, Quality, Blur);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return File(content, contentType);
        }

    }
}
