using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Reflection;
using System.Text.Json;
using Op = Jellystat_API_Core.Models.Reports.ReportFilterOperators;

namespace Jellystat_API_Core.Services.Reports.Query
{
    public abstract record NormalizedFilter;

    public record NormalizedGroup(bool Or, IReadOnlyList<NormalizedFilter> Children) : NormalizedFilter;

    /// <summary>
    /// A rule reduced to a basic comparison with typed values. Op is one of the comparison operators plus
    /// notStartsWith/notEndsWith; between, relative dates and parameters are already expanded and negation pushed down.
    /// </summary>
    public record NormalizedRule(PropertyInfo Property, string Op, object? Value, IReadOnlyList<object?> Values) : NormalizedFilter
    {
        public Type ValueType => Property.PropertyType;
    }

    /// <summary>
    /// Turns a definition filter tree into a validated NormalizedFilter that both query engines translate,
    /// so EF Core and Gridify always apply exactly the same conditions.
    /// </summary>
    public static class ReportFilterNormalizer
    {
        public const string NotStartsWith = "notStartsWith";
        public const string NotEndsWith = "notEndsWith";

        private static readonly HashSet<string> StringOnly = [Op.Contains, Op.NotContains, Op.StartsWith, Op.EndsWith];
        private static readonly HashSet<string> Ordered = [Op.Gt, Op.Gte, Op.Lt, Op.Lte, Op.Between];
        private static readonly HashSet<string> DateOnly = [Op.LastNDays, Op.LastNHours, Op.Today, Op.ThisMonth, Op.ThisYear];

        public static NormalizedFilter? Normalize(ReportFilterNode? node, IReportDataset dataset,
            IReadOnlyDictionary<string, JsonElement?> parameters, DateTime utcNow)
        {
            if (node == null) return null;
            return node.IsGroup ? NormalizeGroup(node, dataset, parameters, utcNow, negate: false)
                                : NormalizeRule(node, dataset, parameters, utcNow, negate: false);
        }

        private static NormalizedFilter? NormalizeGroup(ReportFilterNode node, IReportDataset dataset,
            IReadOnlyDictionary<string, JsonElement?> parameters, DateTime utcNow, bool negate)
        {
            bool not = negate ^ node.Not;
            bool or = string.Equals(node.Combinator, "or", StringComparison.OrdinalIgnoreCase);
            // De Morgan: NOT (a AND b) == NOT a OR NOT b
            if (not) or = !or;

            var children = new List<NormalizedFilter>();
            foreach (var child in node.Rules!)
            {
                var normalized = child.IsGroup
                    ? NormalizeGroup(child, dataset, parameters, utcNow, not)
                    : NormalizeRule(child, dataset, parameters, utcNow, not);
                if (normalized != null) children.Add(normalized);
            }

            return children.Count switch
            {
                0 => null,
                1 => children[0],
                _ => new NormalizedGroup(or, children)
            };
        }

