using Jellystat_API_Core.Services.Reports.Datasets;
using System.Text.Json;

namespace Jellystat_API_Core.Models.Reports
{
    public class ReportSummary
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
        public int ScheduleCount { get; set; }
    }

    public class ReportDetail : ReportSummary
    {
        public required ReportDefinition Definition { get; set; }
    }

    public class SaveReportModel
    {
        /// <summary>Null to create a new report.</summary>
        public string? Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public ReportDefinition Definition { get; set; } = new();
    }

    public class ReportPreviewRequest
    {
        public ReportDefinition Definition { get; set; } = new();
        public string? Name { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public int? MaxRows { get; set; }
        public string? ServerId { get; set; }
    }

    public class ReportQueryRequest
    {
        public ReportDataSource DataSource { get; set; } = new();
        /// <summary>Parameter declarations, so parameter-bound rules can use their defaults.</summary>
        public List<ReportParameter>? ParameterDefinitions { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public int? MaxRows { get; set; }
        public string? ServerId { get; set; }
    }

    public class ReportParameterOptionsRequest
    {
        public string? ReportId { get; set; }
        public ReportDefinition? Definition { get; set; }
        public string Parameter { get; set; } = "";
        /// <summary>Current values of the other parameters, which the lookup's data source may filter on.</summary>
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public string? ServerId { get; set; }
    }

    public class ReportRenderRequest
    {
        /// <summary>A saved report, or...</summary>
        public string? ReportId { get; set; }
        /// <summary>...an unsaved definition (e.g. the designer's current state).</summary>
        public ReportDefinition? Definition { get; set; }
        public string? Name { get; set; }
        public string Format { get; set; } = "pdf";
        public int? Page { get; set; }
        public double? Scale { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public string? ServerId { get; set; }
    }

    public class ReportDatasetInfo
    {
        public required string Key { get; set; }
        public required string Label { get; set; }
        public required string Description { get; set; }
        public required IReadOnlyList<ReportFieldInfo> Fields { get; set; }
    }

    public class ReportCapabilities
    {
        public required List<ReportDatasetInfo> Datasets { get; set; }
        public required string[] Engines { get; set; }
        public required string[] Operators { get; set; }
        public required string[] Aggregates { get; set; }
        public required string[] DateBuckets { get; set; }
        public required string[] Formats { get; set; }
        public required string[] ExportFormats { get; set; }
        public required string[] WebhookTypes { get; set; }
        public int MaxRows { get; set; }
    }

    public class ReportScheduleModel
    {
        public string? Id { get; set; }
        public string ReportId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>Defaults to the caller's server.</summary>
        public string? ServerId { get; set; }
        public int IntervalMinutes { get; set; } = 24 * 60;
        public string Format { get; set; } = "pdf";
        public string WebhookUrl { get; set; } = "";
        public string WebhookType { get; set; } = "generic";
        /// <summary>On save: null keeps the stored secret, "" removes it. Never returned.</summary>
        public string? WebhookSecret { get; set; }
        public bool HasSecret { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime? NextRunAt { get; set; }
        public bool? LastSuccess { get; set; }
        public string? LastError { get; set; }
    }

    public class ReportDeliverRequest
    {
        public string? ReportId { get; set; }
        public ReportDefinition? Definition { get; set; }
        public string? Name { get; set; }
        public string WebhookUrl { get; set; } = "";
        public string WebhookType { get; set; } = "generic";
        public string? WebhookSecret { get; set; }
        public string Format { get; set; } = "pdf";
        public string? Message { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public string? ServerId { get; set; }
    }
}
