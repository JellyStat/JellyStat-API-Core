namespace Jellystat_API_Core.Services.Reports.Datasets
{
    public static class ReportFieldTypes
    {
        public const string String = "string";
        public const string Number = "number";
        public const string Integer = "integer";
        public const string Boolean = "boolean";
        public const string Date = "date";
        /// <summary>Seconds.</summary>
        public const string Duration = "duration";
        public const string Bytes = "bytes";
        public const string Enum = "enum";
    }

    /// <summary>
    /// Marks a dataset row property as visible to reports. Only marked properties can be selected, filtered, grouped
    /// or sorted, which is what keeps report definitions (user input) from reaching anything else.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class ReportFieldAttribute : Attribute
    {
        public string Label { get; }
        public string Type { get; }
        public bool Groupable { get; set; } = true;
        public bool Aggregatable { get; set; }
        public string? Description { get; set; }

        public ReportFieldAttribute(string label, string type)
        {
            Label = label;
            Type = type;
        }
    }

    public class ReportFieldInfo
    {
        public required string Key { get; set; }
        public required string Label { get; set; }
        public required string Type { get; set; }
        public bool Groupable { get; set; }
        public bool Aggregatable { get; set; }
        /// <summary>Whether the field can be empty, i.e. supports isNull/isNotNull.</summary>
        public bool Nullable { get; set; }
        public string? Description { get; set; }
        public List<string>? Options { get; set; }
    }
}