        private static NormalizedFilter? NormalizeRule(ReportFilterNode rule, IReportDataset dataset,
            IReadOnlyDictionary<string, JsonElement?> parameters, DateTime utcNow, bool negate)
        {
            if (string.IsNullOrWhiteSpace(rule.Field)) return null;

            PropertyInfo prop = dataset.GetProperty(rule.Field)
                ?? throw new ReportDefinitionException($"Unknown field '{rule.Field}' in dataset '{dataset.Key}'");
            string op = rule.Operator ?? Op.Eq;
            if (!Op.All.Contains(op)) throw new ReportDefinitionException($"Unknown filter operator '{op}'");

            Type type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            string field = prop.Name;
            if (StringOnly.Contains(op) && type != typeof(string))
                throw new ReportDefinitionException($"'{op}' only works on text fields ({field})");
            if (DateOnly.Contains(op) && type != typeof(DateTime))
                throw new ReportDefinitionException($"'{op}' only works on date fields ({field})");
            if (Ordered.Contains(op) && (type == typeof(bool) || type.IsEnum))
                throw new ReportDefinitionException($"'{op}' does not work on {field}");
            if (op is Op.IsNull or Op.IsNotNull && prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                throw new ReportDefinitionException($"{field} always has a value, so it can't be checked for empty");

            JsonElement? value = rule.Value;
            JsonElement? valueTo = rule.ValueTo;
            if (!string.IsNullOrWhiteSpace(rule.Parameter))
            {
                // An unset parameter switches the rule off, which is how optional filters work
                if (!parameters.TryGetValue(rule.Parameter!, out var p) || ReportValueConverter.IsMissing(p)) return null;
                value = p;
                if (op == Op.Between) (value, valueTo) = SplitRange(p!.Value);
                // Multi-select lookups pass an array: equals means "is one of"
                else if (p!.Value.ValueKind == JsonValueKind.Array && op is Op.Eq or Op.Neq) op = op == Op.Eq ? Op.In : Op.NotIn;
            }

            NormalizedFilter Simple(string o, object? v = null, IReadOnlyList<object?>? values = null) =>
                new NormalizedRule(prop, negate ? Negate(o) : o, v, values ?? []);

            NormalizedFilter Range(object? from, string fromOp, object? to, string toOp)
            {
                var parts = new List<NormalizedFilter>();
                if (from != null) parts.Add(Simple(fromOp, from));
                if (to != null) parts.Add(Simple(toOp, to));
                if (parts.Count == 1) return parts[0];
                // NOT (x >= a AND x <= b) == x < a OR x > b, which Simple already negated per part
                return new NormalizedGroup(negate, parts);
            }

            object? Value() => ReportValueConverter.Convert(value, prop.PropertyType, field);

            switch (op)
            {
                case Op.IsNull:
                case Op.IsNotNull:
                    return Simple(op);

                case Op.In:
                case Op.NotIn:
                    var values = ReportValueConverter.ConvertList(value, prop.PropertyType, field);
                    if (values.Count == 0) return null;
                    return Simple(op, null, values);

                case Op.Between:
                    object? from = ReportValueConverter.IsMissing(value) ? null : Value();
                    object? to = ReportValueConverter.IsMissing(valueTo) ? null : ReportValueConverter.Convert(valueTo, prop.PropertyType, field);
                    if (from == null && to == null) return null;
                    return Range(from, Op.Gte, to, Op.Lte);

                case Op.LastNDays:
                    return Simple(Op.Gte, utcNow.AddDays(-ReportValueConverter.ToDouble(value, field)));
                case Op.LastNHours:
                    return Simple(Op.Gte, utcNow.AddHours(-ReportValueConverter.ToDouble(value, field)));
                case Op.Today:
                    return Range(utcNow.Date, Op.Gte, utcNow.Date.AddDays(1), Op.Lt);
                case Op.ThisMonth:
                    var month = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return Range(month, Op.Gte, month.AddMonths(1), Op.Lt);
                case Op.ThisYear:
                    var year = new DateTime(utcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    return Range(year, Op.Gte, year.AddYears(1), Op.Lt);

                default:
                    if (ReportValueConverter.IsMissing(value)) return null;
                    return Simple(op, Value());
            }
        }

        private static (JsonElement?, JsonElement?) SplitRange(JsonElement p)
        {
            if (p.ValueKind == JsonValueKind.Array)
            {
                var items = p.EnumerateArray().ToList();
                return (items.ElementAtOrDefault(0), items.ElementAtOrDefault(1));
            }
            if (p.ValueKind == JsonValueKind.Object)
            {
                p.TryGetProperty("from", out var from);
                p.TryGetProperty("to", out var to);
                return (from, to);
            }
            return (p, null);
        }

        private static string Negate(string op) => op switch
        {
            Op.Eq => Op.Neq,
            Op.Neq => Op.Eq,
            Op.Gt => Op.Lte,
            Op.Gte => Op.Lt,
            Op.Lt => Op.Gte,
            Op.Lte => Op.Gt,
            Op.Contains => Op.NotContains,
            Op.NotContains => Op.Contains,
            Op.StartsWith => NotStartsWith,
            NotStartsWith => Op.StartsWith,
            Op.EndsWith => NotEndsWith,
            NotEndsWith => Op.EndsWith,
            Op.IsNull => Op.IsNotNull,
            Op.IsNotNull => Op.IsNull,
            Op.In => Op.NotIn,
            Op.NotIn => Op.In,
            _ => throw new ReportDefinitionException($"Operator '{op}' cannot be negated")
        };
    }
}
