using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    /// <summary>
    /// Value formatting shared by every renderer. lib/reports/format.ts in the UI implements the same rules,
    /// so the designer, server renders and exports read the same.
    /// </summary>
    public static class ReportFormatter
    {
        /// <summary>The report's time zone (IANA id), or UTC when unset or unknown.</summary>
        public static TimeZoneInfo Zone(string? timeZone) =>
            !string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var tz) ? tz : TimeZoneInfo.Utc;

        public static bool IsUtc(TimeZoneInfo tz) => tz.Id is "UTC" or "Etc/UTC" or "Etc/UCT" or "Universal" or "Coordinated Universal Time" || tz == TimeZoneInfo.Utc;

        public static readonly string[] Formats = ["auto", "text", "link", "number", "integer", "decimal", "percent", "duration", "bytes", "date", "datetime", "time"];

        public static string Format(object? value, string? format, string? columnType = null, TimeZoneInfo? timeZone = null)
        {
            if (value == null) return "";
            if (value is JsonElement je) value = Unwrap(je);
            if (value == null) return "";

            string f = string.IsNullOrEmpty(format) || format == "auto" ? AutoFormat(columnType) : format!;
            switch (f)
            {
                case "integer": return TryDouble(value, out var i) ? Math.Round(i).ToString("N0", CultureInfo.InvariantCulture) : value.ToString()!;
                case "decimal": return TryDouble(value, out var dd) ? dd.ToString("N2", CultureInfo.InvariantCulture) : value.ToString()!;
                case "number": return TryDouble(value, out var n) ? Number(n) : value.ToString()!;
                case "percent": return TryDouble(value, out var p) ? p.ToString("0.#", CultureInfo.InvariantCulture) + "%" : value.ToString()!;
                case "duration": return TryDouble(value, out var s) ? Duration(s) : value.ToString()!;
                case "bytes": return TryDouble(value, out var b) ? Bytes(b) : value.ToString()!;
                case "date": return TryDate(value, out var d1) ? Local(d1, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value.ToString()!;
                case "datetime": return TryDate(value, out var d2) ? Local(d2, timeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : value.ToString()!;
                case "time": return TryDate(value, out var d3) ? Local(d3, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture) : value.ToString()!;
                default:
                    return value switch
                    {
                        bool bo => bo ? "Yes" : "No",
                        DateTime dt => Local(dt, timeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        double or float or decimal => Number(System.Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
                        _ => value.ToString() ?? ""
                    };
            }
        }

        private static string AutoFormat(string? columnType) => columnType switch
        {
            ReportFieldTypes.Duration => "duration",
            ReportFieldTypes.Bytes => "bytes",
            ReportFieldTypes.Date => "datetime",
            ReportFieldTypes.Integer => "integer",
            ReportFieldTypes.Number => "number",
            _ => "text"
        };

        private static DateTime Local(DateTime utc, TimeZoneInfo? tz) =>
            tz == null || IsUtc(tz) ? utc : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);

        public static string Number(double n) =>
            Math.Abs(n % 1) < 0.0000001 ? n.ToString("N0", CultureInfo.InvariantCulture) : n.ToString("#,0.##", CultureInfo.InvariantCulture);

        public static string Duration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long total = (long)Math.Round(seconds);
            long d = total / 86400, h = total % 86400 / 3600, m = total % 3600 / 60, s = total % 60;
            if (d > 0) return h > 0 ? $"{d}d {h}h" : $"{d}d";
            if (h > 0) return m > 0 ? $"{h}h {m}m" : $"{h}h";
            if (m > 0) return $"{m}m";
            return $"{s}s";
        }

        public static string Bytes(double bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
            int u = 0;
            while (Math.Abs(bytes) >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
            return (u == 0 ? bytes.ToString("0", CultureInfo.InvariantCulture) : bytes.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[u];
        }

        public static bool TryDouble(object? value, out double result)
        {
            switch (value)
            {
                case null: result = 0; return false;
                case double d: result = d; return true;
                case float f: result = f; return true;
                case int i: result = i; return true;
                case long l: result = l; return true;
                case decimal m: result = (double)m; return true;
                case short sh: result = sh; return true;
                case JsonElement { ValueKind: JsonValueKind.Number } je: result = je.GetDouble(); return true;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                default: result = 0; return false;
            }
        }

        public static bool TryDate(object? value, out DateTime result)
        {
            switch (value)
            {
                case DateTime d: result = d.ToUniversalTime(); return true;
                case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed):
                    result = parsed; return true;
                default: result = default; return false;
            }
        }

        public static object? Unwrap(JsonElement je) => je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => string.Join(", ", je.EnumerateArray().Select(e => Convert.ToString(Unwrap(e), CultureInfo.InvariantCulture))),
            _ => je.GetRawText()
        };
    }

    /// <summary>
    /// Replaces {{tokens}} in text: report.name, now, param.NAME, ds.SOURCE.FIELD (first row; SOURCE is the data source
    /// name or id), ds.SOURCE.rowCount, page.number, page.count. Append |format to pick a format, e.g. {{ds.Totals.watch|duration}}.
    /// </summary>
    public static partial class ReportTokenResolver
    {
        [GeneratedRegex(@"\{\{\s*([^{}|]+?)\s*(?:\|\s*([a-zA-Z]+)\s*)?\}\}")]
        private static partial Regex TokenRegex();

        /// <summary>Replaces {{tokens}}; with a row (card grids), {{row.FIELD}} is that row's value.</summary>
        public static string Resolve(string? text, ReportRenderModel model, int pageNumber, int pageCount,
            IReadOnlyDictionary<string, object?>? row = null, IReadOnlyList<ReportColumn>? columns = null)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return TokenRegex().Replace(text, m =>
            {
                string path = m.Groups[1].Value;
                string? format = m.Groups[2].Success ? m.Groups[2].Value : null;
                var (value, type) = Lookup(path, model, pageNumber, pageCount, row, columns);
                return value == null && type == "missing" ? m.Value : ReportFormatter.Format(value, format, type, ReportFormatter.Zone(model.Definition.TimeZone));
            });
        }

        private static (object? Value, string? Type) Lookup(string path, ReportRenderModel model, int pageNumber, int pageCount,
            IReadOnlyDictionary<string, object?>? row = null, IReadOnlyList<ReportColumn>? columns = null)
        {
            string[] parts = path.Split('.', 3);
            switch (parts[0].ToLowerInvariant())
            {
                case "row" when row != null && parts.Length > 1:
                    string field = path[4..];
                    var key = row.Keys.FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
                    if (key == null) return (null, "missing");
                    return (row[key], columns?.FirstOrDefault(c => c.Name == key)?.Type);
                case "now": return (model.GeneratedAt, ReportFieldTypes.Date);
                case "report" when parts.Length > 1 && parts[1].Equals("name", StringComparison.OrdinalIgnoreCase): return (model.Name, null);
                case "page" when parts.Length > 1:
                    return parts[1].Equals("count", StringComparison.OrdinalIgnoreCase) ? (pageCount, ReportFieldTypes.Integer) : (pageNumber, ReportFieldTypes.Integer);
                case "param" when parts.Length > 1:
                    var paramKey = model.Parameters.Keys.FirstOrDefault(k => k.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                    return paramKey == null ? (null, "missing") : (model.Parameters[paramKey], null);
                case "ds" when parts.Length > 2:
                    var data = FindData(model, parts[1]);
                    if (data == null) return (null, "missing");
                    if (parts[2].Equals("rowCount", StringComparison.OrdinalIgnoreCase)) return (data.Rows.Count, ReportFieldTypes.Integer);
                    var column = data.Columns.FirstOrDefault(c => c.Name.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
                    if (column == null) return (null, "missing");
                    return (data.Rows.Count > 0 ? data.Rows[0].GetValueOrDefault(column.Name) : null, column.Type);
            }
            return (null, "missing");
        }

        /// <summary>
        /// Builds a hyperlink from a template: {{value}} is the linked value, {{row.FIELD}} another column of the same
        /// row, and the usual tokens work too. A token at the very start goes in as it is (it is the URL or its base);
        /// anywhere else it is URL-encoded. Only http, https and mailto links are returned, anything else gives null.
        /// </summary>
        public static string? ResolveLink(string? template, object? value, IReadOnlyDictionary<string, object?>? row,
            ReportRenderModel model, int pageNumber, int pageCount)
        {
            string t = string.IsNullOrWhiteSpace(template) ? "{{value}}" : template.Trim();
            var zone = ReportFormatter.Zone(model.Definition.TimeZone);
            string url = TokenRegex().Replace(t, m =>
            {
                string path = m.Groups[1].Value.Trim();
                string? format = m.Groups[2].Success ? m.Groups[2].Value : null;
                object? v;
                if (path.Equals("value", StringComparison.OrdinalIgnoreCase)) v = value;
                else if (path.StartsWith("row.", StringComparison.OrdinalIgnoreCase) && row != null)
                {
                    string field = path[4..];
                    v = row.FirstOrDefault(kv => kv.Key.Equals(field, StringComparison.OrdinalIgnoreCase)).Value;
                }
                else
                {
                    var (found, type) = Lookup(path, model, pageNumber, pageCount);
                    if (found == null && type == "missing") return "";
                    v = format == null ? found : ReportFormatter.Format(found, format, type, zone);
                }
                if (v is JsonElement je) v = ReportFormatter.Unwrap(je);
                string text = v switch
                {
                    null => "",
                    string s => s,
                    DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                    bool b => b ? "true" : "false",
                    IFormattable f => format == null ? f.ToString(null, CultureInfo.InvariantCulture) : ReportFormatter.Format(v, format, null, zone),
                    _ => v.ToString() ?? ""
                };
                return m.Index == 0 ? text.Trim() : Uri.EscapeDataString(text);
            });
            return SafeLink(url);
        }

        public static string? SafeLink(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? url : null;
        }

        public static ReportDataResult? FindData(ReportRenderModel model, string? nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return null;
            if (model.Data.TryGetValue(nameOrId, out var byId)) return byId;
            var source = model.Definition.DataSources.FirstOrDefault(d => d.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
            return source != null ? model.Data.GetValueOrDefault(source.Id) : null;
        }
    }
}
