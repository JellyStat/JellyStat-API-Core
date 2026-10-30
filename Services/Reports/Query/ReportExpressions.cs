using Jellystat_API_Core.Models.Reports;
using System.Linq.Expressions;
using System.Reflection;
using Op = Jellystat_API_Core.Models.Reports.ReportFilterOperators;

namespace Jellystat_API_Core.Services.Reports.Query
{
    public record ReportSortKey(ReportFieldRef Property, bool Desc);

    /// <summary>Expression-tree helpers shared by the query executor and the grouping builder.</summary>
    public static class ReportExpressions
    {
        private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        private static readonly MethodInfo StringEndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        private static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

        public static IQueryable<T> Where<T>(IQueryable<T> query, NormalizedFilter? filter)
        {
            if (filter == null) return query;

            var x = Expression.Parameter(typeof(T), "x");
            return query.Where(Expression.Lambda<Func<T, bool>>(Filter(filter, x), x));
        }

        public static Expression Filter(NormalizedFilter filter, ParameterExpression x)
        {
            if (filter is NormalizedGroup g)
            {
                var parts = g.Children.Select(c => Filter(c, x)).ToList();
                return parts.Aggregate((a, b) => g.Or ? Expression.OrElse(a, b) : Expression.AndAlso(a, b));
            }

            var r = (NormalizedRule)filter;
            Expression member = r.Property.Access(x);
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
