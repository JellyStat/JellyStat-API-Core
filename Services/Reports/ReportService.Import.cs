using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Rendering;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports
{
    public partial class ReportService
    {
        public const string ReportFileFormat = "jellystat-report";
        public const int ReportFileVersion = 1;
        public const int MaxImportCharacters = 20 * 1024 * 1024;
        private const int MaxReportNameLength = 200;

        /// <summary>
        /// Checks an uploaded report file without saving anything: that it's readable, is a report, is complete and that
        /// every data source really runs here (one row each). Errors make it unusable; warnings are for the user to judge.
        /// </summary>
        public async Task<ReportImportResult> CheckImportAsync(string? content, ReportScope scope, CancellationToken cancellationToken = default)
        {
            var result = new ReportImportResult();
            try
            {
                var definition = ReadFile(content, result);
                if (definition != null && CheckStructure(definition, result))
                {
                    Validate(definition);
                    CheckReferences(definition, result);
                    // A report saved elsewhere can name fields this version doesn't have: only running it shows that
                    var model = await BuildAsync(definition, result.Name ?? "Import", null, scope, null, 1, cancellationToken);
                    CheckData(definition, model, result);
                    if (result.Errors.Count == 0) result.Definition = definition;
                }
            }
            catch (ReportDefinitionException ex)
            {
                result.Errors.Add(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Errors.Add($"The report couldn't be read: {ex.GetBaseException().Message}");
            }
            result.Valid = result.Errors.Count == 0;
            return result;
        }

        private static ReportDefinition? ReadFile(string? content, ReportImportResult result)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                result.Errors.Add("The file is empty.");
                return null;
            }
            if (content.Length > MaxImportCharacters)
            {
                result.Errors.Add($"The file is larger than {MaxImportCharacters / 1024 / 1024} MB.");
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"The file is damaged or incomplete: it isn't valid JSON (line {ex.LineNumber + 1}).");
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    result.Errors.Add("This isn't a Jellystat report file.");
                    return null;
                }

                if (Property(root, "format") is { } format && (format.ValueKind != JsonValueKind.String || format.GetString() != ReportFileFormat))
                {
                    result.Errors.Add("This isn't a Jellystat report file.");
                    return null;
                }
                if (Property(root, "formatVersion") is { ValueKind: JsonValueKind.Number } version && version.GetDouble() > ReportFileVersion)
                {
                    result.Errors.Add("This file was exported by a newer version of Jellystat.");
                    return null;
                }

                // Current exports wrap the definition; a bare definition is accepted, but only if it looks like one
                JsonElement body = Property(root, "definition") ?? root;
                if (body.ValueKind != JsonValueKind.Object || Property(body, "pages") == null)
                {
                    result.Errors.Add("This isn't a Jellystat report file: it has no report pages.");
                    return null;
                }

                string? name = Text(root, "name")?.Trim();
                if (name is { Length: > MaxReportNameLength })
                {
                    name = name[..MaxReportNameLength];
                    result.Warnings.Add($"The report name was shortened to {MaxReportNameLength} characters.");
                }
                result.Name = string.IsNullOrEmpty(name) ? null : name;
                result.Description = Text(root, "description");

                try
                {
                    return body.Deserialize<ReportDefinition>(ReportJson.Options);
                }
                catch (JsonException ex)
                {
                    result.Errors.Add($"The file is damaged: the value at {ex.Path} isn't valid.");
                    return null;
                }
            }
        }

        /// <summary>JSON nulls bypass the model's defaults, and the rest of the code relies on those lists existing.</summary>
        private static bool CheckStructure(ReportDefinition d, ReportImportResult result)
        {
            int before = result.Errors.Count;
            void Missing(string what) => result.Errors.Add($"The file is damaged: {what} is missing.");

            if (d.Version > 1) result.Errors.Add("This report was made with a newer version of Jellystat.");
            if (d.Page == null) Missing("the page setup");
            if (d.Pages == null) Missing("the page list");
            if (d.DataSources == null) Missing("the data source list");
            if (d.Parameters == null) Missing("the parameter list");
            if (d.Parameters?.Any(p => p == null) == true) Missing("a parameter");

            foreach (var source in d.DataSources ?? [])
            {
                if (source == null) { Missing("a data source"); continue; }
                if (source.Fields == null || source.GroupBy == null || source.Aggregates == null || source.Sort == null)
                    Missing($"part of data source '{source.Name}'");
            }

            var elementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in d.Pages ?? [])
            {
                if (page?.Elements == null) { Missing("a page's contents"); continue; }
                foreach (var e in page.Elements)
                {
                    if (e == null) { Missing("an element"); continue; }
                    if (e.Style == null) Missing($"the style of element '{e.Name ?? e.Id}'");
                    if (string.IsNullOrWhiteSpace(e.Id) || !elementIds.Add(e.Id))
                        result.Errors.Add($"The file is damaged: element '{e.Name ?? e.Type}' doesn't have a unique id.");
                }
            }
            return result.Errors.Count == before;
        }

        private static void CheckReferences(ReportDefinition d, ReportImportResult result)
        {
            var sources = d.DataSources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var e in d.Pages.SelectMany(p => p.Elements))
            {
                if (!string.IsNullOrEmpty(e.Media?.DataSourceId) && !sources.Contains(e.Media.DataSourceId))
                    result.Errors.Add($"Image '{e.Name ?? e.Id}' uses a data source that doesn't exist.");
            }
        }

        private static void CheckData(ReportDefinition d, ReportRenderModel model, ReportImportResult result)
        {
            foreach (var source in d.DataSources)
            {
                if (model.Data.GetValueOrDefault(source.Id)?.Error is not { } error) continue;
                // Still usable: it reports this itself, and the data source editor shows the filter to rewrite
                if (error.Contains("raw Gridify")) result.Warnings.Add(error);
                else result.Errors.Add($"Data source '{source.Name}': {error}");
            }

            bool Has(ReportDataResult? data, string? column) =>
                data == null || data.Error != null || data.Columns.Any(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

            foreach (var p in d.Parameters.Where(p => p.Lookup != null))
            {
                var data = model.Data.GetValueOrDefault(p.Lookup!.DataSourceId);
                if (!Has(data, p.Lookup.ValueField))
                    result.Errors.Add($"Lookup parameter '{p.Name}' uses column '{p.Lookup.ValueField}', which its data source doesn't return.");
            }

            // A pivot's columns come from the data, so one row can't show them all
            if (d.DataSources.Any(s => s.Transforms?.Any(t => t.Type == "pivot") == true)) return;

            foreach (var e in d.Pages.SelectMany(p => p.Elements))
            {
                if (string.IsNullOrEmpty(e.DataSourceId)) continue;
                var data = model.Data.GetValueOrDefault(e.DataSourceId);
                foreach (var column in BoundColumns(e).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Has(data, column))
                        result.Warnings.Add($"'{e.Name ?? e.Type}' uses column '{column}', which its data source doesn't return, so it will show empty.");
                }
            }
        }

        private static IEnumerable<string?> BoundColumns(ReportElement e)
        {
            yield return e.ValueField;
            foreach (var c in e.Columns ?? []) yield return c?.Field;
            yield return e.Chart?.CategoryField;
            foreach (var s in e.Chart?.Series ?? []) yield return s?.Field;
            yield return e.Cards?.ImageField;
        }

        private static JsonElement? Property(JsonElement element, string name)
        {
            foreach (var p in element.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null) return p.Value;
            return null;
        }

        private static string? Text(JsonElement element, string name) =>
            Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }
}
