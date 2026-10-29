using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Rendering;
using System.Globalization;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Query
{
    /// <summary>
    /// Steps applied, in order, to a data source's result after the database query: things SQL grouping alone can't
    /// express, like pivots, top N with "Other", running totals or filling gaps in a time series. They run on the
    /// server, so every renderer and export sees the same rows.
    /// </summary>
    public static class ReportTransforms
    {
        public static readonly string[] Types = ["filter", "topN", "pivot", "runningTotal", "percentOfTotal", "rank", "change", "fillDates", "sort", "limit"];

        private static readonly HashSet<string> NumericTypes =
            [ReportFieldTypes.Number, ReportFieldTypes.Integer, ReportFieldTypes.Duration, ReportFieldTypes.Bytes];

        private sealed class RowComparer(string field) : IComparer<Dictionary<string, object?>>
        {
            public int Compare(Dictionary<string, object?>? a, Dictionary<string, object?>? b) =>
                CompareValues(a?.GetValueOrDefault(field), b?.GetValueOrDefault(field));
        }

        public static void Apply(ReportDataResult result, IReadOnlyList<ReportTransform> transforms,
            IReadOnlyDictionary<string, JsonElement?> parameters, TimeZoneInfo timeZone, DateTime utcNow)
        {
            foreach (var t in transforms)
            {
                switch (t.Type)
                {
                    case "filter": Filter(result, t, parameters); break;
                    case "topN": TopN(result, t); break;
                    case "pivot": Pivot(result, t); break;
                    case "runningTotal": Window(result, t, "running", RunningTotal); break;
                    case "percentOfTotal": Window(result, t, "pct", PercentOfTotal); break;
                    case "rank": Window(result, t, "rank", Rank); break;
                    case "change": Window(result, t, "change", Change); break;
                    case "fillDates": FillDates(result, t, parameters, timeZone, utcNow); break;
                    case "sort": Sort(result, t.Sort ?? []); break;
                    case "limit":
                        int n = Math.Max(0, t.Count ?? 10);
                        if (result.Rows.Count > n) result.Rows = result.Rows.Take(n).ToList();
                        break;
                    default: throw new ReportDefinitionException($"Unknown transform '{t.Type}'");
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static ReportColumn Column(ReportDataResult result, string? name, string transform)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ReportDefinitionException($"The {transform} step needs a column");
            return result.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ReportDefinitionException($"The {transform} step uses column '{name}', which the data source doesn't return");
        }

        private static ReportColumn? OptionalColumn(ReportDataResult result, string? name, string transform) =>
            string.IsNullOrWhiteSpace(name) ? null : Column(result, name, transform);

        private static double? Number(object? value) => ReportFormatter.TryDouble(value, out var d) ? d : null;

        public static int CompareValues(object? a, object? b)
        {
            if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
            if (ReportFormatter.TryDouble(a, out var x) && ReportFormatter.TryDouble(b, out var y)) return x.CompareTo(y);
            if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
            return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        private static string Key(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        private static IEnumerable<List<Dictionary<string, object?>>> Partitions(List<Dictionary<string, object?>> rows, ReportColumn? partition) =>
            partition == null ? [rows] : rows.GroupBy(r => Key(r.GetValueOrDefault(partition.Name))).Select(g => g.ToList());

        private static void AddColumn(ReportDataResult result, string name, string label, string type)
        {
            result.Columns.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            result.Columns.Add(new ReportColumn { Name = name, Label = label, Type = type });
        }

        // ------------------------------------------------------------------ steps

        /// <summary>Keeps rows matching one condition on an output column (e.g. plays &gt; 10 after grouping).</summary>
        private static void Filter(ReportDataResult result, ReportTransform t, IReadOnlyDictionary<string, JsonElement?> parameters)
        {
            var column = Column(result, t.Field, "filter");
            JsonElement? raw = t.Value;
            if (!string.IsNullOrWhiteSpace(t.Parameter))
            {
                if (!parameters.TryGetValue(t.Parameter!, out raw) || ReportValueConverter.IsMissing(raw)) return;
            }
            string op = t.Operator ?? "eq";
            object? value = raw.HasValue ? ReportFormatter.Unwrap(raw.Value) : null;
            var values = raw is { ValueKind: JsonValueKind.Array } arr ? arr.EnumerateArray().Select(e => ReportFormatter.Unwrap(e)).ToList() : [value];

            bool Match(object? v) => op switch
            {
                "isNull" => v == null || v as string == "",
                "isNotNull" => !(v == null || v as string == ""),
                "eq" => values.Any(x => CompareValues(v, x) == 0),
                "neq" => values.All(x => CompareValues(v, x) != 0),
                "in" => values.Any(x => CompareValues(v, x) == 0),
                "notIn" => values.All(x => CompareValues(v, x) != 0),
                "gt" => v != null && CompareValues(v, value) > 0,
                "gte" => v != null && CompareValues(v, value) >= 0,
                "lt" => v != null && CompareValues(v, value) < 0,
                "lte" => v != null && CompareValues(v, value) <= 0,
                "contains" => Key(v).Contains(Key(value), StringComparison.OrdinalIgnoreCase),
                "notContains" => !Key(v).Contains(Key(value), StringComparison.OrdinalIgnoreCase),
                _ => throw new ReportDefinitionException($"Unknown filter operator '{op}'")
            };
            result.Rows = result.Rows.Where(r => Match(r.GetValueOrDefault(column.Name))).ToList();
        }

        /// <summary>The N rows with the highest (or lowest) value, per partition, optionally folding the rest into "Other".</summary>
        private static void TopN(ReportDataResult result, ReportTransform t)
        {
            var measure = Column(result, t.Field, "top N");
            var partition = OptionalColumn(result, t.PartitionBy, "top N");
            int n = Math.Max(1, t.Count ?? 10);
            var label = OptionalColumn(result, t.LabelField, "top N")
                ?? result.Columns.FirstOrDefault(c => c != partition && c != measure && !NumericTypes.Contains(c.Type));

            var output = new List<Dictionary<string, object?>>();
            foreach (var rows in Partitions(result.Rows, partition))
            {
                var ordered = rows.OrderBy(r => r, new RowComparer(measure.Name)).ToList();
                if (t.Desc) ordered.Reverse();
                output.AddRange(ordered.Take(n));
                var rest = ordered.Skip(n).ToList();
                if (rest.Count == 0 || string.IsNullOrEmpty(t.OtherLabel) || label == null) continue;

                // "Other": numeric columns summed, the label column named, the partition kept
                var other = result.Columns.ToDictionary(c => c.Name, c => (object?)null);
                foreach (var c in result.Columns.Where(c => NumericTypes.Contains(c.Type)))
                    other[c.Name] = rest.Sum(r => Number(r.GetValueOrDefault(c.Name)) ?? 0);
                other[label.Name] = t.OtherLabel;
                if (partition != null) other[partition.Name] = rest[0].GetValueOrDefault(partition.Name);
                output.Add(other);
            }
            result.Rows = output;
        }

        /// <summary>Turns the distinct values of one column into columns, e.g. plays per user (rows) per month (columns).</summary>
        private static void Pivot(ReportDataResult result, ReportTransform t)
        {
            var rowColumn = Column(result, t.RowField, "pivot");
            var pivotColumn = Column(result, t.ColumnField, "pivot");
            var valueColumn = Column(result, t.Field, "pivot");
            string fn = t.Aggregate ?? "sum";

            var columnKeys = result.Rows.Select(r => Key(r.GetValueOrDefault(pivotColumn.Name))).Distinct().ToList();
            if (t.SortColumns) columnKeys.Sort((a, b) => CompareValues(a, b));
            if (columnKeys.Count > 200) throw new ReportDefinitionException("A pivot can make at most 200 columns; filter or group the data first");

            string valueType = fn == "count" ? ReportFieldTypes.Integer : valueColumn.Type;
            var newColumns = new List<ReportColumn> { rowColumn };
            foreach (var key in columnKeys)
            {
                string name = key.Length == 0 ? "(empty)" : key;
                if (name.Equals(rowColumn.Name, StringComparison.OrdinalIgnoreCase)) name += " ";
                newColumns.Add(new ReportColumn { Name = name, Label = name, Type = valueType });
            }

            var output = new List<Dictionary<string, object?>>();
            foreach (var group in result.Rows.GroupBy(r => Key(r.GetValueOrDefault(rowColumn.Name))))
            {
                var row = new Dictionary<string, object?> { [rowColumn.Name] = group.First().GetValueOrDefault(rowColumn.Name) };
                for (int i = 0; i < columnKeys.Count; i++)
                {
                    var cells = group.Where(r => Key(r.GetValueOrDefault(pivotColumn.Name)) == columnKeys[i]).Select(r => r.GetValueOrDefault(valueColumn.Name)).ToList();
                    var numbers = cells.Select(Number).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                    object? cell = fn switch
                    {
                        "count" => cells.Count,
                        "first" => cells.FirstOrDefault(),
                        "avg" => numbers.Count > 0 ? numbers.Average() : null,
                        "min" => numbers.Count > 0 ? numbers.Min() : null,
                        "max" => numbers.Count > 0 ? numbers.Max() : null,
                        _ => numbers.Count > 0 ? numbers.Sum() : null
                    };
                    row[newColumns[i + 1].Name] = cell ?? (t.FillZero ? 0 : null);
                }
                output.Add(row);
            }
            result.Columns = newColumns;
            result.Rows = output;
        }

        /// <summary>Adds a calculated column computed over the rows (per partition) in their current order.</summary>
        private static void Window(ReportDataResult result, ReportTransform t, string suffix,
            Action<List<Dictionary<string, object?>>, ReportColumn, ReportTransform> compute)
        {
            var field = Column(result, t.Field, t.Type);
            var partition = OptionalColumn(result, t.PartitionBy, t.Type);
            string alias = string.IsNullOrWhiteSpace(t.Alias) ? $"{field.Name}_{suffix}" : t.Alias!.Trim();
            var target = new ReportTransform { Type = t.Type, Field = field.Name, Alias = alias, Mode = t.Mode, Desc = t.Desc };
            foreach (var rows in Partitions(result.Rows, partition)) compute(rows, field, target);

            string type = t.Type switch
            {
                "rank" => ReportFieldTypes.Integer,
                "percentOfTotal" => ReportFieldTypes.Number,
                "change" when t.Mode == "percent" => ReportFieldTypes.Number,
                _ => field.Type
            };
            string label = t.Type switch
            {
                "runningTotal" => $"Running {field.Label}",
                "percentOfTotal" => $"% of {field.Label}",
                "rank" => $"Rank by {field.Label}",
                _ => t.Mode == "percent" ? $"% change in {field.Label}" : $"Change in {field.Label}"
            };
            AddColumn(result, alias, label, type);
        }

        private static void RunningTotal(List<Dictionary<string, object?>> rows, ReportColumn field, ReportTransform t)
        {
            double total = 0;
            foreach (var r in rows)
            {
                total += Number(r.GetValueOrDefault(field.Name)) ?? 0;
                r[t.Alias!] = total;
            }
        }

        private static void PercentOfTotal(List<Dictionary<string, object?>> rows, ReportColumn field, ReportTransform t)
        {
            double total = rows.Sum(r => Number(r.GetValueOrDefault(field.Name)) ?? 0);
            foreach (var r in rows)
                r[t.Alias!] = total == 0 ? 0 : (Number(r.GetValueOrDefault(field.Name)) ?? 0) * 100.0 / total;
        }

        /// <summary>Dense rank: equal values share a rank.</summary>
        private static void Rank(List<Dictionary<string, object?>> rows, ReportColumn field, ReportTransform t)
        {
            var distinct = rows.Select(r => r.GetValueOrDefault(field.Name)).DistinctBy(Key).ToList();
            distinct.Sort(CompareValues);
            if (t.Desc) distinct.Reverse();
            var ranks = distinct.Select((v, i) => (Key(v), i + 1)).ToDictionary(x => x.Item1, x => x.Item2);
            foreach (var r in rows) r[t.Alias!] = ranks[Key(r.GetValueOrDefault(field.Name))];
        }

        private static void Change(List<Dictionary<string, object?>> rows, ReportColumn field, ReportTransform t)
        {
            double? previous = null;
            foreach (var r in rows)
            {
                double? current = Number(r.GetValueOrDefault(field.Name));
                r[t.Alias!] = previous == null || current == null ? null
                    : t.Mode == "percent" ? (previous == 0 ? null : (current - previous) * 100.0 / Math.Abs(previous.Value))
                    : current - previous;
                previous = current;
            }
        }

        public static void Sort(ReportDataResult result, IReadOnlyList<ReportSort> sort)
        {
            if (sort.Count == 0) return;
            var columns = sort.Select(s => (Column(result, s.Field, "sort"), s.Desc)).ToList();
            var rows = result.Rows.ToList();
            rows.Sort((a, b) =>
            {
                foreach (var (column, desc) in columns)
                {
                    int c = CompareValues(a.GetValueOrDefault(column.Name), b.GetValueOrDefault(column.Name));
                    if (c != 0) return desc ? -c : c;
                }
                return 0;
            });
            result.Rows = rows;
        }

        // ------------------------------------------------------------------ fill date gaps

        /// <summary>
        /// Adds rows for missing buckets of a date-bucket column (day, month, hour, year, weekday, hour of day), so
        /// charts show empty periods instead of skipping them. Numeric columns of new rows get 0.
        /// </summary>
        private static void FillDates(ReportDataResult result, ReportTransform t, IReadOnlyDictionary<string, JsonElement?> parameters, TimeZoneInfo timeZone, DateTime utcNow)
        {
            var column = Column(result, t.Field, "fill dates");
            string bucket = t.Bucket ?? Detect(result.Rows.Select(r => r.GetValueOrDefault(column.Name)).FirstOrDefault(v => v != null));

            double? days = t.Count;
            if (!string.IsNullOrWhiteSpace(t.Parameter) && parameters.TryGetValue(t.Parameter!, out var p) && !ReportValueConverter.IsMissing(p))
                days = ReportValueConverter.ToDouble(p, t.Parameter!);

            var existing = result.Rows.Select(r => r.GetValueOrDefault(column.Name)).Where(v => v != null).ToList();
            var keys = new List<object>();
            switch (bucket)
            {
                case ReportDateBuckets.Weekday:
                    keys.AddRange(Enum.GetNames<DayOfWeek>());
                    break;
                case ReportDateBuckets.HourOfDay:
                    keys.AddRange(Enumerable.Range(0, 24).Cast<object>());
                    break;
                default:
                    var dates = existing.Select(v => Parse(v, bucket)).Where(d => d.HasValue).Select(d => d!.Value).ToList();
                    DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
                    DateTime? end = days.HasValue ? Truncate(localNow, bucket) : dates.Count > 0 ? dates.Max() : null;
                    DateTime? start = days.HasValue ? Step(Truncate(localNow.AddDays(-Math.Max(0, days.Value - 1)), bucket), bucket, 0) : dates.Count > 0 ? dates.Min() : null;
                    if (start == null || end == null) return;
                    int guard = 0;
                    for (var d = start.Value; d <= end.Value && guard < 5000; d = Step(d, bucket, 1), guard++) keys.Add(Format(d, bucket));
                    break;
            }

            var present = new HashSet<string>(existing.Select(Key));
            var numeric = result.Columns.Where(c => NumericTypes.Contains(c.Type) && c.Name != column.Name).ToList();
            foreach (var key in keys.Where(k => !present.Contains(Key(k))))
            {
                var row = result.Columns.ToDictionary(c => c.Name, c => (object?)null);
                row[column.Name] = key;
                foreach (var c in numeric) row[c.Name] = 0;
                result.Rows.Add(row);
            }

            // Keep rows in bucket order (weekdays Sunday first, as DayOfWeek)
            var order = keys.Select((k, i) => (Key(k), i)).ToDictionary(x => x.Item1, x => x.i);
            result.Rows = result.Rows.OrderBy(r => order.GetValueOrDefault(Key(r.GetValueOrDefault(column.Name)), int.MaxValue)).ToList();
        }

        private static string Detect(object? sample) => Key(sample) switch
        {
            var s when s.Length == 16 => ReportDateBuckets.Hour,        // 2026-10-28 14:00
            var s when s.Length == 10 => ReportDateBuckets.Day,         // 2026-10-28
            var s when s.Length == 7 => ReportDateBuckets.Month,         // 2026-10
            var s when s.Length == 4 => ReportDateBuckets.Year,
            var s when Enum.TryParse<DayOfWeek>(s, out _) => ReportDateBuckets.Weekday,
            _ => ReportDateBuckets.HourOfDay
        };

        private static DateTime? Parse(object? value, string bucket)
        {
            string s = Key(value);
            string format = bucket switch { ReportDateBuckets.Hour => "yyyy-MM-dd HH:mm", ReportDateBuckets.Month => "yyyy-MM", ReportDateBuckets.Year => "yyyy", _ => "yyyy-MM-dd" };
            return DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }

        private static DateTime Truncate(DateTime d, string bucket) => bucket switch
        {
            ReportDateBuckets.Hour => new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0),
            ReportDateBuckets.Month => new DateTime(d.Year, d.Month, 1),
            ReportDateBuckets.Year => new DateTime(d.Year, 1, 1),
            _ => d.Date
        };

        private static DateTime Step(DateTime d, string bucket, int n) => bucket switch
        {
            ReportDateBuckets.Hour => d.AddHours(n),
            ReportDateBuckets.Month => d.AddMonths(n),
            ReportDateBuckets.Year => d.AddYears(n),
            _ => d.AddDays(n)
        };

        private static object Format(DateTime d, string bucket) => bucket switch
        {
            ReportDateBuckets.Hour => d.ToString("yyyy-MM-dd HH:00", CultureInfo.InvariantCulture),
            ReportDateBuckets.Month => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ReportDateBuckets.Year => d.Year,
            _ => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }
}
