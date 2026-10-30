using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Query;
using Jellystat_API_Core.Services.Reports.Rendering;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Reports
{
    public interface IReportService
    {
        ReportDefinition Parse(string json);
        string Serialize(ReportDefinition definition);
        /// <summary>Throws ReportDefinitionException with a user-facing message when the definition can't be saved or run.</summary>
        void Validate(ReportDefinition definition);

        /// <summary>Runs every data source of the report for the scope and returns what renderers need.</summary>
        Task<ReportRenderModel> BuildAsync(ReportDefinition definition, string name, string? reportId, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? parameters, int? maxRows = null, CancellationToken cancellationToken = default);

        Dictionary<string, JsonElement?> ResolveParameters(ReportDefinition definition, IReadOnlyDictionary<string, JsonElement>? values);

        /// <summary>The choices of a lookup parameter: distinct values of its data source column (at most 1000).</summary>
        Task<List<ReportParameterOption>> GetParameterOptionsAsync(ReportDefinition definition, string parameter, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken = default);

        /// <summary>Runs one data source of a definition, including any sources a combined source is built from.</summary>
        Task<ReportDataResult> ExecuteSourceAsync(ReportDefinition definition, ReportDataSource source, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? values, int? maxRows = null, CancellationToken cancellationToken = default);
    }

    public partial class ReportService : IReportService
    {
        public const int MaxPages = 50;
        public const int MaxElementsPerPage = 500;
        public const int MaxDataSources = 30;
        public const double MaxPageSize = 5000;
        public const int MaxImageBytes = 5 * 1024 * 1024;

        private static readonly HashSet<string> ElementTypes =
        [
            ReportElementTypes.Text, ReportElementTypes.Metric, ReportElementTypes.Table, ReportElementTypes.Chart,
            ReportElementTypes.Image, ReportElementTypes.Shape, ReportElementTypes.Line, ReportElementTypes.Cards
        ];

        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,49}$")]
        private static partial Regex ParameterName();

        private readonly IReportQueryExecutor _executor;
        private readonly IReportDatasetRegistry _datasets;

        public ReportService(IReportQueryExecutor executor, IReportDatasetRegistry datasets)
        {
            _executor = executor;
            _datasets = datasets;
        }

        public ReportDefinition Parse(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ReportDefinition>(json, ReportJson.Options) ?? new ReportDefinition();
            }
            catch (JsonException ex)
            {
                throw new ReportDefinitionException($"The report definition is not valid JSON: {ex.Message}");
            }
        }

        public string Serialize(ReportDefinition definition) => JsonSerializer.Serialize(definition, ReportJson.Options);

        public void Validate(ReportDefinition d)
        {
            if (d.Page.Width < 50 || d.Page.Height < 50 || d.Page.Width > MaxPageSize || d.Page.Height > MaxPageSize)
                throw new ReportDefinitionException($"Page size must be between 50 and {MaxPageSize} px");
            if (d.Pages.Count == 0) throw new ReportDefinitionException("A report needs at least one page");
            if (d.Pages.Count > MaxPages) throw new ReportDefinitionException($"A report can have at most {MaxPages} pages");
            if (d.DataSources.Count > MaxDataSources) throw new ReportDefinitionException($"A report can have at most {MaxDataSources} data sources");

            if (!string.IsNullOrWhiteSpace(d.TimeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(d.TimeZone, out _))
                throw new ReportDefinitionException($"Unknown time zone '{d.TimeZone}'");

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in d.DataSources)
            {
                if (string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
                    throw new ReportDefinitionException("Every data source needs a unique id");
                if (source.Derive != null)
                {
                    if (!ReportDeriveModes.All.Contains(source.Derive.Mode))
                        throw new ReportDefinitionException($"Data source '{source.Name}' uses unknown combine mode '{source.Derive.Mode}'");
                    if (source.Derive.Sources.Count == 0)
                        throw new ReportDefinitionException($"Choose the data sources '{source.Name}' combines");
                    if (source.Derive.Mode == ReportDeriveModes.Join && (source.Derive.Sources.Count != 2 || source.Derive.On.Count == 0 || !ReportDerivation.JoinTypes.Contains(source.Derive.JoinType)))
                        throw new ReportDefinitionException($"The join in '{source.Name}' needs two data sources, a join type and at least one column pair");
                }
                else
                {
                    var dataset = _datasets.Get(source.Dataset)
                        ?? throw new ReportDefinitionException($"Data source '{source.Name}' uses unknown dataset '{source.Dataset}'");
                    // Parses (and safety-checks) the calculated fields
                    _ = new ReportFieldSet(dataset, source.Calculated);
                }
                if (source.Limit < 1 || source.Limit > ReportQueryExecutor.MaxLimit)
                    throw new ReportDefinitionException($"Row limit of '{source.Name}' must be 1-{ReportQueryExecutor.MaxLimit}");
                foreach (var t in source.Transforms ?? [])
                    if (!ReportTransforms.Types.Contains(t.Type))
                        throw new ReportDefinitionException($"Data source '{source.Name}' has an unknown step '{t.Type}'");
            }

            // Combined sources must point at existing sources, without loops
            var byId = d.DataSources.ToDictionary(x => x.Id);
            foreach (var source in d.DataSources.Where(x => x.Derive != null))
            {
                foreach (var input in source.Derive!.Sources)
                    if (!byId.ContainsKey(input) || input == source.Id)
                        throw new ReportDefinitionException($"'{source.Name}' combines a data source that doesn't exist");
                var path = new HashSet<string>();
                bool Loops(string id)
                {
                    if (!path.Add(id)) return true;
                    bool loops = byId[id].Derive?.Sources.Any(Loops) ?? false;
                    path.Remove(id);
                    return loops;
                }
                if (Loops(source.Id)) throw new ReportDefinitionException($"'{source.Name}' is built from itself (directly or through other sources)");
            }

            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in d.Parameters)
            {
                if (!ParameterName().IsMatch(p.Name ?? ""))
                    throw new ReportDefinitionException($"Parameter name '{p.Name}' must start with a letter and use only letters, digits and _");
                if (!parameterNames.Add(p.Name!)) throw new ReportDefinitionException($"Parameter '{p.Name}' is defined twice");
                if (p.Type == "lookup" && (p.Lookup == null || !sourceIds.Contains(p.Lookup.DataSourceId) || string.IsNullOrWhiteSpace(p.Lookup.ValueField)))
                    throw new ReportDefinitionException($"Lookup parameter '{p.Name}' needs a data source and a value column");
            }

            foreach (var page in d.Pages)
            {
                if (page.Elements.Count > MaxElementsPerPage)
                    throw new ReportDefinitionException($"A page can have at most {MaxElementsPerPage} elements");
                foreach (var e in page.Elements)
                {
                    if (!ElementTypes.Contains(e.Type)) throw new ReportDefinitionException($"Unknown element type '{e.Type}'");
                    if (!double.IsFinite(e.X) || !double.IsFinite(e.Y) || e.Width < 0 || e.Height < 0 || e.Width > MaxPageSize * 2 || e.Height > MaxPageSize * 2)
                        throw new ReportDefinitionException($"Element '{e.Name ?? e.Id}' has an invalid position or size");
                    if (e.Src != null && e.Src.Length > MaxImageBytes * 4 / 3 + 100)
                        throw new ReportDefinitionException($"Image '{e.Name ?? e.Id}' is larger than {MaxImageBytes / 1024 / 1024} MB");
                    if (e.DataSourceId != null && !sourceIds.Contains(e.DataSourceId))
                        throw new ReportDefinitionException($"Element '{e.Name ?? e.Id}' uses a data source that doesn't exist");
                }
            }
        }

        public Dictionary<string, JsonElement?> ResolveParameters(ReportDefinition definition, IReadOnlyDictionary<string, JsonElement>? values)
        {
            var resolved = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in definition.Parameters)
            {
                JsonElement? value = p.DefaultValue;
                if (values != null)
                {
                    var key = values.Keys.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                    if (key != null) value = values[key];
                }
                resolved[p.Name] = value;
            }
            return resolved;
        }

        public async Task<List<ReportParameterOption>> GetParameterOptionsAsync(ReportDefinition definition, string parameter, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken = default)
        {
            var p = definition.Parameters.FirstOrDefault(x => x.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase))
                ?? throw new ReportDefinitionException($"Unknown parameter '{parameter}'");
            if (p.Type != "lookup" || p.Lookup == null) throw new ReportDefinitionException($"'{p.Name}' is not a lookup parameter");
            var source = definition.DataSources.FirstOrDefault(d => d.Id == p.Lookup.DataSourceId)
                ?? throw new ReportDefinitionException($"The data source of '{p.Name}' doesn't exist");

            // The lookup's own value never filters its choices, otherwise picking one value would hide all the others
            var resolved = ResolveParameters(definition, values);
            resolved.Remove(p.Name);
            var data = await ExecuteSourceAsync(definition, source, scope, resolved, 1000, [], [], cancellationToken);
            if (data.Error != null) throw new ReportDefinitionException(data.Error);

            var valueColumn = data.Columns.FirstOrDefault(c => c.Name.Equals(p.Lookup.ValueField, StringComparison.OrdinalIgnoreCase))
                ?? throw new ReportDefinitionException($"Column '{p.Lookup.ValueField}' isn't in data source '{source.Name}'");
            var labelColumn = string.IsNullOrWhiteSpace(p.Lookup.LabelField) ? valueColumn
                : data.Columns.FirstOrDefault(c => c.Name.Equals(p.Lookup.LabelField, StringComparison.OrdinalIgnoreCase)) ?? valueColumn;

            var seen = new HashSet<string>();
            var options = new List<ReportParameterOption>();
            foreach (var row in data.Rows)
            {
                object? value = row.GetValueOrDefault(valueColumn.Name);
                if (value == null || !seen.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "")) continue;
                string label = ReportFormatter.Format(row.GetValueOrDefault(labelColumn.Name) ?? value, null, labelColumn.Type, ReportFormatter.Zone(definition.TimeZone));
                options.Add(new ReportParameterOption { Value = value, Label = label.Length == 0 ? "(empty)" : label });
            }
            return options;
        }

        public Task<ReportDataResult> ExecuteSourceAsync(ReportDefinition definition, ReportDataSource source, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? values, int? maxRows = null, CancellationToken cancellationToken = default) =>
            ExecuteSourceAsync(definition, source, scope, ResolveParameters(definition, values), maxRows, [], [], cancellationToken);

        private async Task<ReportDataResult> ExecuteSourceAsync(ReportDefinition definition, ReportDataSource source, ReportScope scope,
            Dictionary<string, JsonElement?> parameters, int? maxRows, Dictionary<string, ReportDataResult> memo, HashSet<string> visiting,
            CancellationToken cancellationToken)
        {
            if (memo.TryGetValue(source.Id, out var done)) return done;
            ReportDataResult result;
            if (source.Derive == null)
            {
                result = await _executor.ExecuteAsync(source, scope, parameters, maxRows, cancellationToken, definition.TimeZone);
            }
            else
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (!visiting.Add(source.Id)) throw new ReportDefinitionException($"'{source.Name}' is built from itself");
                    var inputs = new List<(ReportDataSource, ReportDataResult)>();
                    foreach (var id in source.Derive.Sources)
                    {
                        var input = definition.DataSources.FirstOrDefault(x => x.Id == id)
                            ?? throw new ReportDefinitionException($"'{source.Name}' combines a data source that doesn't exist");
                        var inputResult = await ExecuteSourceAsync(definition, input, scope, parameters, maxRows, memo, visiting, cancellationToken);
                        if (inputResult.Error != null) throw new ReportDefinitionException($"'{input.Name}' failed: {inputResult.Error}");
                        inputs.Add((input, inputResult));
                    }
                    visiting.Remove(source.Id);
                    result = ReportDerivation.Combine(source, inputs, ReportQueryExecutor.MaxLimit);
                    ReportTransforms.Apply(result, source.Transforms ?? [], parameters, ReportFormatter.Zone(definition.TimeZone), DateTime.UtcNow);
                }
                catch (ReportDefinitionException ex)
                {
                    result = new ReportDataResult { DataSourceId = source.Id, Error = ex.Message };
                }
                result.ElapsedMs = sw.ElapsedMilliseconds;
            }
            memo[source.Id] = result;
            return result;
        }

        public async Task<ReportRenderModel> BuildAsync(ReportDefinition definition, string name, string? reportId, ReportScope scope,
            IReadOnlyDictionary<string, JsonElement>? parameters, int? maxRows = null, CancellationToken cancellationToken = default)
        {
            Validate(definition);
            var resolved = ResolveParameters(definition, parameters);
            var model = new ReportRenderModel
            {
                ReportId = reportId,
                Name = name,
                Definition = definition,
                Parameters = resolved.ToDictionary(kv => kv.Key, kv => kv.Value.HasValue ? ReportFormatter.Unwrap(kv.Value.Value) : null, StringComparer.OrdinalIgnoreCase)
            };

            // One DbContext can't run queries in parallel, so data sources run one after another. Results are
            // remembered, so a source that combined sources use only runs once.
            var memo = new Dictionary<string, ReportDataResult>();
            foreach (var source in definition.DataSources)
            {
                model.Data[source.Id] = await ExecuteSourceAsync(definition, source, scope, resolved, maxRows, memo, [], cancellationToken);
            }
            model.Layout = ReportPaginator.Paginate(model);
            return model;
        }
    }
}
