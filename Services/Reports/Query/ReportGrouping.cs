using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Linq.Expressions;
using System.Reflection;
using Fn = Jellystat_API_Core.Models.Reports.ReportAggregateFunctions;

namespace Jellystat_API_Core.Services.Reports.Query
{
    // Typed group keys and result rows. EF Core can only translate GroupBy/OrderBy over real members, so dynamic
    // groupings are built from these fixed shapes instead of anonymous types.
    public class ReportGroupKey<T1> { public T1 K1 { get; set; } = default!; }
    public class ReportGroupKey<T1, T2> { public T1 K1 { get; set; } = default!; public T2 K2 { get; set; } = default!; }
    public class ReportGroupKey<T1, T2, T3> { public T1 K1 { get; set; } = default!; public T2 K2 { get; set; } = default!; public T3 K3 { get; set; } = default!; }
    public class ReportGroupKey<T1, T2, T3, T4> { public T1 K1 { get; set; } = default!; public T2 K2 { get; set; } = default!; public T3 K3 { get; set; } = default!; public T4 K4 { get; set; } = default!; }

    public class ReportAggRow<TKey>
    {
        public TKey Key { get; set; } = default!;
        public double? A0 { get; set; }
        public double? A1 { get; set; }
        public double? A2 { get; set; }
        public double? A3 { get; set; }
        public double? A4 { get; set; }
        public double? A5 { get; set; }
        public double? A6 { get; set; }
        public double? A7 { get; set; }
        public DateTime? D0 { get; set; }
        public DateTime? D1 { get; set; }
        public DateTime? D2 { get; set; }
        public DateTime? D3 { get; set; }
    }

    /// <summary>One output column of a grouped query and where its value lives on ReportAggRow.</summary>
    public record ReportGroupedColumn(string Name, string Label, string Type, string[] Path, string? Bucket);

    public class ReportGroupedQuery
    {
        public required IQueryable Query { get; init; }
        public required Type RowType { get; init; }
        public required List<ReportGroupedColumn> Columns { get; init; }
    }

    public static class ReportGrouping
    {
        public const int MaxGroups = 4;
        public const int MaxNumericAggregates = 8;
        public const int MaxDateAggregates = 4;

        private static readonly HashSet<string> NumericTypes =
            [ReportFieldTypes.Number, ReportFieldTypes.Integer, ReportFieldTypes.Duration, ReportFieldTypes.Bytes];

