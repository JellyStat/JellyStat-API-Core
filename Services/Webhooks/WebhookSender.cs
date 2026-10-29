using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Webhooks
{
    public interface IWebhookSender
    {
        /// <summary>Posts one event to one webhook. Never throws for delivery failures; the result says what happened.</summary>
        Task<WebhookDeliveryResult> SendAsync(Data.Schema.Webhooks webhook, WebhookEvent evt, CancellationToken cancellationToken = default);
    }

    public partial class WebhookSender : IWebhookSender
    {
        public const string HttpClientName = "Webhooks";
        public const string SignatureHeader = "X-Jellystat-Signature";
        public const string EventHeader = "X-Jellystat-Event";

        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Discord rejects explicit nulls in embeds
        private static readonly JsonSerializerOptions DiscordJsonOptions = new(JsonOptions)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}")]
        private static partial Regex TokenPattern();

        private readonly IHttpClientFactory _httpClientFactory;

        public WebhookSender(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public static bool IsValidUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

        public static Dictionary<string, string> ParseHeaders(string? json) =>
            string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

        /// <summary>The standard payload every generic webhook gets, and what template tokens resolve against.</summary>
        public static object Envelope(WebhookEvent evt) => new
        {
            @event = evt.Event,
            timestamp = evt.Timestamp,
            server = evt.ServerId == null ? null : new { id = evt.ServerId, name = evt.ServerName },
            data = evt.Data
        };

        /// <summary>
        /// Replaces {{path}} tokens (e.g. {{data.user.name}}, {{data.items.0.name}}) with values from the envelope.
        /// Text is JSON-escaped without quotes, so text tokens belong inside a JSON string; numbers, objects and arrays are
        /// inserted as raw JSON; unknown and null tokens become empty.
        /// </summary>
        public static string ApplyTemplate(string template, WebhookEvent evt)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(Envelope(evt), JsonOptions));
            var root = doc.RootElement;
            return TokenPattern().Replace(template, match =>
            {
                JsonElement current = root;
                foreach (string segment in match.Groups[1].Value.Split('.'))
                {
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var child)) current = child;
                    else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out int index) && index >= 0 && index < current.GetArrayLength()) current = current[index];
                    else return "";
                }
                return current.ValueKind switch
                {
                    JsonValueKind.String => JsonEncodedText.Encode(current.GetString() ?? "", JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
                    JsonValueKind.Null => "",
                    _ => current.GetRawText()
                };
            });
        }

        public async Task<WebhookDeliveryResult> SendAsync(Data.Schema.Webhooks webhook, WebhookEvent evt, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            long Elapsed() => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            try
            {
                if (!IsValidUrl(webhook.Url)) return new(false, null, "The webhook URL must be an absolute http(s) URL", Elapsed());

                string body = !string.IsNullOrWhiteSpace(webhook.PayloadTemplate)
                    ? ApplyTemplate(webhook.PayloadTemplate, evt)
                    : webhook.Type == WebhookTypes.Discord
                        ? JsonSerializer.Serialize(DiscordMessage(evt), DiscordJsonOptions)
                        : JsonSerializer.Serialize(Envelope(evt), JsonOptions);
                byte[] bytes = Encoding.UTF8.GetBytes(body);

                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                foreach (var (name, value) in ParseHeaders(webhook.Headers))
                {
                    if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content.Headers.TryAddWithoutValidation(name, value);
                }
                request.Headers.UserAgent.ParseAdd(Global.UserAgent);
                request.Headers.Add(EventHeader, evt.Event);
                if (!string.IsNullOrEmpty(webhook.Secret) && webhook.Type != WebhookTypes.Discord)
                {
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhook.Secret));
                    request.Headers.Add(SignatureHeader, "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(bytes)));
                }

                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) return new(true, (int)response.StatusCode, null, Elapsed());

                string text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (text.Length > 300) text = text[..300];
                return new(false, (int)response.StatusCode, $"Webhook returned {(int)response.StatusCode} {response.ReasonPhrase}: {text}".TrimEnd(' ', ':'), Elapsed());
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A timeout surfaces as TaskCanceledException without our token being cancelled
                return new(false, null, ex is TaskCanceledException ? "The webhook timed out" : ex.Message, Elapsed());
            }
        }

        private static object DiscordMessage(WebhookEvent evt)
        {
            string footer = evt.ServerName ?? "Jellystat";
            object Embed(string title, string? description, int color, object[]? fields = null) => new
            {
                embeds = new[] { new { title = Clip(title, 256), description = description == null ? null : Clip(description, 4096), color, fields, footer = new { text = footer }, timestamp = evt.Timestamp } }
            };

            switch (evt.Data)
            {
                case WebhookPlaybackData p:
                    string title = p.Item.SeriesName != null
                        ? $"{p.Item.SeriesName}{(p.Item.SeasonNumber != null && p.Item.EpisodeNumber != null ? $" S{p.Item.SeasonNumber:00}E{p.Item.EpisodeNumber:00}" : "")} - {p.Item.Name}"
                        : p.Item.Name;
                    bool ended = evt.Event == WebhookEvents.PlaybackEnded;
                    var fields = new List<object>
                    {
                        new { name = "User", value = p.User.Name, inline = true },
                        new { name = "Client", value = $"{p.Client} ({p.Device})", inline = true },
                        new { name = "Play method", value = p.PlayMethod ?? "Unknown", inline = true }
                    };
                    if (p.WatchedSeconds is long watched) fields.Add(new { name = "Watched", value = FormatDuration(watched), inline = true });
                    return Embed($"{(ended ? "Stopped" : "Playing")}: {title}", null, ended ? 0x95A5A6 : 0x00A4DC, [.. fields]);

                case WebhookMediaAddedData m:
                    var lines = m.Items.Take(15).Select(i => $"- {i.Name} ({i.Type})").ToList();
                    if (m.Count > lines.Count) lines.Add($"...and {m.Count - lines.Count} more");
                    return Embed($"{m.Count} new item{(m.Count == 1 ? "" : "s")} added", string.Join("\n", lines), 0x2ECC71);

                case WebhookSyncCompletedData s:
                    var libraryLines = s.Libraries.Take(20).Select(l => $"- {l.Name}").ToList();
                    if (s.Libraries.Count > libraryLines.Count) libraryLines.Add($"...and {s.Libraries.Count - libraryLines.Count} more");
                    return Embed($"Full sync completed ({s.Libraries.Count} librar{(s.Libraries.Count == 1 ? "y" : "ies")})",
                        libraryLines.Count > 0 ? string.Join("\n", libraryLines) : null, 0x9B59B6,
                        [
                            new { name = "Added", value = s.Added.ToString(), inline = true },
                            new { name = "Updated", value = s.Updated.ToString(), inline = true },
                            new { name = "Archived", value = s.Archived.ToString(), inline = true },
                            new { name = "Took", value = FormatDuration(s.DurationSeconds), inline = true }
                        ]);

                case WebhookTestData t:
                    return Embed("Jellystat webhook test", t.Message, 0x3498DB);

                default:
                    return new { content = evt.Event };
            }
        }

        private static string Clip(string text, int max) => text.Length > max ? text[..(max - 1)] + "…" : text;

        private static string FormatDuration(long seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m {span.Seconds}s";
        }
    }
}
