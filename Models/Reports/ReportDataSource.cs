using System.Text.Json;

namespace Jellystat_API_Core.Models.Reports
{
    public static class ReportQueryEngines
    {
        /// <summary>Filters and sorting compiled straight to LINQ expression trees.</summary>
        public const string EfCore = "efcore";
        /// <summary>Filters and sorting translated to Gridify syntax and applied through a whitelisted GridifyMapper.</summary>
        public const string Gridify = "gridify";
    }

    /// <summary>
    /// A query against one dataset (see ReportDatasetRegistry). Either a plain row list (Fields) or, when GroupBy or
    /// Aggregates are set, one row per group. Both engines run on the same EF Core IQueryable, so the SQL runs in the database.
    /// </summary>
    public class ReportDataSource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Data source";
        public string Dataset { get; set; } = "activity";
        public string Engine { get; set; } = ReportQueryEngines.EfCore;

        public ReportFilterNode? Filter { get; set; }

        /// <summary>Extra filter in raw Gridify syntax (e.g. "UserName=*bob/i,Duration>60"). Gridify engine only.</summary>
        public string? GridifyFilter { get; set; }

        /// <summary>Columns to return when not grouping. Empty = every field of the dataset.</summary>
        public List<string> Fields { get; set; } = [];
        public List<ReportGroupBy> GroupBy { get; set; } = [];
        public List<ReportAggregate> Aggregates { get; set; } = [];

        /// <summary>Field keys, or group/aggregate aliases when grouping.</summary>
        public List<ReportSort> Sort { get; set; } = [];
        public int? Limit { get; set; }

        /// <summary>Steps applied to the query result, in order (see ReportTransforms).</summary>
        public List<ReportTransform> Transforms { get; set; } = [];

        /// <summary>Extra fields computed from the dataset's fields; usable anywhere a field is (see ReportFieldSet).</summary>
        public List<ReportCalculatedField> Calculated { get; set; } = [];

