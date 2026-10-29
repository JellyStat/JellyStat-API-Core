namespace Jellystat_API_Core.Models.Reports
{
    public class ReportColumn
    {
        public required string Name { get; set; }
        public required string Label { get; set; }
        /// <summary>A ReportFieldTypes value; tells renderers how to format the column by default.</summary>
        public required string Type { get; set; }
    }

    public class ReportDataResult
    {
        public required string DataSourceId { get; set; }
        public List<ReportColumn> Columns { get; set; } = [];
        public List<Dictionary<string, object?>> Rows { get; set; } = [];
        /// <summary>True when more rows matched than the data source limit allowed.</summary>
        public bool Truncated { get; set; }
        public string? Error { get; set; }
        public long ElapsedMs { get; set; }
    }

    /// <summary>Everything a renderer needs: the layout, the data for each data source, and the values used to produce it.</summary>
    public class ReportRenderModel
    {
        public string? ReportId { get; set; }
        public required string Name { get; set; }
        public required ReportDefinition Definition { get; set; }
        public Dictionary<string, ReportDataResult> Data { get; set; } = [];
        public Dictionary<string, object?> Parameters { get; set; } = [];
        /// <summary>Images fetched for server-side rendering (element id to data: URI). Never sent to clients.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, string> Images { get; set; } = [];
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class ReportDefinitionException : Exception
    {
        public ReportDefinitionException(string message) : base(message) { }
    }
}
