namespace Jellystat_API_Core.Models.Webhooks
{
    public class WebhookModel
    {
        /// <summary>Null to create a new webhook.</summary>
        public string? Id { get; set; }
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Type { get; set; } = "generic";
        public string[] Events { get; set; } = [];
        /// <summary>Only events from this server; null for every server.</summary>
        public string? ServerId { get; set; }
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string>? Headers { get; set; }
        public string? PayloadTemplate { get; set; }

        /// <summary>Write only: null keeps the saved secret, "" removes it.</summary>
        public string? Secret { get; set; }
        public bool HasSecret { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
        public string? LastEvent { get; set; }
        public bool? LastSuccess { get; set; }
        public string? LastError { get; set; }
    }

    public class WebhookEventInfo
    {
        public required string Key { get; set; }
        public required string Label { get; set; }
        public required string Description { get; set; }
        /// <summary>The standard payload for this event with sample values; template tokens are paths into it.</summary>
        public required object Sample { get; set; }
    }
}
