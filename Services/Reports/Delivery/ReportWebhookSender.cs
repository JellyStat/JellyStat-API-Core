using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Rendering;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Delivery
{
    public static class ReportWebhookTypes
    {
        /// <summary>multipart/form-data: "payload" (JSON metadata) + "file".</summary>
        public const string Generic = "generic";
        /// <summary>Discord webhook: payload_json with the message + the file as an attachment.</summary>
        public const string Discord = "discord";
        /// <summary>application/json: metadata plus the file as base64 (or the data itself for the json format).</summary>
        public const string Json = "json";

        public static readonly string[] All = [Generic, Discord, Json];
    }

    public class ReportWebhookTarget
    {
        public required string Url { get; set; }
        public string Type { get; set; } = ReportWebhookTypes.Generic;
        public string? Secret { get; set; }
        public string? Message { get; set; }
    }

    public interface IReportWebhookSender
    {
        Task SendAsync(ReportWebhookTarget target, ReportRenderModel model, ReportFile file, CancellationToken cancellationToken = default);
    }

    public class ReportWebhookSender : IReportWebhookSender
    {
        public const string HttpClientName = "ReportWebhooks";
        public const string SignatureHeader = "X-Jellystat-Signature";

        private readonly IHttpClientFactory _httpClientFactory;

        public ReportWebhookSender(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public static void ValidateUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new ReportDefinitionException("The webhook URL must be an absolute http(s) URL");
        }

        public async Task SendAsync(ReportWebhookTarget target, ReportRenderModel model, ReportFile file, CancellationToken cancellationToken = default)
        {
            ValidateUrl(target.Url);
            if (!ReportWebhookTypes.All.Contains(target.Type)) throw new ReportDefinitionException($"Unknown webhook type '{target.Type}'");

            string message = ReportTokenResolver.Resolve(target.Message ?? "{{report.name}} ({{now|datetime}} UTC)", model, 1, model.Definition.Pages.Count);
            var metadata = new
            {
                @event = "report.generated",
                reportId = model.ReportId,
                reportName = model.Name,
                generatedAt = model.GeneratedAt,
                parameters = model.Parameters,
                message,
                fileName = file.FileName,
                contentType = file.ContentType,
                size = file.Content.Length
            };

            HttpContent content;
            switch (target.Type)
            {
                case ReportWebhookTypes.Discord:
                    var discord = new MultipartFormDataContent();
                    // Discord rejects content over 2000 characters
                    discord.Add(new StringContent(JsonSerializer.Serialize(new { content = message.Length > 2000 ? message[..2000] : message }), Encoding.UTF8, "application/json"), "payload_json");
                    discord.Add(File(file), "files[0]", file.FileName);
                    content = discord;
                    break;

                case ReportWebhookTypes.Json:
                    object body = file.ContentType.StartsWith("application/json")
                        ? new { metadata, data = model.Data }
                        : new { metadata, data = Convert.ToBase64String(file.Content) };
                    content = new StringContent(JsonSerializer.Serialize(body, ReportJson.Options), Encoding.UTF8, "application/json");
                    break;

                default:
                    var multipart = new MultipartFormDataContent();
                    multipart.Add(new StringContent(JsonSerializer.Serialize(metadata, ReportJson.Options), Encoding.UTF8, "application/json"), "payload");
                    multipart.Add(File(file), "file", file.FileName);
                    content = multipart;
                    break;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url);
            if (!string.IsNullOrEmpty(target.Secret) && target.Type != ReportWebhookTypes.Discord)
            {
                // Sign the exact bytes that are sent, so the receiver can check them before parsing
                byte[] bytes = await content.ReadAsByteArrayAsync(cancellationToken);
                var signed = new ByteArrayContent(bytes);
                foreach (var header in content.Headers) signed.Headers.TryAddWithoutValidation(header.Key, header.Value);
                content.Dispose();
                content = signed;
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(target.Secret));
                request.Headers.Add(SignatureHeader, "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(bytes)));
            }
            request.Content = content;
            request.Headers.UserAgent.ParseAdd(Global.UserAgent);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (text.Length > 300) text = text[..300];
                throw new HttpRequestException($"Webhook returned {(int)response.StatusCode} {response.ReasonPhrase}: {text}");
            }
        }

        private static ByteArrayContent File(ReportFile file)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            return content;
        }
    }
}
