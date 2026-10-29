using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Webhooks;
using Jellystat_API_Core.Services.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Jellystat_API_Core.Controllers
{
    /// <summary>
    /// Event webhooks: post playback and new-media events to Discord or any URL as they happen.
    /// Scheduled report delivery lives under /Reports/Schedules. Administrators only.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class WebhooksController : ControllerBase
    {
        private const int MaxTemplateLength = 20_000;

        private static readonly Dictionary<string, (string Label, string Description)> EventInfo = new()
        {
            [WebhookEvents.PlaybackStarted] = ("Playback started", "A user starts playing something."),
            [WebhookEvents.PlaybackEnded] = ("Playback ended", "A user stops playing something, with how long they watched."),
            [WebhookEvents.MediaAdded] = ("Media added", "A sync finds new movies, series, episodes or music (one event per sync, not on a server's first sync)."),
            [WebhookEvents.SyncCompleted] = ("Full sync completed", "A full sync finishes, with the libraries synced and how many items were added, updated and archived."),
        };

        private readonly ApplicationDbContext context;
        private readonly IWebhookSender _sender;

        public WebhooksController(ApplicationDbContext context, IWebhookSender sender)
        {
            this.context = context;
            _sender = sender;
        }

        private static WebhookModel ToModel(Data.Schema.Webhooks w) => new()
        {
            Id = w.Id,
            Name = w.Name,
            Url = w.Url,
            Type = w.Type,
            Events = w.Events,
            ServerId = w.ServerId,
            Enabled = w.Enabled,
            Headers = WebhookSender.ParseHeaders(w.Headers),
            PayloadTemplate = w.PayloadTemplate,
            HasSecret = !string.IsNullOrEmpty(w.Secret),
            CreatedAt = w.CreatedAt,
            LastTriggeredAt = w.LastTriggeredAt,
            LastEvent = w.LastEvent,
            LastSuccess = w.LastSuccess,
            LastError = w.LastError
        };

        [HttpGet("Events", Name = "WebhookEvents")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public List<WebhookEventInfo> Events() => WebhookEvents.All.Select(key => new WebhookEventInfo
        {
            Key = key,
            Label = EventInfo[key].Label,
            Description = EventInfo[key].Description,
            Sample = WebhookSender.Envelope(WebhookSamples.For(key, "sample-server-id", "My Server"))
        }).ToList();

        [HttpGet("List", Name = "ListWebhooks")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<List<WebhookModel>> List()
        {
            return (await context.Webhooks.AsNoTracking().OrderBy(w => w.Name).ToListAsync()).Select(ToModel).ToList();
        }

        [HttpPost("Save", Name = "SaveWebhook")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<WebhookModel>> Save([FromBody] WebhookModel model)
        {
            string name = (model.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200) return BadRequest("A webhook name of 1-200 characters is required");
            string url = (model.Url ?? "").Trim();
            if (!WebhookSender.IsValidUrl(url)) return BadRequest("The webhook URL must be an absolute http(s) URL");
            if (!WebhookTypes.All.Contains(model.Type)) return BadRequest($"Type must be one of: {string.Join(", ", WebhookTypes.All)}");
            string[] events = (model.Events ?? []).Distinct().ToArray();
            if (events.Length == 0) return BadRequest("Pick at least one event");
            if (events.FirstOrDefault(e => !WebhookEvents.All.Contains(e)) is string unknown) return BadRequest($"Unknown event '{unknown}'");
            if (!string.IsNullOrEmpty(model.ServerId) && !await context.Servers.AnyAsync(s => s.Id == model.ServerId)) return BadRequest("Unknown ServerId");
            if (model.PayloadTemplate is { Length: > MaxTemplateLength }) return BadRequest($"The payload template can be at most {MaxTemplateLength} characters");

            var headers = new Dictionary<string, string>();
            foreach (var (key, value) in model.Headers ?? [])
            {
                string headerName = key.Trim();
                if (headerName.Length == 0) continue;
                // RFC 7230 token characters only, and no line breaks in values (header injection)
                if (!headerName.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c))) return BadRequest($"Invalid header name '{headerName}'");
                if (value.Contains('\r') || value.Contains('\n')) return BadRequest($"Header '{headerName}' can't contain line breaks");
                headers[headerName] = value;
            }

            Data.Schema.Webhooks? webhook = null;
            if (!string.IsNullOrEmpty(model.Id))
            {
                webhook = await context.Webhooks.FirstOrDefaultAsync(w => w.Id == model.Id);
                if (webhook == null) return NotFound("Webhook not found");
            }
            if (webhook == null)
            {
                webhook = new Data.Schema.Webhooks { Id = Guid.NewGuid().ToString(), Name = name, Url = url, CreatedAt = DateTime.UtcNow };
                context.Webhooks.Add(webhook);
            }

            webhook.Name = name;
            webhook.Url = url;
            webhook.Type = model.Type;
            webhook.Events = events;
            webhook.ServerId = string.IsNullOrEmpty(model.ServerId) ? null : model.ServerId;
            webhook.Enabled = model.Enabled;
            webhook.Headers = JsonSerializer.Serialize(headers);
            webhook.PayloadTemplate = string.IsNullOrWhiteSpace(model.PayloadTemplate) ? null : model.PayloadTemplate;
            if (model.Secret != null) webhook.Secret = model.Secret.Length == 0 ? null : model.Secret;
            webhook.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return ToModel(webhook);
        }

        [HttpDelete("Delete", Name = "DeleteWebhook")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> Delete(string Id)
        {
            int removed = await context.Webhooks.Where(w => w.Id == Id).ExecuteDeleteAsync();
            return removed == 0 ? NotFound("Webhook not found") : Ok();
        }

        /// <summary>Sends a sample of one of the webhook's events (or a plain test message) right now, without retries.</summary>
        [HttpPost("Test", Name = "TestWebhook")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<WebhookDeliveryResult>> Test(string Id, string? Event, CancellationToken cancellationToken)
        {
            var webhook = await context.Webhooks.FirstOrDefaultAsync(w => w.Id == Id, cancellationToken);
            if (webhook == null) return NotFound("Webhook not found");
            if (Event != null && !WebhookEvents.All.Contains(Event)) return BadRequest($"Unknown event '{Event}'");

            string serverId = webhook.ServerId ?? HttpContext.User.GetServerId();
            string? serverName = await context.Servers.Where(s => s.Id == serverId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
            var evt = WebhookSamples.For(Event ?? WebhookEvents.Test, serverId, serverName);

            var result = await _sender.SendAsync(webhook, evt, cancellationToken);
            WebhookDispatcher.Record(webhook, evt, result);
            await context.SaveChangesAsync(CancellationToken.None);
            return result;
        }
    }
}
