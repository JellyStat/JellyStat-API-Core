using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Reports.Query
{
    /// <summary>A field a query can use: a dataset column, or a calculated field built from them.</summary>
    public sealed class ReportFieldRef
    {
        public required string Name { get; init; }
        public required ReportFieldInfo Info { get; init; }
        /// <summary>row => value, over the dataset's row type.</summary>
        public required LambdaExpression Lambda { get; init; }
        public bool IsCalculated { get; init; }
        public Type Type => Lambda.ReturnType;
        public Type PropertyType => Lambda.ReturnType;

        /// <summary>The value expression for a given row parameter.</summary>
        public Expression Access(ParameterExpression row) => new ReplaceParameter(Lambda.Parameters[0], row).Visit(Lambda.Body);

        private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
        }
    }

    /// <summary>
    /// The fields of one data source: its dataset's [ReportField] properties plus its calculated fields. Everything that
    /// reads a field (filters, grouping, sorting, output) goes through this, so calculated fields work everywhere.
    /// </summary>
    public sealed partial class ReportFieldSet
    {
        private readonly Dictionary<string, ReportFieldRef> _fields = new(StringComparer.OrdinalIgnoreCase);

        public IReportDataset Dataset { get; }
        public IReadOnlyList<ReportFieldRef> All { get; }

        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,49}$")]
        private static partial Regex Identifier();

        public ReportFieldSet(IReportDataset dataset, IReadOnlyList<ReportCalculatedField>? calculated = null)
        {
            Dataset = dataset;
            var all = new List<ReportFieldRef>();
            foreach (var info in dataset.Fields)
            {
                var prop = dataset.GetProperty(info.Key)!;
                var x = Expression.Parameter(dataset.RowType, "x");
                all.Add(new ReportFieldRef { Name = prop.Name, Info = info, Lambda = Expression.Lambda(Expression.Property(x, prop), x) });
            }
            foreach (var r in all) _fields[r.Name] = r;

            foreach (var calc in calculated ?? [])
            {
                string name = (calc.Name ?? "").Trim();
                if (!Identifier().IsMatch(name))
                    throw new ReportDefinitionException($"Calculated field name '{calc.Name}' must start with a letter and use only letters, digits and _");
                if (_fields.ContainsKey(name))
                    throw new ReportDefinitionException($"Calculated field '{name}' has the same name as another field");

                var lambda = ReportExpressionGuard.ParseRow(dataset, calc.Expression, name);
                string type = string.IsNullOrWhiteSpace(calc.Type) || calc.Type == "auto" ? InferType(lambda.ReturnType) : calc.Type!;
                var underlying = Nullable.GetUnderlyingType(lambda.ReturnType) ?? lambda.ReturnType;
                var field = new ReportFieldRef
                {
                    Name = name,
                    IsCalculated = true,
                    Lambda = lambda,
                    Info = new ReportFieldInfo
                    {
                        Key = name,
                        Label = string.IsNullOrWhiteSpace(calc.Label) ? name : calc.Label!,
                        Type = type,
                        Groupable = true,
                        Aggregatable = type is ReportFieldTypes.Number or ReportFieldTypes.Integer or ReportFieldTypes.Duration or ReportFieldTypes.Bytes or ReportFieldTypes.Date,
                        Nullable = !lambda.ReturnType.IsValueType || Nullable.GetUnderlyingType(lambda.ReturnType) != null,
                        Description = calc.Expression,
                        Options = underlying.IsEnum ? Enum.GetNames(underlying).ToList() : null
                    }
                };
                all.Add(field);
                _fields[name] = field;
            }
            All = all;
        }

        public ReportFieldRef? Get(string? name) => string.IsNullOrEmpty(name) ? null : _fields.GetValueOrDefault(name);

        public ReportFieldRef Require(string? name) =>
            Get(name) ?? throw new ReportDefinitionException($"Unknown field '{name}' in dataset '{Dataset.Key}'");

        public static string InferType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string)) return ReportFieldTypes.String;
            if (t == typeof(bool)) return ReportFieldTypes.Boolean;
            if (t == typeof(DateTime)) return ReportFieldTypes.Date;
            if (t.IsEnum) return ReportFieldTypes.Enum;
            if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return ReportFieldTypes.Integer;
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return ReportFieldTypes.Number;
            throw new ReportDefinitionException($"Calculated fields can't return {t.Name}");
        }
    }

    /// <summary>
    /// Parses user expressions (System.Linq.Dynamic.Core syntax) and only accepts trees built from the allowed fields,
    /// operators and a short list of functions. This keeps calculated fields from reaching anything else in the
    /// process, and keeps them translatable to SQL.
    /// </summary>
    public static class ReportExpressionGuard
    {
        public const int MaxLength = 1000;

        private static readonly ParsingConfig Config = new()
        {
            AllowNewToEvaluateAnyType = false,
            DisableMemberAccessToIndexAccessorFallback = true,
            AreContextKeywordsEnabled = false
        };

        private static readonly Dictionary<Type, HashSet<string>> AllowedMethods = new()
        {
            [typeof(string)] = ["Concat", "ToLower", "ToUpper", "Trim", "TrimStart", "TrimEnd", "Substring", "Replace", "Contains", "StartsWith", "EndsWith", "IndexOf", "IsNullOrEmpty", "ToString"],
            [typeof(Math)] = ["Round", "Abs", "Floor", "Ceiling", "Max", "Min", "Pow", "Sqrt", "Truncate", "Sign"],
            [typeof(DateTime)] = ["AddDays", "AddHours", "AddMinutes", "AddSeconds", "AddMonths", "AddYears", "ToString"],
            [typeof(int)] = ["ToString"],
            [typeof(long)] = ["ToString"],
            [typeof(double)] = ["ToString"],
            [typeof(bool)] = ["ToString"],
        };

        private static readonly Dictionary<Type, HashSet<string>> AllowedMembers = new()
        {
            [typeof(string)] = ["Length"],
            [typeof(DateTime)] = ["Year", "Month", "Day", "Hour", "Minute", "Second", "DayOfWeek", "DayOfYear", "Date"],
            [typeof(TimeSpan)] = ["TotalDays", "TotalHours", "TotalMinutes", "TotalSeconds", "Days", "Hours", "Minutes", "Seconds"],
        };

        /// <summary>A row-level calculated field: row => expression over the dataset's fields.</summary>
        public static LambdaExpression ParseRow(IReportDataset dataset, string? expression, string name)
        {
            var row = Expression.Parameter(dataset.RowType, "it");
            var lambda = Parse(name, expression, () => DynamicExpressionParser.ParseLambda(Config, [row], null, Check(expression, name)));
            var allowed = new HashSet<MemberInfo>(dataset.Fields.Select(f => (MemberInfo)dataset.GetProperty(f.Key)!));
            Validate(lambda, name, member => member.Expression == lambda.Parameters[0] && allowed.Contains(member.Member));
            return lambda;
        }

        /// <summary>An expression over named values (e.g. result columns), for post-query calculations.</summary>
        public static LambdaExpression ParseValues(IReadOnlyList<ParameterExpression> parameters, string? expression, string name)
        {
            var lambda = Parse(name, expression, () => DynamicExpressionParser.ParseLambda(Config, parameters.ToArray(), null, Check(expression, name)));
            Validate(lambda, name, _ => false);
            return lambda;
        }

        private static string Check(string? expression, string name)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ReportDefinitionException($"Calculated field '{name}' needs an expression");
            if (expression.Length > MaxLength) throw new ReportDefinitionException($"The expression of '{name}' is too long");
            return expression;
        }

        private static LambdaExpression Parse(string name, string? expression, Func<LambdaExpression> parse)
        {
            try
            {
                return parse();
            }
            catch (ParseException ex)
            {
                throw new ReportDefinitionException($"'{name}': {ex.Message} (at position {ex.Position})");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                throw new ReportDefinitionException($"'{name}': {ex.Message}");
            }
        }

        private static void Validate(LambdaExpression lambda, string name, Func<MemberExpression, bool> isRowField)
        {
            var visitor = new Whitelist(lambda.Parameters, isRowField);
            visitor.Visit(lambda.Body);
            if (visitor.Problem != null) throw new ReportDefinitionException($"'{name}' uses {visitor.Problem}, which isn't allowed in report expressions");
        }

        private sealed class Whitelist(IReadOnlyCollection<ParameterExpression> parameters, Func<MemberExpression, bool> isRowField) : ExpressionVisitor
        {
            public string? Problem { get; private set; }

            private Expression Reject(Expression node, string what)
            {
                Problem ??= what;
                return node;
            }

            public override Expression? Visit(Expression? node)
            {
                if (node == null || Problem != null) return node;
                switch (node.NodeType)
                {
                    case ExpressionType.Parameter:
                        return parameters.Contains(node) ? node : Reject(node, "an unknown value");
                    case ExpressionType.Constant:
                        var value = ((ConstantExpression)node).Value;
                        return value is null or string or bool or char or DateTime or TimeSpan || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal
                            ? node : Reject(node, $"a {value.GetType().Name} value");
                    case ExpressionType.MemberAccess:
                        var m = (MemberExpression)node;
                        if (m.Expression != null && parameters.Contains(m.Expression))
                            return isRowField(m) ? node : Reject(node, $"'{m.Member.Name}'");
                        var owner = Nullable.GetUnderlyingType(m.Member.DeclaringType!) ?? m.Member.DeclaringType!;
                        if (m.Member.DeclaringType!.IsGenericType && m.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>) && m.Member.Name is "Value" or "HasValue")
                            return base.Visit(node);
                        return AllowedMembers.TryGetValue(owner, out var members) && members.Contains(m.Member.Name) && m.Expression != null
                            ? base.Visit(node) : Reject(node, $"'{owner.Name}.{m.Member.Name}'");
                    case ExpressionType.Call:
                        var call = (MethodCallExpression)node;
                        var declaring = call.Method.DeclaringType!;
                        if (declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(Nullable<>) && call.Method.Name == "GetValueOrDefault")
                            return base.Visit(node);
                        return AllowedMethods.TryGetValue(declaring, out var methods) && methods.Contains(call.Method.Name)
                            ? base.Visit(node) : Reject(node, $"'{declaring.Name}.{call.Method.Name}()'");
                    case ExpressionType.Conditional:
                    case ExpressionType.Convert:
                    case ExpressionType.ConvertChecked:
                    case ExpressionType.Not:
                    case ExpressionType.Negate:
                    case ExpressionType.UnaryPlus:
                    case ExpressionType.Add:
                    case ExpressionType.Subtract:
                    case ExpressionType.Multiply:
                    case ExpressionType.Divide:
                    case ExpressionType.Modulo:
                    case ExpressionType.Equal:
                    case ExpressionType.NotEqual:
                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                    case ExpressionType.AndAlso:
                    case ExpressionType.OrElse:
                    case ExpressionType.Coalesce:
                        if (node is UnaryExpression { Method: not null } u && !AllowedMethods.ContainsKey(u.Method.DeclaringType!))
                            return Reject(node, $"'{u.Method.Name}'");
                        return base.Visit(node);
                    default:
                        return Reject(node, $"'{node.NodeType}'");
                }
            }
        }
    }
}
