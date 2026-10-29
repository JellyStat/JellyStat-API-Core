namespace Jellystat_API_Core.Data.Schema
{
    public class Reports
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }

        /// <summary>ReportDefinition as JSON (jsonb). See Models/Reports/ReportDefinition.cs.</summary>
        public required string Definition { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>Renders a report on an interval and posts the file to a webhook.</summary>
    public class ReportSchedules
    {
        public required string Id { get; set; }
        public required string ReportId { get; set; }
        public Reports? Report { get; set; }

        /// <summary>Whose data the report shows; scheduled runs have no signed-in user to take it from.</summary>
        public required string ServerId { get; set; }
        public Server? Server { get; set; }

        public required string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public int IntervalMinutes { get; set; } = 24 * 60;

        /// <summary>pdf | png | svg | html | json</summary>
        public string Format { get; set; } = "pdf";
        public required string WebhookUrl { get; set; }
        /// <summary>generic | discord | json</summary>
        public string WebhookType { get; set; } = "generic";
        /// <summary>Signs generic/json deliveries with HMAC-SHA256 (X-Jellystat-Signature) when set.</summary>
        public string? WebhookSecret { get; set; }
        /// <summary>Text sent with the file; supports the same {{tokens}} as report text elements.</summary>
        public string? Message { get; set; }
        /// <summary>Report parameter values as a JSON object (jsonb).</summary>
        public string Parameters { get; set; } = "{}";

        public DateTime? LastRunAt { get; set; }
        public DateTime? NextRunAt { get; set; }
        public bool? LastSuccess { get; set; }
        public string? LastError { get; set; }
    }
}
