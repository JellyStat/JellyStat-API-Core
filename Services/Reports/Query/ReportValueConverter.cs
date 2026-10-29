using Jellystat_API_Core.Models.Reports;
using System.Globalization;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Query
{
    /// <summary>Converts JSON values from report definitions and parameters to the CLR type of the field they compare against.</summary>
    public static class ReportValueConverter
    {
        public static bool IsMissing(JsonElement? value) =>
            value == null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || (value.Value.ValueKind == JsonValueKind.String && value.Value.GetString() == "");

        public static object? Convert(JsonElement? value, Type targetType, string field)
        {
            if (value == null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            JsonElement v = value.Value;
            Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (type == typeof(string))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();

                if (type == typeof(bool))
                    return v.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => bool.Parse(AsString(v))
                    };

                if (type.IsEnum)
                    return v.ValueKind == JsonValueKind.Number
                        ? Enum.ToObject(type, v.GetInt32())
                        : Enum.Parse(type, AsString(v), ignoreCase: true);

                if (type == typeof(DateTime))
                    return ParseDate(AsString(v));

                if (type == typeof(int)) return v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : int.Parse(AsString(v), CultureInfo.InvariantCulture);
                if (type == typeof(long)) return v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : long.Parse(AsString(v), CultureInfo.InvariantCulture);
                if (type == typeof(double)) return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.Parse(AsString(v), CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or InvalidOperationException)
            {
                throw new ReportDefinitionException($"'{v}' is not a valid value for {field}");
            }

            throw new ReportDefinitionException($"Field {field} has an unsupported type");
        }

        public static List<object?> ConvertList(JsonElement? value, Type targetType, string field)
        {
            if (value == null) return [];
            if (value.Value.ValueKind == JsonValueKind.Array)
                return value.Value.EnumerateArray().Select(e => Convert(e, targetType, field)).ToList();
            // "a, b, c" typed into a single text box
            if (value.Value.ValueKind == JsonValueKind.String)
                return value.Value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => Convert(JsonSerializer.SerializeToElement(s), targetType, field)).ToList();
            return [Convert(value, targetType, field)];
        }

        public static double ToDouble(JsonElement? value, string field)
        {
            object? converted = Convert(value, typeof(double), field);
            return converted is double d ? d : throw new ReportDefinitionException($"A number is required for {field}");
        }

        /// <summary>ISO 8601 date or date-time; values without an offset are taken as UTC.</summary>
        public static DateTime ParseDate(string s) =>
            DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        private static string AsString(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
    }
}
