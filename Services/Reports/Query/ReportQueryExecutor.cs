using Jellystat_API_Core.Data;
using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Query
{
    public interface IReportQueryExecutor
    {
        /// <param name="maxRows">Caps the data source's own limit, e.g. for designer previews.</param>
        Task<ReportDataResult> ExecuteAsync(ReportDataSource source, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement?> parameters, int? maxRows = null, CancellationToken cancellationToken = default, string? timeZone = null);
    }

    public class ReportQueryExecutor : IReportQueryExecutor
    {
        public const int DefaultLimit = 1000;
        /// <summary>Most rows one data source may return; REPORTS_MAX_ROWS overrides it.</summary>
        public static int MaxLimit { get; set; } = 10000;

        private static readonly MethodInfo RunMethod = typeof(ReportQueryExecutor).GetMethod(nameof(RunAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private readonly ApplicationDbContext _db;
        private readonly IReportDatasetRegistry _datasets;
        private readonly Dictionary<string, IReportQueryEngine> _engines;
        private readonly ILogger<ReportQueryExecutor> _logger;

        public ReportQueryExecutor(ApplicationDbContext db, IReportDatasetRegistry datasets, IEnumerable<IReportQueryEngine> engines, ILogger<ReportQueryExecutor> logger)
        {
            _db = db;
            _datasets = datasets;
            _engines = engines.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        public async Task<ReportDataResult> ExecuteAsync(ReportDataSource source, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement?> parameters, int? maxRows = null, CancellationToken cancellationToken = default, string? timeZone = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var dataset = _datasets.Get(source.Dataset) ?? throw new ReportDefinitionException($"Unknown dataset '{source.Dataset}'");
                var engine = _engines.GetValueOrDefault(source.Engine ?? ReportQueryEngines.EfCore)
                    ?? throw new ReportDefinitionException($"Unknown query engine '{source.Engine}'");

                var task = (Task<ReportDataResult>)RunMethod.MakeGenericMethod(dataset.RowType)
                    .Invoke(this, [source, dataset, engine, scope, parameters, maxRows, cancellationToken, ReportFormatter.Zone(timeZone)])!;
                var result = await task;
                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }
            catch (ReportDefinitionException ex)
            {
                return new ReportDataResult { DataSourceId = source.Id, Error = ex.Message, ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("could not be translated"))
            {
                _logger.LogWarning(ex, "Report data source {Name} could not be translated to SQL", source.Name);
                return new ReportDataResult { DataSourceId = source.Id, Error = "This combination of fields can't be run as a database query.", ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Report data source {Name} failed", source.Name);
                return new ReportDataResult { DataSourceId = source.Id, Error = $"Query failed: {ex.GetBaseException().Message}", ElapsedMs = sw.ElapsedMilliseconds };
            }
        }

        private async Task<ReportDataResult> RunAsync<TRow>(ReportDataSource source, IReportDataset dataset, IReportQueryEngine engine,
            ReportScope scope, IReadOnlyDictionary<string, JsonElement?> parameters, int? maxRows, CancellationToken cancellationToken, TimeZoneInfo timeZone) where TRow : class, new()
        {
            int limit = Math.Clamp(source.Limit ?? DefaultLimit, 1, MaxLimit);
            if (maxRows.HasValue) limit = Math.Min(limit, Math.Max(1, maxRows.Value));

            var query = (IQueryable<TRow>)dataset.Query(_db, scope);
            var fieldSet = new ReportFieldSet(dataset, source.Calculated);
            var utcNow = DateTime.UtcNow;
            var filter = ReportFilterNormalizer.Normalize(source.Filter, fieldSet, parameters, utcNow, timeZone);
            query = engine.ApplyFilter(query, filter, source, fieldSet);

            var result = new ReportDataResult { DataSourceId = source.Id };

            if (source.GroupBy.Count > 0 || source.Aggregates.Count > 0)
            {
                var grouped = ReportGrouping.Build(query, source, fieldSet, timeZone);
                var rows = await TakeAsync(grouped.Query, grouped.RowType, limit + 1, cancellationToken);
                result.Truncated = rows.Count > limit;
                result.Columns = grouped.Columns.Select(c => new ReportColumn { Name = c.Name, Label = c.Label, Type = c.Type }).ToList();
                foreach (var row in rows.Take(limit))
                {
                    var dict = new Dictionary<string, object?>(grouped.Columns.Count);
                    foreach (var column in grouped.Columns)
                    {
                        object? value = column.Path.Aggregate((object?)row, (o, p) => o?.GetType().GetProperty(p)!.GetValue(o));
                        dict[column.Name] = column.Bucket != null ? FormatBucket(value, column.Bucket) : Normalize(value);
                    }
                    result.Rows.Add(dict);
                }
                ReportTransforms.Apply(result, source.Transforms ?? [], parameters, timeZone, utcNow);
                return result;
            }

            var fields = source.Fields.Count == 0
                ? fieldSet.All.ToList()
                : source.Fields.Select(fieldSet.Require).DistinctBy(f => f.Name).ToList();

            var sort = source.Sort.Select(s => new ReportSortKey(fieldSet.Require(s.Field), s.Desc)).ToList();
            query = engine.ApplySort(query, sort, fieldSet);

            // Only the selected columns (and calculated fields) are read, as one object[] per row
            var x = Expression.Parameter(typeof(TRow), "x");
            var projection = Expression.Lambda<Func<TRow, object?[]>>(
                Expression.NewArrayInit(typeof(object), fields.Select(f => Expression.Convert(f.Access(x), typeof(object)))), x);

            var list = await query.Select(projection).Take(limit + 1).ToListAsync(cancellationToken);
            result.Truncated = list.Count > limit;
            result.Columns = fields.Select(f => new ReportColumn { Name = f.Name, Label = f.Info.Label, Type = f.Info.Type }).ToList();
            foreach (var row in list.Take(limit))
            {
                var dict = new Dictionary<string, object?>(fields.Count);
                for (int i = 0; i < fields.Count; i++) dict[fields[i].Name] = Normalize(row[i]);
                result.Rows.Add(dict);
            }
            ReportTransforms.Apply(result, source.Transforms ?? [], parameters, timeZone, utcNow);
            return result;
        }

        private static readonly MethodInfo ToListMethod = typeof(ReportQueryExecutor).GetMethod(nameof(ToListAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static Task<List<object>> TakeAsync(IQueryable query, Type rowType, int count, CancellationToken cancellationToken)
        {
            var take = Expression.Call(typeof(Queryable), nameof(Queryable.Take), [rowType], query.Expression, Expression.Constant(count));
            return (Task<List<object>>)ToListMethod.MakeGenericMethod(rowType).Invoke(null, [query.Provider.CreateQuery(take), cancellationToken])!;
        }

        private static async Task<List<object>> ToListAsync<T>(IQueryable query, CancellationToken cancellationToken) =>
            (await ((IQueryable<T>)query).ToListAsync(cancellationToken)).Cast<object>().ToList();

        private static object? Normalize(object? value) => value switch
        {
            Enum e => e.ToString(),
            DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            _ => value
        };

        private static object? FormatBucket(object? value, string bucket)
        {
            if (value == null) return null;
            long v = Convert.ToInt64(value);
            return bucket switch
            {
                ReportDateBuckets.Day => $"{v / 10000:0000}-{v / 100 % 100:00}-{v % 100:00}",
                ReportDateBuckets.Month => $"{v / 100:0000}-{v % 100:00}",
                ReportDateBuckets.Hour => $"{v / 1000000:0000}-{v / 10000 % 100:00}-{v / 100 % 100:00} {v % 100:00}:00",
                ReportDateBuckets.Weekday => ((DayOfWeek)(int)v).ToString(),
                ReportDateBuckets.HourOfDay => (int)v,
                _ => (int)v
            };
        }
    }
}
