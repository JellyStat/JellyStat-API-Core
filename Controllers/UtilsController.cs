using Jellystat_API_Core.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Jellystat_API_Core.Controllers
{
    /// <summary>
    /// Small utility endpoints that don't belong to a specific resource.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class UtilsController : ControllerBase
    {
        private const string GeoLiteUrlBase = "https://geolite.info/geoip/v2.1/city";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
        private const int MaxCacheEntries = 5000;

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GeoLite's free tier is rate limited, so remember recent lookups
        private static readonly ConcurrentDictionary<string, (DateTime Expires, GeolocationInfo Info)> _cache = new();

        public record GeolocationInfo(
            string IpAddress,
            string? City,
            string? Region,
            string? Country,
            string? CountryCode,
            string? PostalCode,
            string? Isp,
            double? Latitude,
            double? Longitude,
            int? AccuracyRadiusKm);

        /// <summary>
        /// Looks up the approximate location of a public IP address using MaxMind GeoLite.
        /// Requires JS_GEOLITE_ACCOUNT_ID and JS_GEOLITE_LICENSE_KEY to be set.
        /// </summary>
        [HttpGet("GeolocateIp", Name = "GeolocateIp")]
        public async Task<ActionResult<GeolocationInfo>> GeolocateIp(string IpAddress)
        {
            if (string.IsNullOrWhiteSpace(Global.GeoLiteAccountId) || string.IsNullOrWhiteSpace(Global.GeoLiteLicenseKey))
            {
                return StatusCode(StatusCodes.Status501NotImplemented,
                    "IP lookup is not configured. Set JS_GEOLITE_ACCOUNT_ID and JS_GEOLITE_LICENSE_KEY to enable it.");
            }

            if (!IPAddress.TryParse(IpAddress?.Trim(), out IPAddress? ip) || !NetworkHelper.IsPublicAddress(ip))
            {
                return BadRequest("A valid public IP address is required.");
            }

            string key = ip.ToString();
            if (_cache.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow)
            {
                return Ok(cached.Info);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{GeoLiteUrlBase}/{Uri.EscapeDataString(key)}");
                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Global.GeoLiteAccountId}:{Global.GeoLiteLicenseKey}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.UserAgent.ParseAdd(Global.UserAgent);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[GeolocateIp] GeoLite returned {(int)response.StatusCode}: {body}");

                    return response.StatusCode switch
                    {
                        HttpStatusCode.NotFound => NotFound("No location data for this IP address."),
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                            StatusCode(StatusCodes.Status502BadGateway, "GeoLite rejected the configured credentials."),
                        HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired =>
                            StatusCode(StatusCodes.Status503ServiceUnavailable, "GeoLite lookup limit reached. Try again later."),
                        _ => StatusCode(StatusCodes.Status502BadGateway, "GeoLite lookup failed."),
                    };
                }

                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                GeolocationInfo info = Parse(key, doc.RootElement);

                if (_cache.Count >= MaxCacheEntries)
                {
                    _cache.Clear();
                }
                _cache[key] = (DateTime.UtcNow.Add(CacheDuration), info);

                return Ok(info);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Debug.WriteLine($"[GeolocateIp] Error: {ex.Message}");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Could not reach the GeoLite service.");
            }
        }

        private static GeolocationInfo Parse(string ip, JsonElement root)
        {
            string? Name(string section)
            {
                if (root.TryGetProperty(section, out var el) && el.TryGetProperty("names", out var names) && names.TryGetProperty("en", out var en))
                    return en.GetString();
                return null;
            }

            string? region = null;
            if (root.TryGetProperty("subdivisions", out var subs) && subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0
                && subs[0].TryGetProperty("names", out var subNames) && subNames.TryGetProperty("en", out var subEn))
            {
                region = subEn.GetString();
            }

            string? countryCode = root.TryGetProperty("country", out var country) && country.TryGetProperty("iso_code", out var iso) ? iso.GetString() : null;
            string? postal = root.TryGetProperty("postal", out var p) && p.TryGetProperty("code", out var code) ? code.GetString() : null;
            string? isp = root.TryGetProperty("traits", out var traits) && traits.TryGetProperty("autonomous_system_organization", out var org) ? org.GetString() : null;

            double? lat = null, lon = null;
            int? accuracy = null;
            if (root.TryGetProperty("location", out var loc))
            {
                if (loc.TryGetProperty("latitude", out var la) && la.TryGetDouble(out var laVal)) lat = laVal;
                if (loc.TryGetProperty("longitude", out var lo) && lo.TryGetDouble(out var loVal)) lon = loVal;
                if (loc.TryGetProperty("accuracy_radius", out var ar) && ar.TryGetInt32(out var arVal)) accuracy = arVal;
            }

            return new GeolocationInfo(ip, Name("city"), region, Name("country"), countryCode, postal, isp, lat, lon, accuracy);
        }
    }
}