        public static ReportGroupedQuery Build<TRow>(IQueryable<TRow> query, ReportDataSource source, IReportDataset dataset)
        {
            if (source.GroupBy.Count > MaxGroups)
                throw new ReportDefinitionException($"A data source can group by at most {MaxGroups} fields");

            var x = Expression.Parameter(typeof(TRow), "x");
            var columns = new List<ReportGroupedColumn>();

            // ---- key
            var keyParts = new List<Expression>();
            foreach (var group in source.GroupBy)
            {
                var (prop, info) = Field(dataset, group.Field);
                if (!info.Groupable) throw new ReportDefinitionException($"{info.Label} can't be grouped on");
                Expression key = Expression.Property(x, prop);
                string type = info.Type;
                if (!string.IsNullOrEmpty(group.Bucket))
                {
                    key = DateBucket(key, group.Bucket!, info);
                    type = group.Bucket is ReportDateBuckets.Year or ReportDateBuckets.HourOfDay ? ReportFieldTypes.Integer : ReportFieldTypes.String;
                }
                string label = string.IsNullOrEmpty(group.Bucket) ? info.Label : $"{info.Label} ({group.Bucket})";
                columns.Add(new ReportGroupedColumn(group.OutputName, label, type, ["Key", $"K{keyParts.Count + 1}"], group.Bucket));
                keyParts.Add(key);
            }

            Expression keyBody;
            Type keyType;
            if (keyParts.Count == 0)
            {
                // Whole-table aggregates (totals): one group
                keyBody = Expression.Constant(1);
                keyType = typeof(int);
            }
            else
            {
                Type generic = keyParts.Count switch
                {
                    1 => typeof(ReportGroupKey<>),
                    2 => typeof(ReportGroupKey<,>),
                    3 => typeof(ReportGroupKey<,,>),
                    _ => typeof(ReportGroupKey<,,,>)
                };
                keyType = generic.MakeGenericType(keyParts.Select(k => k.Type).ToArray());
                keyBody = Expression.MemberInit(Expression.New(keyType),
                    keyParts.Select((k, i) => Expression.Bind(keyType.GetProperty($"K{i + 1}")!, k)));
            }

            var groupingType = typeof(IGrouping<,>).MakeGenericType(keyType, typeof(TRow));
            var grouped = Expression.Call(typeof(Queryable), nameof(Queryable.GroupBy), [typeof(TRow), keyType],
                query.Expression, Expression.Quote(Expression.Lambda(keyBody, x)));

            // ---- aggregates
            var rowType = typeof(ReportAggRow<>).MakeGenericType(keyType);
            var g = Expression.Parameter(groupingType, "g");
            var bindings = new List<MemberBinding>();
            if (keyParts.Count > 0) bindings.Add(Expression.Bind(rowType.GetProperty("Key")!, Expression.Property(g, "Key")));

            int numeric = 0, dates = 0;
            var aggregates = source.Aggregates.Count == 0 && keyParts.Count > 0
                ? [new ReportAggregate { Fn = Fn.Count, Alias = "count" }]
                : source.Aggregates;
            foreach (var agg in aggregates)
            {
                var (value, isDate, type, label) = Aggregate(agg, g, dataset);
                string slot;
                if (isDate)
                {
                    if (dates >= MaxDateAggregates) throw new ReportDefinitionException($"At most {MaxDateAggregates} date aggregates are allowed");
                    slot = $"D{dates++}";
                }
                else
                {
                    if (numeric >= MaxNumericAggregates) throw new ReportDefinitionException($"At most {MaxNumericAggregates} numeric aggregates are allowed");
                    slot = $"A{numeric++}";
                }
                bindings.Add(Expression.Bind(rowType.GetProperty(slot)!, value));
                columns.Add(new ReportGroupedColumn(agg.OutputName, label, type, [slot], null));
            }

            if (columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
                throw new ReportDefinitionException($"Data source '{source.Name}' has two columns with the same name; give them different aliases");

            var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [groupingType, rowType],
                grouped, Expression.Quote(Expression.Lambda(Expression.MemberInit(Expression.New(rowType), bindings), g)));
            IQueryable result = query.Provider.CreateQuery(select);

            // ---- order
            var r = Expression.Parameter(rowType, "r");
            var sortKeys = new List<(Expression, bool)>();
            foreach (var sort in source.Sort)
            {
                var column = columns.FirstOrDefault(c => string.Equals(c.Name, sort.Field, StringComparison.OrdinalIgnoreCase))
                    ?? columns.FirstOrDefault(c => c.Path[0] == "Key" && string.Equals(source.GroupBy[int.Parse(c.Path[1][1..]) - 1].Field, sort.Field, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ReportDefinitionException($"Can't sort by '{sort.Field}': it isn't a group or aggregate of '{source.Name}'");
                sortKeys.Add((Path(r, column.Path), sort.Desc));
            }
            if (sortKeys.Count == 0 && keyParts.Count > 0)
                sortKeys.AddRange(columns.Where(c => c.Path[0] == "Key").Select(c => (Path(r, c.Path), false)));

            if (sortKeys.Count > 0)
            {
                var orderBy = typeof(ReportExpressions).GetMethod(nameof(ReportExpressions.OrderBy))!.MakeGenericMethod(rowType);
                result = (IQueryable)orderBy.Invoke(null, [result, sortKeys, r])!;
            }

            return new ReportGroupedQuery { Query = result, RowType = rowType, Columns = columns };
        }

        private static Expression Path(Expression root, string[] path) => path.Aggregate(root, Expression.Property);

        private static (PropertyInfo, ReportFieldInfo) Field(IReportDataset dataset, string? field)
        {
            var prop = string.IsNullOrEmpty(field) ? null : dataset.GetProperty(field);
            if (prop == null) throw new ReportDefinitionException($"Unknown field '{field}' in dataset '{dataset.Key}'");
            return (prop, dataset.Fields.First(f => f.Key == prop.Name));
        }

        /// <summary>
        /// Date buckets as integers (yyyyMMdd, yyyyMM, ...) because DateTime parts translate on every provider while
        /// date truncation does not. ReportQueryExecutor turns them back into readable labels.
        /// </summary>
        private static Expression DateBucket(Expression member, string bucket, ReportFieldInfo info)
        {
            if (info.Type != ReportFieldTypes.Date) throw new ReportDefinitionException($"Date buckets only work on date fields ({info.Label})");
            bool nullable = Nullable.GetUnderlyingType(member.Type) != null;
            Expression d = nullable ? Expression.Property(member, "Value") : member;

            Expression Part(string name) => Expression.Property(d, name);
            Expression Times(Expression e, long factor) => Expression.Multiply(e, Expression.Constant((int)factor));
            Expression day = Expression.Add(Expression.Add(Times(Part("Year"), 10000), Times(Part("Month"), 100)), Part("Day"));

            Expression value = bucket switch
            {
                ReportDateBuckets.Year => Part("Year"),
                ReportDateBuckets.Month => Expression.Add(Times(Part("Year"), 100), Part("Month")),
                ReportDateBuckets.Day => day,
                ReportDateBuckets.Hour => Expression.Add(Expression.Multiply(Expression.Convert(day, typeof(long)), Expression.Constant(100L)), Expression.Convert(Part("Hour"), typeof(long))),
                ReportDateBuckets.Weekday => Expression.Convert(Part("DayOfWeek"), typeof(int)),
                ReportDateBuckets.HourOfDay => Part("Hour"),
                _ => throw new ReportDefinitionException($"Unknown date bucket '{bucket}'")
            };
            // Nullable dates group their empty values together as null
            return nullable ? Expression.Convert(value, typeof(Nullable<>).MakeGenericType(value.Type)) : value;
        }

        private static (Expression Value, bool IsDate, string Type, string Label) Aggregate(ReportAggregate agg, ParameterExpression g, IReportDataset dataset)
        {
            Type rowType = g.Type.GetGenericArguments()[1];
            var x = Expression.Parameter(rowType, "x");

            if (agg.Fn == Fn.Count && string.IsNullOrEmpty(agg.Field))
                return (ToDouble(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [rowType], g)), false, ReportFieldTypes.Integer, "Count");

            if (!Fn.All.Contains(agg.Fn)) throw new ReportDefinitionException($"Unknown aggregate '{agg.Fn}'");
            var (prop, info) = Field(dataset, agg.Field);
            Expression member = Expression.Property(x, prop);

            switch (agg.Fn)
            {
                case Fn.Count:
                case Fn.CountDistinct:
                    // count(field) counts non-empty values, like SQL
                    var values = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [rowType, prop.PropertyType], g, Expression.Lambda(member, x));
                    Expression source = values;
                    if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    {
                        var v = Expression.Parameter(prop.PropertyType, "v");
                        source = Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [prop.PropertyType], values,
                            Expression.Lambda(Expression.NotEqual(v, Expression.Constant(null, prop.PropertyType)), v));
                    }
                    if (agg.Fn == Fn.CountDistinct)
                        source = Expression.Call(typeof(Enumerable), nameof(Enumerable.Distinct), [prop.PropertyType], source);
                    string countLabel = agg.Fn == Fn.Count ? $"Count of {info.Label}" : $"Distinct {info.Label}";
                    return (ToDouble(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [prop.PropertyType], source)), false, ReportFieldTypes.Integer, countLabel);
            }

