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
            ReportElementTypes.Image, ReportElementTypes.Shape, ReportElementTypes.Line
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

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in d.DataSources)
            {
                if (string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
                    throw new ReportDefinitionException("Every data source needs a unique id");
                if (_datasets.Get(source.Dataset) == null)
                    throw new ReportDefinitionException($"Data source '{source.Name}' uses unknown dataset '{source.Dataset}'");
                if (source.Engine is not (ReportQueryEngines.EfCore or ReportQueryEngines.Gridify))
                    throw new ReportDefinitionException($"Data source '{source.Name}' uses unknown engine '{source.Engine}'");
                if (source.Limit is < 1 or > ReportQueryExecutor.MaxLimit)
                    throw new ReportDefinitionException($"Row limit of '{source.Name}' must be 1-{ReportQueryExecutor.MaxLimit}");
            }

            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in d.Parameters)
            {
                if (!ParameterName().IsMatch(p.Name ?? ""))
                    throw new ReportDefinitionException($"Parameter name '{p.Name}' must start with a letter and use only letters, digits and _");
                if (!parameterNames.Add(p.Name!)) throw new ReportDefinitionException($"Parameter '{p.Name}' is defined twice");
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

            // One DbContext can't run queries in parallel, so data sources run one after another
            foreach (var source in definition.DataSources)
            {
                model.Data[source.Id] = await _executor.ExecuteAsync(source, scope, resolved, maxRows, cancellationToken);
            }
            return model;
        }
    }
}
