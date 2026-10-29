using Jellystat_API_Core.Models.Reports;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    /// <summary>
    /// Works out the pages a report draws: each design page, followed by the extra pages its growing tables and card
    /// grids need for rows that don't fit. Row heights are fixed, so this is plain arithmetic and the server and the
    /// browser (which uses model.Layout) always break pages in the same places.
    /// </summary>
    public static class ReportPaginator
    {
        public const int MaxPages = 500;
        public const double DefaultContinueTop = 40;

        public static List<ReportLayoutPage> Paginate(ReportRenderModel model)
        {
            var pages = new List<ReportLayoutPage>();
            for (int p = 0; p < model.Definition.Pages.Count && pages.Count < MaxPages; p++)
            {
                var growing = model.Definition.Pages[p].Elements
                    .Where(e => !e.Hidden && e.Grow && e.Type is ReportElementTypes.Table or ReportElementTypes.Cards)
                    .Select(e => (Element: e, Total: TotalRows(e, model)))
                    .Where(g => g.Total > 0)
                    .ToList();
                var next = growing.ToDictionary(g => g.Element.Id, _ => 0);

                for (int continuation = 0; pages.Count < MaxPages; continuation++)
                {
                    var page = new ReportLayoutPage { Page = p, Continuation = continuation };
                    foreach (var (e, total) in growing)
                    {
                        int start = next[e.Id];
                        if (start >= total) continue;
                        // Extra pages: the element moves up to its continue position and keeps its bottom edge
                        double y = continuation == 0 ? e.Y : Math.Min(e.ContinueTop ?? DefaultContinueTop, e.Y);
                        double height = e.Y + e.Height - y;
                        int fit = Capacity(e, height);
                        if (fit <= 0)
                        {
                            next[e.Id] = total;
                            continue;
                        }
                        int count = Math.Min(fit, total - start);
                        page.Slices[e.Id] = new ReportSlice { Start = start, Count = count, Y = y, Height = height };
                        next[e.Id] = start + count;
                    }
                    pages.Add(page);
                    if (growing.All(g => next[g.Element.Id] >= g.Total)) break;
                }
            }
            return pages;
        }

        /// <summary>Rows the element shows in total (all pages), after its own row limit.</summary>
        public static int TotalRows(ReportElement e, ReportRenderModel model)
        {
            var data = ReportTokenResolver.FindData(model, e.DataSourceId);
            if (data == null || data.Error != null) return 0;
            int limit = e.Type == ReportElementTypes.Cards ? e.Cards?.MaxItems ?? int.MaxValue : e.Table?.MaxRows ?? int.MaxValue;
            return Math.Min(data.Rows.Count, Math.Max(0, limit));
        }

        /// <summary>How many rows fit in an element of this height (same rules the renderers draw with).</summary>
        public static int Capacity(ReportElement e, double height)
        {
            double inner = height - 2 * e.Style.Padding;
            if (e.Type == ReportElementTypes.Cards)
            {
                var o = e.Cards ?? new ReportCardOptions();
                var grid = CardGrid.Of(o, 0);
                return Math.Max(0, (int)Math.Floor((inner + grid.Gap) / (grid.CardHeight + grid.Gap))) * grid.Columns;
            }
            var t = e.Table ?? new ReportTableOptions();
            double rowH = Math.Max(12, t.RowHeight);
            double header = t.ShowHeader ? rowH : 0;
            return Math.Max(0, (int)Math.Floor((inner - header) / rowH));
        }
    }

    /// <summary>Card sizes for a card grid of a given content width.</summary>
    public readonly record struct CardGrid(int Columns, double Gap, double CardWidth, double CardHeight)
    {
        public static CardGrid Of(ReportCardOptions o, double width)
        {
            int columns = Math.Clamp(o.Columns, 1, 24);
            double gap = Math.Max(0, o.Gap);
            return new CardGrid(columns, gap, Math.Max(1, (width - gap * (columns - 1)) / columns), Math.Max(20, o.CardHeight));
        }

        public (double X, double Y) Position(int index, double left, double top) =>
            (left + index % Columns * (CardWidth + Gap), top + index / Columns * (CardHeight + Gap));
    }

    public record ReportConditionStyle(string? Color, string? Background, bool? Bold);

    /// <summary>Conditional formatting: same rules as lib/reports/conditions.ts in the UI.</summary>
    public static class ReportConditions
    {
        public static readonly string[] Operators = ["eq", "neq", "gt", "gte", "lt", "lte", "contains", "notContains", "isNull", "isNotNull"];

        /// <summary>
        /// The style the matching conditions give a row (target null) or one of its columns. Later conditions win
        /// where they set the same property. Null when nothing matches.
        /// </summary>
        public static ReportConditionStyle? Style(IEnumerable<ReportCondition>? conditions, IReadOnlyDictionary<string, object?> row, string? target)
        {
            if (conditions == null) return null;
            string? color = null, background = null;
            bool? bold = null;
            bool any = false;
            foreach (var c in conditions)
            {
                string? t = string.IsNullOrWhiteSpace(c.Target) ? null : c.Target;
                if (!string.Equals(t, target, StringComparison.OrdinalIgnoreCase) || !Matches(c, row)) continue;
                any = true;
                if (!string.IsNullOrWhiteSpace(c.Color)) color = c.Color;
                if (!string.IsNullOrWhiteSpace(c.Background)) background = c.Background;
                if (c.Bold.HasValue) bold = c.Bold;
            }
            return any ? new ReportConditionStyle(color, background, bold) : null;
        }

        public static bool Matches(ReportCondition c, IReadOnlyDictionary<string, object?> row)
        {
            if (string.IsNullOrWhiteSpace(c.Field)) return false;
            object? value = row.FirstOrDefault(kv => kv.Key.Equals(c.Field, StringComparison.OrdinalIgnoreCase)).Value;
            if (value is JsonElement je) value = ReportFormatter.Unwrap(je);
            bool empty = value == null || value is string { Length: 0 };
            switch (c.Operator)
            {
                case "isNull": return empty;
                case "isNotNull": return !empty;
            }

            object? target = c.Value.HasValue ? ReportFormatter.Unwrap(c.Value.Value) : null;
            string Text(object? v) => v switch { null => "", bool b => b ? "true" : "false", IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };

            if (c.Operator is "contains" or "notContains")
            {
                bool contains = Text(value).Contains(Text(target), StringComparison.OrdinalIgnoreCase);
                return c.Operator == "contains" ? contains : !contains;
            }

            int? cmp = null;
            if (value != null && target != null)
            {
                if (value is not string && ReportFormatter.TryDouble(value, out var a) && ReportFormatter.TryDouble(target, out var b)) cmp = a.CompareTo(b);
                else if (value is DateTime && ReportFormatter.TryDate(value, out var da) && ReportFormatter.TryDate(target, out var db)) cmp = da.CompareTo(db);
                else if (value is bool vb) cmp = Text(vb).Equals(Text(target), StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                else cmp = string.Compare(Text(value), Text(target), StringComparison.OrdinalIgnoreCase);
            }
            return c.Operator switch
            {
                "eq" => cmp == 0 || value == null && target == null,
                "neq" => cmp is not null and not 0 || (value == null) != (target == null),
                "gt" => cmp > 0,
                "gte" => cmp >= 0,
                "lt" => cmp < 0,
                "lte" => cmp <= 0,
                _ => false
            };
        }
    }
}
