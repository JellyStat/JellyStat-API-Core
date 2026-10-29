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
            var utcNow = DateTime.UtcNow;
            var filter = ReportFilterNormalizer.Normalize(source.Filter, dataset, parameters, utcNow, timeZone);
            query = engine.ApplyFilter(query, filter, source, dataset);

            var result = new ReportDataResult { DataSourceId = source.Id };

            if (source.GroupBy.Count > 0 || source.Aggregates.Count > 0)
            {
                var grouped = ReportGrouping.Build(query, source, dataset, timeZone);
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
                ? dataset.Fields.Select(f => f.Key).ToList()
                : source.Fields.Select(f => dataset.GetProperty(f)?.Name ?? throw new ReportDefinitionException($"Unknown field '{f}' in dataset '{dataset.Key}'")).Distinct().ToList();

            var sort = source.Sort.Select(s => new ReportSortKey(
                dataset.GetProperty(s.Field) ?? throw new ReportDefinitionException($"Can't sort by unknown field '{s.Field}'"), s.Desc)).ToList();
            query = engine.ApplySort(query, sort, dataset);

            // Only the selected columns are read, so expensive fields (play counts) aren't computed unless asked for
            var x = Expression.Parameter(typeof(TRow), "x");
            var props = fields.Select(f => dataset.GetProperty(f)!).ToList();
            var projection = Expression.Lambda<Func<TRow, TRow>>(
                Expression.MemberInit(Expression.New(typeof(TRow)), props.Select(p => Expression.Bind(p, Expression.Property(x, p)))), x);

            var list = await query.Select(projection).Take(limit + 1).ToListAsync(cancellationToken);
            result.Truncated = list.Count > limit;
            result.Columns = props.Select(p =>
            {
                var info = dataset.Fields.First(f => f.Key == p.Name);
                return new ReportColumn { Name = p.Name, Label = info.Label, Type = info.Type };
            }).ToList();
            foreach (var row in list.Take(limit))
            {
                var dict = new Dictionary<string, object?>(props.Count);
                foreach (var p in props) dict[p.Name] = Normalize(p.GetValue(row));
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
