using Gridify;
using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Op = Jellystat_API_Core.Models.Reports.ReportFilterOperators;

namespace Jellystat_API_Core.Services.Reports.Query
{
    public record ReportSortKey(PropertyInfo Property, bool Desc);

    /// <summary>
    /// Applies a data source's filter and row sort to a dataset query. Aggregation is shared (ReportGrouping) because
    /// Gridify has no grouping; both engines leave the IQueryable for EF Core to translate to SQL.
    /// </summary>
    public interface IReportQueryEngine
    {
        string Key { get; }
        IQueryable<T> ApplyFilter<T>(IQueryable<T> query, NormalizedFilter? filter, ReportDataSource source, IReportDataset dataset);
        IQueryable<T> ApplySort<T>(IQueryable<T> query, IReadOnlyList<ReportSortKey> sort, IReportDataset dataset);
    }

    public class EfCoreReportQueryEngine : IReportQueryEngine
    {
        public string Key => ReportQueryEngines.EfCore;

        public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, NormalizedFilter? filter, ReportDataSource source, IReportDataset dataset)
        {
            if (!string.IsNullOrWhiteSpace(source.GridifyFilter))
                throw new ReportDefinitionException($"Data source '{source.Name}' has a Gridify filter but uses the EF Core engine");
            if (filter == null) return query;

            var x = Expression.Parameter(typeof(T), "x");
            return query.Where(Expression.Lambda<Func<T, bool>>(ReportExpressions.Filter(filter, x), x));
        }