        /// <summary>When set, the source combines other data sources' results instead of querying a dataset.</summary>
        public ReportDerive? Derive { get; set; }
    }

    /// <summary>A field computed per row, e.g. "Duration / 3600.0" or "iif(CompletionPercentage >= 90, \"Finished\", \"Partial\")".</summary>
    public class ReportCalculatedField
    {
        public string Name { get; set; } = "";
        public string? Label { get; set; }
        public string Expression { get; set; } = "";
        /// <summary>auto (from the expression) or a ReportFieldTypes value such as duration or bytes, for formatting.</summary>
        public string? Type { get; set; }
    }

    public static class ReportDeriveModes
    {
        /// <summary>Rows of Sources[0] matched with rows of Sources[1] on the On columns.</summary>
        public const string Join = "join";
        /// <summary>Rows of every source, one after another.</summary>
        public const string Union = "union";

        public static readonly string[] All = [Join, Union];
    }

    public class ReportDerive
    {
        public string Mode { get; set; } = ReportDeriveModes.Join;
        public List<string> Sources { get; set; } = [];
        /// <summary>join: column pairs that must match.</summary>
        public List<ReportJoinKey> On { get; set; } = [];
        /// <summary>join: inner | left | full</summary>
        public string JoinType { get; set; } = "left";
        /// <summary>join: put in front of right-hand columns whose names are taken (default: the right source's name + "_").</summary>
        public string? RightPrefix { get; set; }
        /// <summary>union: optional column naming the source each row came from.</summary>
        public string? SourceColumn { get; set; }
    }

    public class ReportJoinKey
    {
        public string Left { get; set; } = "";
        public string Right { get; set; } = "";
    }

    /// <summary>
    /// One post-query step. Which properties apply depends on Type:
    /// filter (Field, Operator, Value|Parameter), topN (Field, Count, Desc, PartitionBy, LabelField, OtherLabel),
    /// pivot (RowField, ColumnField, Field, Aggregate, FillZero, SortColumns), runningTotal / percentOfTotal / rank / change
    /// (Field, Alias, PartitionBy, Desc, Mode = difference|percent), fillDates (Field, Bucket, Count|Parameter = last N days),
    /// sort (Sort), limit (Count), calculate (Alias, Expression, Mode = number|string|... result type).
    /// </summary>
    public class ReportTransform
    {
        public string Type { get; set; } = "filter";
        public string? Field { get; set; }
        public string? Operator { get; set; }
        public JsonElement? Value { get; set; }
        public string? Parameter { get; set; }
        public int? Count { get; set; }
        public bool Desc { get; set; } = true;
        public string? PartitionBy { get; set; }
        public string? LabelField { get; set; }
        public string? OtherLabel { get; set; }
        public string? RowField { get; set; }
        public string? ColumnField { get; set; }
        public string? Aggregate { get; set; }
        public bool FillZero { get; set; } = true;
        public bool SortColumns { get; set; } = true;
        public string? Alias { get; set; }
        public string? Mode { get; set; }
        public string? Bucket { get; set; }
        public List<ReportSort>? Sort { get; set; }
        /// <summary>calculate: expression over the result's columns, e.g. "watch / plays".</summary>
        public string? Expression { get; set; }
    }

    public static class ReportDateBuckets
    {
        public const string Hour = "hour";
        public const string Day = "day";
        public const string Weekday = "weekday";
        public const string HourOfDay = "hourOfDay";
        public const string Month = "month";
        public const string Year = "year";

        public static readonly string[] All = [Hour, Day, Weekday, HourOfDay, Month, Year];
    }

    public class ReportGroupBy
    {
        public string Field { get; set; } = "";
        /// <summary>Date fields only: hour | day | weekday | hourOfDay | month | year (UTC).</summary>
        public string? Bucket { get; set; }
        public string? Alias { get; set; }

        public string OutputName => !string.IsNullOrWhiteSpace(Alias) ? Alias! : (string.IsNullOrEmpty(Bucket) ? Field : $"{Field}_{Bucket}");
    }

    public static class ReportAggregateFunctions
    {
        public const string Count = "count";
        public const string CountDistinct = "countDistinct";
        public const string Sum = "sum";
        public const string Avg = "avg";
        public const string Min = "min";
        public const string Max = "max";

        public static readonly string[] All = [Count, CountDistinct, Sum, Avg, Min, Max];
    }

    public class ReportAggregate
    {
        public string Fn { get; set; } = ReportAggregateFunctions.Count;
        /// <summary>Not needed for count.</summary>
        public string? Field { get; set; }
        public string? Alias { get; set; }

        public string OutputName => !string.IsNullOrWhiteSpace(Alias) ? Alias! : (string.IsNullOrEmpty(Field) ? Fn : $"{Fn}_{Field}");
    }

    public class ReportSort
    {
        public string Field { get; set; } = "";
        public bool Desc { get; set; }
    }

    public static class ReportFilterOperators
    {
        public const string Eq = "eq";
        public const string Neq = "neq";
        public const string Gt = "gt";
        public const string Gte = "gte";
        public const string Lt = "lt";
        public const string Lte = "lte";
        public const string Contains = "contains";
        public const string NotContains = "notContains";
        public const string StartsWith = "startsWith";
        public const string EndsWith = "endsWith";
        public const string IsNull = "isNull";
        public const string IsNotNull = "isNotNull";
        public const string In = "in";
        public const string NotIn = "notIn";
        public const string Between = "between";
        /// <summary>Date fields: Value = number of days back from now.</summary>
        public const string LastNDays = "lastNDays";
        public const string LastNHours = "lastNHours";
        public const string Today = "today";
        public const string ThisMonth = "thisMonth";
        public const string ThisYear = "thisYear";

        public static readonly string[] All =
            [Eq, Neq, Gt, Gte, Lt, Lte, Contains, NotContains, StartsWith, EndsWith, IsNull, IsNotNull, In, NotIn, Between, LastNDays, LastNHours, Today, ThisMonth, ThisYear];
    }

    /// <summary>
    /// A filter tree node: a group when Rules is set (Combinator and/or, optionally negated), otherwise a rule.
    /// A rule with Parameter set takes its value from the report parameter of that name at run time.
    /// </summary>
    public class ReportFilterNode
    {
        public string? Combinator { get; set; }
        public bool Not { get; set; }
        public List<ReportFilterNode>? Rules { get; set; }

        public string? Field { get; set; }
        public string? Operator { get; set; }
        public JsonElement? Value { get; set; }
        public JsonElement? ValueTo { get; set; }
        public string? Parameter { get; set; }

        public bool IsGroup => Rules != null;
    }
}
