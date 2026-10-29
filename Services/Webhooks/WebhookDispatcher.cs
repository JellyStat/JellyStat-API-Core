using Jellystat_API_Core.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace Jellystat_API_Core.Services.Webhooks
{
    public interface IWebhookPublisher
    {
        /// <summary>Queues an event for every enabled webhook subscribed to it. Never blocks or throws, so tasks can call it inline.</summary>
        void Publish(WebhookEvent evt);
    }

    /// <summary>
    /// Delivers published events in the background, one at a time, retrying network errors, 429s and 5xx responses.
    /// The queue is bounded: under a flood the oldest events are dropped rather than holding memory.
    /// </summary>
    public class WebhookDispatcher : BackgroundService, IWebhookPublisher
    {
        private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

        private readonly Channel<WebhookEvent> _queue = Channel.CreateBounded<WebhookEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebhookSender _sender;
        private readonly ILogger<WebhookDispatcher> _logger;

        public WebhookDispatcher(IServiceScopeFactory scopeFactory, IWebhookSender sender, ILogger<WebhookDispatcher> logger)
        {
            _scopeFactory = scopeFactory;
            _sender = sender;
            _logger = logger;
        }

        public void Publish(WebhookEvent evt) => _queue.Writer.TryWrite(evt);

        /// <summary>Stores the outcome of a delivery on the webhook, for the settings page.</summary>
        public static void Record(Data.Schema.Webhooks webhook, WebhookEvent evt, WebhookDeliveryResult result)
        {
            webhook.LastTriggeredAt = evt.Timestamp;
            webhook.LastEvent = evt.Event;
            webhook.LastSuccess = result.Success;
            webhook.LastError = result.Error is { Length: > 1000 } e ? e[..1000] : result.Error;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var evt in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await DeliverAsync(evt, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Webhook delivery of {Event} failed", evt.Event);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task DeliverAsync(WebhookEvent evt, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var webhooks = await db.Webhooks
                .Where(w => w.Enabled && w.Events.Contains(evt.Event) && (w.ServerId == null || w.ServerId == evt.ServerId))
                .ToListAsync(stoppingToken);
            if (webhooks.Count == 0) return;

            await Task.WhenAll(webhooks.Select(async webhook =>
            {
                var result = await _sender.SendAsync(webhook, evt, stoppingToken);
                for (int attempt = 0; !result.Success && IsRetryable(result) && attempt < RetryDelays.Length; attempt++)
                {
                    await Task.Delay(RetryDelays[attempt], stoppingToken);
                    result = await _sender.SendAsync(webhook, evt, stoppingToken);
                }
                if (!result.Success) _logger.LogWarning("Webhook {Name} failed for {Event}: {Error}", webhook.Name, evt.Event, result.Error);
                Record(webhook, evt, result);
            }));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        private static bool IsRetryable(WebhookDeliveryResult result) => result.StatusCode is null or 429 or >= 500;
    }
}