        public IQueryable<T> ApplySort<T>(IQueryable<T> query, IReadOnlyList<ReportSortKey> sort, IReportDataset dataset)
        {
            var x = Expression.Parameter(typeof(T), "x");
            return ReportExpressions.OrderBy(query, sort.Select(s => ((Expression)Expression.Property(x, s.Property), s.Desc)).ToList(), x);
        }
    }

    public class GridifyReportQueryEngine : IReportQueryEngine
    {
        public string Key => ReportQueryEngines.Gridify;

        public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, NormalizedFilter? filter, ReportDataSource source, IReportDataset dataset)
        {
            var parts = new List<string>();
            if (filter != null) parts.Add(ToGridify(filter));
            if (!string.IsNullOrWhiteSpace(source.GridifyFilter)) parts.Add(source.GridifyFilter!.Trim());
            if (parts.Count == 0) return query;

            string gridifyFilter = parts.Count == 1 ? parts[0] : string.Join(",", parts.Select(p => $"({p})"));
            try
            {
                return query.ApplyFiltering(gridifyFilter, CreateMapper<T>(dataset));
            }
            catch (Exception ex) when (ex is GridifyFilteringException or GridifyQueryException or GridifyMapperException)
            {
                throw new ReportDefinitionException($"Invalid Gridify filter in '{source.Name}': {ex.Message}");
            }
        }

        public IQueryable<T> ApplySort<T>(IQueryable<T> query, IReadOnlyList<ReportSortKey> sort, IReportDataset dataset)
        {
            if (sort.Count == 0) return query;
            string orderBy = string.Join(", ", sort.Select(s => s.Desc ? $"{s.Property.Name} desc" : s.Property.Name));
            try
            {
                return query.ApplyOrdering(orderBy, CreateMapper<T>(dataset));
            }
            catch (GridifyOrderingException ex)
            {
                throw new ReportDefinitionException($"Invalid sort: {ex.Message}");
            }
        }

        /// <summary>Only the dataset's [ReportField] properties are mapped, so raw Gridify input can't reach anything else.</summary>
        public static GridifyMapper<T> CreateMapper<T>(IReportDataset dataset)
        {
            var mapper = new GridifyMapper<T>(new GridifyMapperConfiguration { CaseSensitive = false, AllowNullSearch = true, DefaultDateTimeKind = DateTimeKind.Utc });
            foreach (var field in dataset.Fields)
            {
                var x = Expression.Parameter(typeof(T), "x");
                var body = Expression.Convert(Expression.Property(x, field.Key), typeof(object));
                mapper.AddMap(field.Key, Expression.Lambda<Func<T, object?>>(body, x));
            }
            return mapper;
        }

        public static string ToGridify(NormalizedFilter filter)
        {
            switch (filter)
            {
                case NormalizedGroup g:
                    return string.Join(g.Or ? "|" : ",", g.Children.Select(c => c is NormalizedGroup ? $"({ToGridify(c)})" : ToGridify(c)));

                case NormalizedRule r:
                    string f = r.Property.Name;
                    switch (r.Op)
                    {
                        case Op.IsNull: return $"{f}=null";
                        case Op.IsNotNull: return $"{f}!=null";
                        case Op.In: return "(" + string.Join("|", r.Values.Select(v => $"{f}={Value(v)}")) + ")";
                        case Op.NotIn: return "(" + string.Join(",", r.Values.Select(v => $"{f}!={Value(v)}")) + ")";
                    }
                    string op = r.Op switch
                    {
                        Op.Eq => "=",
                        Op.Neq => "!=",
                        Op.Gt => ">",
                        Op.Gte => ">=",
                        Op.Lt => "<",
                        Op.Lte => "<=",
                        Op.Contains => "=*",
                        Op.NotContains => "!*",
                        Op.StartsWith => "^",
                        ReportFilterNormalizer.NotStartsWith => "!^",
                        Op.EndsWith => "$",
                        ReportFilterNormalizer.NotEndsWith => "!$",
                        _ => throw new ReportDefinitionException($"Operator '{r.Op}' is not supported by the Gridify engine")
                    };
                    // Text matching is case-insensitive in both engines; equality stays exact
                    bool caseInsensitive = r.Op is Op.Contains or Op.NotContains or Op.StartsWith or Op.EndsWith
                        or ReportFilterNormalizer.NotStartsWith or ReportFilterNormalizer.NotEndsWith;
                    return $"{f}{op}{Value(r.Value)}{(caseInsensitive ? "/i" : "")}";
            }
            throw new InvalidOperationException("Unknown filter node");
        }

        private static string Value(object? value) => value switch
        {
            null => "null",
            string s => Escape(s),
            DateTime d => d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Enum e => e.ToString(),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => Escape(value.ToString() ?? "")
        };

        // Gridify's special characters in values must be backslash-escaped
        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c is '\\' or '(' or ')' or ',' or '|' or '/') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Expression-tree helpers shared by the EF Core engine and the grouping builder.</summary>
    public static class ReportExpressions
    {
        private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        private static readonly MethodInfo StringEndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        private static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

        public static Expression Filter(NormalizedFilter filter, ParameterExpression x)
        {
            if (filter is NormalizedGroup g)
            {
                var parts = g.Children.Select(c => Filter(c, x)).ToList();
                return parts.Aggregate((a, b) => g.Or ? Expression.OrElse(a, b) : Expression.AndAlso(a, b));
            }

            var r = (NormalizedRule)filter;
            Expression member = Expression.Property(x, r.Property);
            Type type = r.ValueType;
            bool canBeNull = !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
            // Only used when canBeNull: a typed null constant can't exist for non-nullable value types
            Expression nullConst = canBeNull ? Expression.Constant(null, type) : Expression.Default(type);

            switch (r.Op)
            {
                case Op.IsNull: return canBeNull ? Expression.Equal(member, nullConst) : Expression.Constant(false);
                case Op.IsNotNull: return canBeNull ? Expression.NotEqual(member, nullConst) : Expression.Constant(true);
                case Op.In:
                case Op.NotIn:
                    Array values = Array.CreateInstance(type, r.Values.Count);
                    for (int i = 0; i < r.Values.Count; i++) values.SetValue(r.Values[i], i);
                    var contains = typeof(Enumerable).GetMethods()
                        .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                        .MakeGenericMethod(type);
                    Expression inExpr = Expression.Call(contains, Parameter(values, values.GetType()), member);
                    return r.Op == Op.In ? inExpr : Expression.Not(inExpr);
            }

            if (r.Value == null)
            {
                // Comparing to null: only equality makes sense
                return r.Op switch
                {
                    Op.Eq => canBeNull ? Expression.Equal(member, nullConst) : Expression.Constant(false),
                    Op.Neq => canBeNull ? Expression.NotEqual(member, nullConst) : Expression.Constant(true),
                    _ => Expression.Constant(false)
                };
            }

            if (type == typeof(string))
            {
                string s = (string)r.Value;
                Expression notNull = Expression.NotEqual(member, nullConst);
                Expression isNull = Expression.Equal(member, nullConst);
                Expression lowered = Expression.Call(member, ToLower);
                Expression Text(MethodInfo m) => Expression.Call(lowered, m, Parameter(s.ToLowerInvariant(), typeof(string)));
                Expression Compare(Func<Expression, Expression, BinaryExpression> cmp) =>
                    cmp(Expression.Call(StringCompare, member, Parameter(s, typeof(string))), Expression.Constant(0));

                return r.Op switch
                {
                    Op.Eq => Expression.Equal(member, Parameter(s, typeof(string))),
                    Op.Neq => Expression.OrElse(isNull, Expression.NotEqual(member, Parameter(s, typeof(string)))),
                    Op.Contains => Expression.AndAlso(notNull, Text(StringContains)),
                    Op.NotContains => Expression.OrElse(isNull, Expression.Not(Text(StringContains))),
                    Op.StartsWith => Expression.AndAlso(notNull, Text(StringStartsWith)),
                    ReportFilterNormalizer.NotStartsWith => Expression.OrElse(isNull, Expression.Not(Text(StringStartsWith))),
                    Op.EndsWith => Expression.AndAlso(notNull, Text(StringEndsWith)),
                    ReportFilterNormalizer.NotEndsWith => Expression.OrElse(isNull, Expression.Not(Text(StringEndsWith))),
                    Op.Gt => Compare(Expression.GreaterThan),
                    Op.Gte => Compare(Expression.GreaterThanOrEqual),
                    Op.Lt => Compare(Expression.LessThan),
                    Op.Lte => Compare(Expression.LessThanOrEqual),
                    _ => throw new ReportDefinitionException($"Operator '{r.Op}' does not work on text")
                };
            }

            Expression value = Parameter(r.Value, type);
            return r.Op switch
            {
                Op.Eq => Expression.Equal(member, value),
                Op.Neq => canBeNull ? Expression.OrElse(Expression.Equal(member, nullConst), Expression.NotEqual(member, value)) : Expression.NotEqual(member, value),
                Op.Gt => Expression.GreaterThan(member, value),
                Op.Gte => Expression.GreaterThanOrEqual(member, value),
                Op.Lt => Expression.LessThan(member, value),
                Op.Lte => Expression.LessThanOrEqual(member, value),
                _ => throw new ReportDefinitionException($"Operator '{r.Op}' does not work on {r.Property.Name}")
            };
        }

        public static IQueryable<T> OrderBy<T>(IQueryable<T> query, IReadOnlyList<(Expression Key, bool Desc)> keys, ParameterExpression x)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                var (key, desc) = keys[i];
                string method = i == 0 ? (desc ? "OrderByDescending" : "OrderBy") : (desc ? "ThenByDescending" : "ThenBy");
                var lambda = Expression.Lambda(key, x);
                query = (IQueryable<T>)query.Provider.CreateQuery<T>(Expression.Call(typeof(Queryable), method,
                    [typeof(T), key.Type], query.Expression, Expression.Quote(lambda)));
            }
            return query;
        }

        /// <summary>A closure member rather than a constant, so EF Core sends values as SQL parameters.</summary>
        public static Expression Parameter(object? value, Type type)
        {
            var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(type), [value])!;
            return Expression.Property(Expression.Constant(holder), "Value");
        }

        private sealed class ValueHolder<TValue>
        {
            public ValueHolder(TValue value) { Value = value; }
            public TValue Value { get; }
        }
    }
}