            if (!info.Aggregatable) throw new ReportDefinitionException($"{info.Label} can't be used with {agg.Fn}");
            string label = $"{char.ToUpperInvariant(agg.Fn[0])}{agg.Fn[1..]} of {info.Label}";

            if (info.Type == ReportFieldTypes.Date)
            {
                if (agg.Fn is not (Fn.Min or Fn.Max)) throw new ReportDefinitionException($"Dates only support min and max ({info.Label})");
                var selector = Expression.Lambda(Expression.Convert(member, typeof(DateTime?)), x);
                var method = typeof(Enumerable).GetMethods().First(m => m.Name == (agg.Fn == Fn.Min ? "Min" : "Max")
                    && m.GetGenericArguments().Length == 2 && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                    .MakeGenericMethod(rowType, typeof(DateTime?));
                return (Expression.Call(method, g, selector), true, ReportFieldTypes.Date, label);
            }

            if (!NumericTypes.Contains(info.Type)) throw new ReportDefinitionException($"{info.Label} is not numeric");
            string name = agg.Fn switch { Fn.Sum => "Sum", Fn.Avg => "Average", Fn.Min => "Min", Fn.Max => "Max", _ => throw new ReportDefinitionException($"Unknown aggregate '{agg.Fn}'") };
            var numericSelector = Expression.Lambda(Expression.Convert(member, typeof(double?)), x);
            var numericMethod = typeof(Enumerable).GetMethods().First(m => m.Name == name && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.IsGenericType
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>)
                && m.GetParameters()[1].ParameterType.GetGenericArguments()[1] == typeof(double?))
                .MakeGenericMethod(rowType);
            // Averages of whole numbers are fractional; everything else keeps the field's type (duration, bytes...)
            string type = agg.Fn == Fn.Avg && info.Type == ReportFieldTypes.Integer ? ReportFieldTypes.Number : info.Type;
            return (Expression.Call(numericMethod, g, numericSelector), false, type, label);
        }

        private static Expression ToDouble(Expression e) => Expression.Convert(e, typeof(double?));
    }
}
