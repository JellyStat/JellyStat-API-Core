using Jellystat_API_Core.Models.Reports;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Services.Reports.Query
{
    /// <summary>
    /// Builds a data source from other data sources' results: a join on matching columns, or a union of rows.
    /// Runs after the inputs' own queries and steps, so it can combine anything they produce (totals, pivots...).
    /// </summary>
    public static partial class ReportDerivation
    {
        public static readonly string[] JoinTypes = ["inner", "left", "full"];

        [GeneratedRegex("[^A-Za-z0-9_]+")]
        private static partial Regex NotIdentifier();

        public static ReportDataResult Combine(ReportDataSource source, IReadOnlyList<(ReportDataSource Source, ReportDataResult Result)> inputs, int maxRows)
        {
            var derive = source.Derive!;
            var result = derive.Mode switch
            {
                ReportDeriveModes.Join => Join(source, derive, inputs, maxRows),
                ReportDeriveModes.Union => Union(source, derive, inputs, maxRows),
                _ => throw new ReportDefinitionException($"Unknown combine mode '{derive.Mode}'")
            };
            result.Truncated |= inputs.Any(i => i.Result.Truncated);
            return result;
        }

        private static ReportColumn Column(ReportDataResult result, string name, string sourceName) =>
            result.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportDefinitionException($"'{sourceName}' has no column '{name}' to join on");

        private static ReportDataResult Join(ReportDataSource source, ReportDerive derive,
            IReadOnlyList<(ReportDataSource Source, ReportDataResult Result)> inputs, int maxRows)
        {
            if (inputs.Count != 2) throw new ReportDefinitionException("A join combines exactly two data sources");
            if (derive.On.Count == 0) throw new ReportDefinitionException("Choose the columns to join on");
            string joinType = (derive.JoinType ?? "left").ToLowerInvariant();
            if (!JoinTypes.Contains(joinType)) throw new ReportDefinitionException($"Unknown join type '{derive.JoinType}'");

            var (leftSource, left) = inputs[0];
            var (rightSource, right) = inputs[1];
            var leftKeys = derive.On.Select(k => Column(left, k.Left, leftSource.Name)).ToList();
            var rightKeys = derive.On.Select(k => Column(right, k.Right, rightSource.Name)).ToList();

            // Output: every left column, then the right columns that aren't join keys; clashing names get a prefix
            string prefix = derive.RightPrefix ?? NotIdentifier().Replace(rightSource.Name, "_").Trim('_') + "_";
            var columns = left.Columns.Select(c => new ReportColumn { Name = c.Name, Label = c.Label, Type = c.Type }).ToList();
            var rightOutput = new List<(string From, string To)>();
            foreach (var c in right.Columns.Where(c => !rightKeys.Contains(c)))
            {
                string name = columns.Any(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)) ? prefix + c.Name : c.Name;
                columns.Add(new ReportColumn { Name = name, Label = name == c.Name ? c.Label : $"{c.Label} ({rightSource.Name})", Type = c.Type });
                rightOutput.Add((c.Name, name));
            }

            string KeyOf(Dictionary<string, object?> row, List<ReportColumn> keys) =>
                string.Join("\u001f", keys.Select(k => ReportTransforms.Key(row.GetValueOrDefault(k.Name))));

            var rightByKey = right.Rows.GroupBy(r => KeyOf(r, rightKeys)).ToDictionary(g => g.Key, g => g.ToList());
            var matchedRight = new HashSet<string>();
            var rows = new List<Dictionary<string, object?>>();
            bool truncated = false;

            void Add(Dictionary<string, object?> row)
            {
                if (rows.Count >= maxRows) { truncated = true; return; }
                rows.Add(row);
            }

            foreach (var l in left.Rows)
            {
                string key = KeyOf(l, leftKeys);
                if (rightByKey.TryGetValue(key, out var matches))
                {
                    matchedRight.Add(key);
                    foreach (var r in matches)
                    {
                        var row = new Dictionary<string, object?>(l);
                        foreach (var (from, to) in rightOutput) row[to] = r.GetValueOrDefault(from);
                        Add(row);
                    }
                }
                else if (joinType is "left" or "full")
                {
                    var row = new Dictionary<string, object?>(l);
                    foreach (var (_, to) in rightOutput) row[to] = null;
                    Add(row);
                }
            }

            if (joinType == "full")
            {
                foreach (var (key, matches) in rightByKey.Where(kv => !matchedRight.Contains(kv.Key)))
                {
                    foreach (var r in matches)
                    {
                        var row = columns.ToDictionary(c => c.Name, c => (object?)null);
                        // The left key columns carry the right side's key values, so unmatched rows still say what they are
                        for (int i = 0; i < leftKeys.Count; i++) row[leftKeys[i].Name] = r.GetValueOrDefault(rightKeys[i].Name);
                        foreach (var (from, to) in rightOutput) row[to] = r.GetValueOrDefault(from);
                        Add(row);
                    }
                }
            }

            return new ReportDataResult { DataSourceId = source.Id, Columns = columns, Rows = rows, Truncated = truncated };
        }

        private static ReportDataResult Union(ReportDataSource source, ReportDerive derive,
            IReadOnlyList<(ReportDataSource Source, ReportDataResult Result)> inputs, int maxRows)
        {
            if (inputs.Count == 0) throw new ReportDefinitionException("Choose the data sources to combine");
            var columns = new List<ReportColumn>();
            string? sourceColumn = string.IsNullOrWhiteSpace(derive.SourceColumn) ? null : derive.SourceColumn!.Trim();
            if (sourceColumn != null) columns.Add(new ReportColumn { Name = sourceColumn, Label = sourceColumn, Type = "string" });
            foreach (var (_, r) in inputs)
                foreach (var c in r.Columns.Where(c => !columns.Any(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))))
                    columns.Add(new ReportColumn { Name = c.Name, Label = c.Label, Type = c.Type });

            var rows = new List<Dictionary<string, object?>>();
            bool truncated = false;
            foreach (var (s, r) in inputs)
            {
                foreach (var input in r.Rows)
                {
                    if (rows.Count >= maxRows) { truncated = true; break; }
                    var row = columns.ToDictionary(c => c.Name, c => input.FirstOrDefault(kv => kv.Key.Equals(c.Name, StringComparison.OrdinalIgnoreCase)).Value);
                    if (sourceColumn != null) row[sourceColumn] = s.Name;
                    rows.Add(row);
                }
            }
            return new ReportDataResult { DataSourceId = source.Id, Columns = columns, Rows = rows, Truncated = truncated };
        }
    }
}
