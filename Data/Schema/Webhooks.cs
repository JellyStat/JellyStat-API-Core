namespace Jellystat_API_Core.Data.Schema
{
    /// <summary>Posts server events (playback, new media) to a URL as they happen.</summary>
    public class Webhooks
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Url { get; set; }
        /// <summary>generic | discord</summary>
        public string Type { get; set; } = "generic";
        /// <summary>Event keys this webhook receives, see WebhookEvents.</summary>
        public string[] Events { get; set; } = [];

        /// <summary>Only events from this server; null for every server.</summary>
        public string? ServerId { get; set; }
        public Server? Server { get; set; }

        public bool Enabled { get; set; } = true;
        /// <summary>Signs generic deliveries with HMAC-SHA256 (X-Jellystat-Signature) when set.</summary>
        public string? Secret { get; set; }
        /// <summary>Extra request headers as a JSON object (jsonb), e.g. an Authorization header.</summary>
        public string Headers { get; set; } = "{}";
        /// <summary>Custom request body with {{tokens}} from the event payload; null sends the standard payload.</summary>
        public string? PayloadTemplate { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
        public string? LastEvent { get; set; }
        public bool? LastSuccess { get; set; }
        public string? LastError { get; set; }
    }
}
