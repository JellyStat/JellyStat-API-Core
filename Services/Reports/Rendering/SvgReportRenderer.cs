using Jellystat_API_Core.Models.Reports;
using System.Globalization;
using System.Security;
using System.Text;
using Kinds = Jellystat_API_Core.Models.Reports.ReportChartKinds;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    public class SvgRenderOptions
    {
        /// <summary>
        /// Write one installed font family instead of the CSS fallback list. Needed for PNG/PDF, where Svg.Skia
        /// doesn't walk fallback lists; browsers (SVG/HTML output) handle the full list themselves.
        /// </summary>
        public bool ResolveFonts { get; set; }
        /// <summary>Images that aren't data: URIs are never fetched by the server; browsers may show them.</summary>
        public bool AllowRemoteImages { get; set; }
        /// <summary>
        /// Collects the page's hyperlinks (areas in page px), so a PDF can add them as link annotations.
        /// PNG/PDF output (ResolveFonts) never gets SVG &lt;a&gt; elements.
        /// </summary>
        public List<ReportLinkArea>? Links { get; set; }
    }

    public record ReportLinkArea(double X, double Y, double Width, double Height, string Url);

    /// <summary>
    /// Draws a report page as SVG: the single server-side rendering that HTML, PNG and PDF exports are made from.
    /// components/Reports/render in the UI draws the same layout with the same rules; change both together.
    /// </summary>
    public class SvgReportRenderer
    {
        public static readonly string[] DefaultPalette =
            ["#6366f1", "#22c55e", "#f59e0b", "#ef4444", "#06b6d4", "#a855f7", "#ec4899", "#84cc16", "#14b8a6", "#f97316"];

        private readonly IReportTextMeasurer _measurer;

        public SvgReportRenderer(IReportTextMeasurer measurer)
        {
            _measurer = measurer;
        }

        /// <summary>Draws one page of model.Layout: a design page, or one of the extra pages a growing element adds.</summary>
        public string RenderPage(ReportRenderModel model, int pageIndex, SvgRenderOptions? options = null)
        {
            options ??= new SvgRenderOptions();
            if (model.Layout.Count == 0) model.Layout = ReportPaginator.Paginate(model);
            var page = model.Definition.Page;
            var layout = model.Layout[pageIndex];
            var reportPage = model.Definition.Pages[layout.Page];
            var ctx = new Ctx(model, pageIndex + 1, model.Layout.Count, options, _measurer);

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{N(page.Width)}\" height=\"{N(page.Height)}\" viewBox=\"0 0 {N(page.Width)} {N(page.Height)}\">");
            sb.Append($"<rect x=\"0\" y=\"0\" width=\"{N(page.Width)}\" height=\"{N(page.Height)}\" fill=\"{Attr(page.Background)}\"/>");
            foreach (var element in reportPage.Elements.Where(e => !e.Hidden))
            {
                var slice = layout.Slices.GetValueOrDefault(element.Id);
                // Extra pages only carry the growing elements still showing rows, and the ones marked to repeat
                if (layout.Continuation > 0 && slice == null && !element.Repeat) continue;
                sb.Append(RenderElement(element, ctx, slice));
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        private sealed class Ctx(ReportRenderModel model, int pageNumber, int pageCount, SvgRenderOptions options, IReportTextMeasurer measurer)
        {
            public ReportRenderModel Model { get; } = model;
            public int PageNumber { get; } = pageNumber;
            public int PageCount { get; } = pageCount;
            public SvgRenderOptions Options { get; } = options;
            public IReportTextMeasurer Measurer { get; } = measurer;
            public TimeZoneInfo Zone { get; } = ReportFormatter.Zone(model.Definition.TimeZone);
            public string Family(ReportElementStyle s) => Options.ResolveFonts ? Measurer.ResolveFamily(s.FontFamily) : s.FontFamily;
            public double Measure(string text, ReportElementStyle s, double? size = null, string? weight = null) =>
                Measurer.Measure(text, s.FontFamily, size ?? s.FontSize, weight ?? s.FontWeight, s.Italic);
        }

        private record struct Box(double X, double Y, double W, double H);

        private string RenderElement(ReportElement e, Ctx ctx, ReportSlice? slice)
        {
            var sb = new StringBuilder();
            // Ids are unique per page: HTML exports put every page in one document
            string clipId = $"clip-{ctx.PageNumber}-{Id(e.Id)}";
            // A growing element on an extra page sits higher and is taller than in the design
            double ey = slice?.Y ?? e.Y, eh = slice?.Height ?? e.Height;
            sb.Append($"<defs><clipPath id=\"{clipId}\"><rect x=\"{N(e.X)}\" y=\"{N(ey)}\" width=\"{N(e.Width)}\" height=\"{N(eh)}\" rx=\"{N(e.Style.BorderRadius)}\"/></clipPath></defs>");
            sb.Append($"<g opacity=\"{N(Math.Clamp(e.Opacity, 0, 1))}\" clip-path=\"url(#{clipId})\">");

            var outer = new Box(e.X, ey, e.Width, eh);
            if (e.Type is not (ReportElementTypes.Shape or ReportElementTypes.Line)) sb.Append(Background(outer, e.Style));
            var content = new Box(e.X + e.Style.Padding, ey + e.Style.Padding, Math.Max(0, e.Width - 2 * e.Style.Padding), Math.Max(0, eh - 2 * e.Style.Padding));

            // Text and images link as a whole element
            string? elementLink = e.Type is ReportElementTypes.Text or ReportElementTypes.Image && !string.IsNullOrWhiteSpace(e.Link)
                ? ReportTokenResolver.ResolveLink(e.Link, null, null, ctx.Model, ctx.PageNumber, ctx.PageCount)
                : null;
            if (elementLink != null) sb.Append(LinkStart(outer, elementLink, ctx));

            switch (e.Type)
            {
                case ReportElementTypes.Text:
                    sb.Append(TextBlock(ReportTokenResolver.Resolve(e.Text, ctx.Model, ctx.PageNumber, ctx.PageCount), content, e.Style, ctx));
                    break;
                case ReportElementTypes.Metric:
                    sb.Append(Metric(e, outer, content, ctx));
                    break;
                case ReportElementTypes.Table:
                    sb.Append(Table(e, content, ctx, slice));
                    break;
                case ReportElementTypes.Cards:
                    sb.Append(Cards(e, content, ctx, slice));
                    break;
                case ReportElementTypes.Chart:
                    sb.Append(Chart(e, content, ctx));
                    break;
                case ReportElementTypes.Image:
                    sb.Append(Image(e, content, ctx));
                    break;
                case ReportElementTypes.Shape:
                    sb.Append(Shape(e));
                    break;
                case ReportElementTypes.Line:
                    sb.Append(Line(e));
                    break;
            }
            if (elementLink != null) sb.Append(LinkEnd(ctx));

            sb.Append("</g>");
            // Border on top of the content and outside the clip, so it isn't cut in half
            if (e.Type is not (ReportElementTypes.Shape or ReportElementTypes.Line)) sb.Append(Border(outer, e.Style));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Opens a hyperlink over an area: an SVG &lt;a&gt; for browsers, and a recorded area for PDF links.</summary>
        private static string LinkStart(Box area, string url, Ctx ctx)
        {
            ctx.Options.Links?.Add(new ReportLinkArea(area.X, area.Y, area.W, area.H, url));
            return ctx.Options.ResolveFonts ? "" : $"<a href=\"{Attr(url)}\" xlink:href=\"{Attr(url)}\" target=\"_blank\" rel=\"noopener noreferrer\">";
        }

        private static string LinkEnd(Ctx ctx) => ctx.Options.ResolveFonts ? "" : "</a>";

        /// <summary>An underline under one line of text drawn by Label.</summary>
        private static string Underline(string text, double x, double cy, string anchor, ReportElementStyle s, Ctx ctx, double size, string? weight, string? color)
        {
            if (text.Length == 0) return "";
            double w = ctx.Measure(text, s, size, weight);
            double startX = anchor switch { "middle" => x - w / 2, "end" => x - w, _ => x };
            double uy = cy + size * 0.35 + size * 0.12;
            return $"<line x1=\"{N(startX)}\" y1=\"{N(uy)}\" x2=\"{N(startX + w)}\" y2=\"{N(uy)}\" stroke=\"{Attr(color ?? s.Color)}\" stroke-width=\"{N(Math.Max(1, size / 14))}\"/>";
        }

        private static string Background(Box b, ReportElementStyle s) =>
            string.IsNullOrEmpty(s.Background) ? "" :
            $"<rect x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" rx=\"{N(s.BorderRadius)}\" fill=\"{Attr(s.Background)}\"/>";

        private static string Border(Box b, ReportElementStyle s)
        {
            if (s.BorderWidth <= 0 || string.IsNullOrEmpty(s.BorderColor)) return "";
            double half = s.BorderWidth / 2;
            return $"<rect x=\"{N(b.X + half)}\" y=\"{N(b.Y + half)}\" width=\"{N(Math.Max(0, b.W - s.BorderWidth))}\" height=\"{N(Math.Max(0, b.H - s.BorderWidth))}\" rx=\"{N(Math.Max(0, s.BorderRadius - half))}\" fill=\"none\" stroke=\"{Attr(s.BorderColor)}\" stroke-width=\"{N(s.BorderWidth)}\"{Dash(s.BorderStyle, s.BorderWidth)}/>";
        }

        private static string Dash(string? style, double width) => style switch
        {
            "dashed" => $" stroke-dasharray=\"{N(width * 4)} {N(width * 3)}\"",
            "dotted" => $" stroke-dasharray=\"{N(width)} {N(width * 2)}\" stroke-linecap=\"round\"",
            _ => ""
        };

        private static string TextAttrs(ReportElementStyle s, Ctx ctx, double? size = null, string? weight = null, string? color = null, double? opacity = null) =>
            $" font-family=\"{Attr(ctx.Family(s))}\" font-size=\"{N(size ?? s.FontSize)}\" font-weight=\"{SkiaTextMeasurer.Weight(weight ?? s.FontWeight)}\"" +
            (s.Italic ? " font-style=\"italic\"" : "") + $" fill=\"{Attr(color ?? s.Color)}\"" + (opacity.HasValue ? $" fill-opacity=\"{N(opacity.Value)}\"" : "");

        /// <summary>One line of text with its baseline centred on cy.</summary>
        private static string Label(string text, double x, double cy, string anchor, ReportElementStyle s, Ctx ctx, double size, string? weight = null, string? color = null, double? opacity = null) =>
            $"<text x=\"{N(x)}\" y=\"{N(cy + size * 0.35)}\" text-anchor=\"{anchor}\"{TextAttrs(s, ctx, size, weight, color, opacity)}>{Xml(text)}</text>";

        private string TextBlock(string text, Box b, ReportElementStyle s, Ctx ctx)
        {
            var lines = Wrap(text, b.W, s, ctx);
            double lineH = s.FontSize * s.LineHeight;
            double total = lines.Count * lineH;
            double top = s.VerticalAlign switch
            {
                "middle" => b.Y + (b.H - total) / 2,
                "bottom" => b.Y + b.H - total,
                _ => b.Y
            };

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                double lineTop = top + i * lineH;
                double baseline = lineTop + (lineH - s.FontSize) / 2 + s.FontSize * 0.8;
                var (x, anchor) = Align(s.TextAlign, b);
                sb.Append($"<text x=\"{N(x)}\" y=\"{N(baseline)}\" text-anchor=\"{anchor}\"{TextAttrs(s, ctx)}>{Xml(lines[i])}</text>");
                if (s.Underline && lines[i].Length > 0)
                {
                    double w = ctx.Measure(lines[i], s);
                    double startX = anchor switch { "middle" => x - w / 2, "end" => x - w, _ => x };
                    double uy = baseline + s.FontSize * 0.12;
                    sb.Append($"<line x1=\"{N(startX)}\" y1=\"{N(uy)}\" x2=\"{N(startX + w)}\" y2=\"{N(uy)}\" stroke=\"{Attr(s.Color)}\" stroke-width=\"{N(Math.Max(1, s.FontSize / 14))}\"/>");
                }
            }
            return sb.ToString();
        }

        private static (double X, string Anchor) Align(string? align, Box b) => align switch
        {
            "center" => (b.X + b.W / 2, "middle"),
            "right" => (b.X + b.W, "end"),
            _ => (b.X, "start")
        };

        /// <summary>Greedy word wrap; words longer than a line are broken by character. Matches CSS overflow-wrap: anywhere closely enough.</summary>
        private static List<string> Wrap(string text, double width, ReportElementStyle s, Ctx ctx)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var words = paragraph.Split(' ');
                string line = "";
                foreach (var word in words)
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (ctx.Measure(candidate, s) <= width || line.Length == 0 && ctx.Measure(word, s) <= width)
                    {
                        line = candidate;
                        continue;
                    }
                    if (line.Length > 0) lines.Add(line);
                    line = "";
                    // Break an over-long word
                    string rest = word;
                    while (rest.Length > 0 && ctx.Measure(rest, s) > width)
                    {
                        int fit = Math.Max(1, FitChars(rest, width, s, ctx));
                        lines.Add(rest[..fit]);
                        rest = rest[fit..];
                    }
                    line = rest;
                }
                lines.Add(line);
            }
            return lines;
        }

        private static int FitChars(string text, double width, ReportElementStyle s, Ctx ctx, double? size = null)
        {
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ctx.Measure(text[..mid], s, size) <= width) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private static string Ellipsis(string text, double width, ReportElementStyle s, Ctx ctx, double size, string? weight = null)
        {
            if (ctx.Measure(text, s, size, weight) <= width) return text;
            const string dots = "…";
            double avail = width - ctx.Measure(dots, s, size, weight);
            if (avail <= 0) return "";
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ctx.Measure(text[..mid], s, size, weight) <= avail) lo = mid; else hi = mid - 1;
            }
            return text[..lo].TrimEnd() + dots;
        }

        private static string Message(string text, Box b, ReportElementStyle s, Ctx ctx, string color = "#9ca3af") =>
            Label(text, b.X + b.W / 2, b.Y + b.H / 2, "middle", s, ctx, Math.Max(10, Math.Min(14, s.FontSize)), "normal", color);

        // ------------------------------------------------------------------ elements

        private string Metric(ReportElement e, Box outer, Box b, Ctx ctx)
        {
            var s = e.Style;
            var data = ReportTokenResolver.FindData(ctx.Model, e.DataSourceId);
            string valueText;
            string? link = null;
            ReportConditionStyle? cond = null;
            if (data?.Error != null) return Message(data.Error, b, s, ctx, "#ef4444");
            var column = data?.Columns.FirstOrDefault(c => c.Name.Equals(e.ValueField ?? "", StringComparison.OrdinalIgnoreCase));
            if (data == null || column == null) valueText = "—";
            else
            {
                var row = data.Rows.Count > 0 ? data.Rows[0] : null;
                object? value = row?.GetValueOrDefault(column.Name);
                valueText = value == null ? "—" : ReportFormatter.Format(value, e.Format, column.Type, ctx.Zone);
                if (e.Format == "link" && value != null)
                    link = ReportTokenResolver.ResolveLink(e.Link, value, row, ctx.Model, ctx.PageNumber, ctx.PageCount);
                if (row != null) cond = ReportConditions.Style(e.Conditions, row, null);
            }
            valueText = (e.Prefix ?? "") + valueText + (e.Suffix ?? "");

            string label = ReportTokenResolver.Resolve(e.Label, ctx.Model, ctx.PageNumber, ctx.PageCount);
            double labelSize = Math.Max(10, s.FontSize * 0.4);
            double labelH = label.Length > 0 ? labelSize * 1.5 : 0;
            double valueH = s.FontSize * 1.15;
            double total = labelH + valueH;
            double top = s.VerticalAlign switch
            {
                "middle" => b.Y + (b.H - total) / 2,
                "bottom" => b.Y + b.H - total,
                _ => b.Y
            };
            var (x, anchor) = Align(s.TextAlign, b);
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(cond?.Background))
                sb.Append($"<rect x=\"{N(outer.X)}\" y=\"{N(outer.Y)}\" width=\"{N(outer.W)}\" height=\"{N(outer.H)}\" rx=\"{N(s.BorderRadius)}\" fill=\"{Attr(cond.Background)}\"/>");
            string? valueColor = cond?.Color;
            string? valueWeight = cond?.Bold switch { true => "bold", false => "normal", _ => null };
            if (label.Length > 0)
                sb.Append(Label(Ellipsis(label, b.W, s, ctx, labelSize, "normal"), x, top + labelH / 2, anchor, s, ctx, labelSize, "normal", null, 0.65));
            string shown = Ellipsis(valueText, b.W, s, ctx, s.FontSize, valueWeight);
            double valueY = top + labelH + valueH / 2;
            if (link != null) sb.Append(LinkStart(b, link, ctx));
            sb.Append(Label(shown, x, valueY, anchor, s, ctx, s.FontSize, valueWeight, valueColor));
            if (link != null) sb.Append(Underline(shown, x, valueY, anchor, s, ctx, s.FontSize, valueWeight, valueColor)).Append(LinkEnd(ctx));
            return sb.ToString();
        }

        private string Table(ReportElement e, Box b, Ctx ctx, ReportSlice? slice)
        {
            var s = e.Style;
            var t = e.Table ?? new ReportTableOptions();
            var data = ReportTokenResolver.FindData(ctx.Model, e.DataSourceId);
            if (data == null) return Message("No data source", b, s, ctx);
            if (data.Error != null) return Message(data.Error, b, s, ctx, "#ef4444");

            var columns = e.Columns is { Count: > 0 }
                ? e.Columns
                : data.Columns.Select(c => new ReportTableColumn { Field = c.Name }).ToList();
            double totalWeight = columns.Sum(c => Math.Max(0.1, c.Width));
            double rowH = Math.Max(12, t.RowHeight);
            double y = b.Y;
            var sb = new StringBuilder();

            double[] widths = columns.Select(c => b.W * Math.Max(0.1, c.Width) / totalWeight).ToArray();
            const double cellPad = 6;

            string Cell(string text, int col, double x, double cy, string? weight, string? color, string? link = null)
            {
                var c = columns[col];
                double w = widths[col] - cellPad * 2;
                text = Ellipsis(text, w, s, ctx, s.FontSize, weight);
                var (tx, anchor) = Align(c.Align, new Box(x + cellPad, 0, w, 0));
                string label = Label(text, tx, cy, anchor, s, ctx, s.FontSize, weight, color);
                if (link == null || text.Length == 0) return label;
                return LinkStart(new Box(x, cy - rowH / 2, widths[col], rowH), link, ctx) + label
                    + Underline(text, tx, cy, anchor, s, ctx, s.FontSize, weight, color) + LinkEnd(ctx);
            }

            if (t.ShowHeader)
            {
                sb.Append($"<rect x=\"{N(b.X)}\" y=\"{N(y)}\" width=\"{N(b.W)}\" height=\"{N(rowH)}\" fill=\"{Attr(t.HeaderBackground)}\"/>");
                double x = b.X;
                for (int i = 0; i < columns.Count; i++)
                {
                    string header = columns[i].Header ?? data.Columns.FirstOrDefault(c => c.Name == columns[i].Field)?.Label ?? columns[i].Field;
                    sb.Append(Cell(header, i, x, y + rowH / 2, "bold", t.HeaderColor));
                    x += widths[i];
                }
                y += rowH;
                sb.Append(HLine(b.X, b.X + b.W, y, t.GridColor));
            }

            // A slice (growing tables) says which rows this page shows; otherwise the rows that fit, from the first
            int fit = (int)Math.Floor((b.Y + b.H - y) / rowH);
            int start = slice?.Start ?? 0;
            int count = slice?.Count ?? Math.Min(data.Rows.Count, Math.Max(0, Math.Min(fit, t.MaxRows ?? int.MaxValue)));
            count = Math.Max(0, Math.Min(count, data.Rows.Count - start));
            if (data.Rows.Count == 0)
                sb.Append(Message("No rows", new Box(b.X, y, b.W, Math.Min(rowH * 2, b.Y + b.H - y)), s, ctx));

            for (int n = 0; n < count; n++)
            {
                int r = start + n;
                var rowStyle = ReportConditions.Style(e.Conditions, data.Rows[r], null);
                string? rowBackground = rowStyle?.Background ?? (r % 2 == 1 ? t.StripeBackground : null);
                if (!string.IsNullOrEmpty(rowBackground))
                    sb.Append($"<rect x=\"{N(b.X)}\" y=\"{N(y)}\" width=\"{N(b.W)}\" height=\"{N(rowH)}\" fill=\"{Attr(rowBackground)}\"/>");
                double x = b.X;
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    var type = data.Columns.FirstOrDefault(c => c.Name == col.Field)?.Type;
                    object? value = data.Rows[r].GetValueOrDefault(col.Field);
                    string text = ReportFormatter.Format(value, col.Format, type, ctx.Zone);
                    string? link = col.Format == "link" && value != null
                        ? ReportTokenResolver.ResolveLink(col.Link, value, data.Rows[r], ctx.Model, ctx.PageNumber, ctx.PageCount)
                        : null;
                    var cellStyle = ReportConditions.Style(e.Conditions, data.Rows[r], col.Field);
                    if (!string.IsNullOrEmpty(cellStyle?.Background))
                        sb.Append($"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(widths[i])}\" height=\"{N(rowH)}\" fill=\"{Attr(cellStyle.Background)}\"/>");
                    string? color = cellStyle?.Color ?? rowStyle?.Color;
                    string? weight = (cellStyle?.Bold ?? rowStyle?.Bold) switch { true => "bold", false => "normal", _ => null };
                    sb.Append(Cell(text, i, x, y + rowH / 2, weight, color, link));
                    x += widths[i];
                }
                y += rowH;
                sb.Append(HLine(b.X, b.X + b.W, y, t.GridColor));
            }
            return sb.ToString();
        }

        /// <summary>
        /// A grid of cards, one per row: an optional media picture and up to three text lines. Same layout as the
        /// browser's ReportElementView.
        /// </summary>
        private string Cards(ReportElement e, Box b, Ctx ctx, ReportSlice? slice)
        {
            var s = e.Style;
            var o = e.Cards ?? new ReportCardOptions();
            var data = ReportTokenResolver.FindData(ctx.Model, e.DataSourceId);
            if (data == null) return Message("No data source", b, s, ctx);
            if (data.Error != null) return Message(data.Error, b, s, ctx, "#ef4444");
            if (data.Rows.Count == 0) return Message("No rows", b, s, ctx);

            var grid = CardGrid.Of(o, b.W);
            int total = ReportPaginator.TotalRows(e, ctx.Model);
            int start = slice?.Start ?? 0;
            int count = slice?.Count ?? Math.Min(total, ReportPaginator.Capacity(e, b.H + 2 * s.Padding));
            count = Math.Max(0, Math.Min(count, total - start));
            bool picture = o.ImagePosition is "top" or "left" && !string.IsNullOrWhiteSpace(o.ImageField);
            double share = Math.Clamp(o.ImageSize, 0.1, 0.95);
            var sb = new StringBuilder();

            for (int n = 0; n < count; n++)
            {
                int r = start + n;
                var row = data.Rows[r];
                var (cx, cy) = grid.Position(n, b.X, b.Y);
                var card = new Box(cx, cy, grid.CardWidth, grid.CardHeight);
                var cond = ReportConditions.Style(e.Conditions, row, null);
                string? link = string.IsNullOrWhiteSpace(e.Link) ? null : ReportTokenResolver.ResolveLink(e.Link, null, row, ctx.Model, ctx.PageNumber, ctx.PageCount);

                string clipId = $"card-{Id(e.Id)}-{r}";
                if (link != null) sb.Append(LinkStart(card, link, ctx));
                sb.Append($"<defs><clipPath id=\"{clipId}\"><rect x=\"{N(card.X)}\" y=\"{N(card.Y)}\" width=\"{N(card.W)}\" height=\"{N(card.H)}\" rx=\"{N(o.CardRadius)}\"/></clipPath></defs>");
                sb.Append($"<g clip-path=\"url(#{clipId})\">");
                string? background = cond?.Background ?? o.CardBackground;
                if (!string.IsNullOrEmpty(background))
                    sb.Append($"<rect x=\"{N(card.X)}\" y=\"{N(card.Y)}\" width=\"{N(card.W)}\" height=\"{N(card.H)}\" fill=\"{Attr(background)}\"/>");

                Box text;
                if (picture)
                {
                    var image = o.ImagePosition == "left"
                        ? new Box(card.X, card.Y, card.W * share, card.H)
                        : new Box(card.X, card.Y, card.W, card.H * share);
                    string? src = ctx.Model.Images.GetValueOrDefault($"{e.Id}#{r}");
                    sb.Append(src != null
                        ? $"<image x=\"{N(image.X)}\" y=\"{N(image.Y)}\" width=\"{N(image.W)}\" height=\"{N(image.H)}\" preserveAspectRatio=\"xMidYMid slice\" xlink:href=\"{Attr(src)}\" href=\"{Attr(src)}\"/>"
                        : $"<rect x=\"{N(image.X)}\" y=\"{N(image.Y)}\" width=\"{N(image.W)}\" height=\"{N(image.H)}\" fill=\"#e5e7eb\"/>");
                    text = o.ImagePosition == "left"
                        ? new Box(image.X + image.W + 10, card.Y + 8, Math.Max(0, card.W - image.W - 18), Math.Max(0, card.H - 16))
                        : new Box(card.X + 8, image.Y + image.H + 6, Math.Max(0, card.W - 16), Math.Max(0, card.H - image.H - 12));
                }
                else
                {
                    text = new Box(card.X + 10, card.Y + 8, Math.Max(0, card.W - 20), Math.Max(0, card.H - 16));
                }

                string color = cond?.Color ?? s.Color;
                string? valueWeight = cond?.Bold switch { true => "bold", false => "normal", _ => null };
                double small = Math.Max(9, s.FontSize * 0.85);
                var lines = new List<(string Text, double Size, string? Weight, double? Opacity)>();
                void Line(string? template, double size, string? weight, double? opacity)
                {
                    string value = ReportTokenResolver.Resolve(template, ctx.Model, ctx.PageNumber, ctx.PageCount, row, data.Columns);
                    if (value.Length > 0) lines.Add((value, size, weight, opacity));
                }
                Line(o.Title, s.FontSize, "bold", null);
                Line(o.Subtitle, small, "normal", 0.7);
                Line(o.Value, s.FontSize, valueWeight, null);

                double ty = text.Y;
                var (tx, anchor) = Align(s.TextAlign, text);
                foreach (var (value, size, weight, opacity) in lines)
                {
                    double lineH = size * 1.35;
                    if (ty + lineH > text.Y + text.H + 1) break;
                    sb.Append(Label(Ellipsis(value, text.W, s, ctx, size, weight), tx, ty + lineH / 2, anchor, s, ctx, size, weight, color, opacity));
                    ty += lineH;
                }
                sb.Append("</g>");
                if (link != null) sb.Append(LinkEnd(ctx));
            }
            return sb.ToString();
        }

        private static string HLine(double x1, double x2, double y, string color) =>
            $"<line x1=\"{N(x1)}\" y1=\"{N(y)}\" x2=\"{N(x2)}\" y2=\"{N(y)}\" stroke=\"{Attr(color)}\" stroke-width=\"1\"/>";

        private string Image(ReportElement e, Box b, Ctx ctx)
        {
            // Images fetched from the media server (ReportImageResolver) take precedence over the element's own Src
            string? src = ctx.Model.Images.GetValueOrDefault(e.Id) ?? (e.Media == null ? e.Src : null);
            bool isData = src != null && src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(src) || (!isData && !ctx.Options.AllowRemoteImages))
                return $"<rect x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" fill=\"#f3f4f6\"/>"
                    + Message(string.IsNullOrEmpty(e.Src) && e.Media == null ? "Image" : "Image not available", b, e.Style, ctx);
            string aspect = e.Fit switch { "cover" => "xMidYMid slice", "fill" => "none", _ => "xMidYMid meet" };
            return $"<image x=\"{N(b.X)}\" y=\"{N(b.Y)}\" width=\"{N(b.W)}\" height=\"{N(b.H)}\" preserveAspectRatio=\"{aspect}\" xlink:href=\"{Attr(src)}\" href=\"{Attr(src)}\"/>";
        }

        private static string Shape(ReportElement e)
        {
            var s = e.Style;
            string fill = string.IsNullOrEmpty(s.Background) ? "none" : Attr(s.Background);
            string stroke = s.BorderWidth > 0 && !string.IsNullOrEmpty(s.BorderColor)
                ? $" stroke=\"{Attr(s.BorderColor)}\" stroke-width=\"{N(s.BorderWidth)}\"{Dash(s.BorderStyle, s.BorderWidth)}" : "";
            double half = s.BorderWidth > 0 ? s.BorderWidth / 2 : 0;
            if (e.Shape == "ellipse")
                return $"<ellipse cx=\"{N(e.X + e.Width / 2)}\" cy=\"{N(e.Y + e.Height / 2)}\" rx=\"{N(Math.Max(0, e.Width / 2 - half))}\" ry=\"{N(Math.Max(0, e.Height / 2 - half))}\" fill=\"{fill}\"{stroke}/>";
            return $"<rect x=\"{N(e.X + half)}\" y=\"{N(e.Y + half)}\" width=\"{N(Math.Max(0, e.Width - 2 * half))}\" height=\"{N(Math.Max(0, e.Height - 2 * half))}\" rx=\"{N(s.BorderRadius)}\" fill=\"{fill}\"{stroke}/>";
        }

        private static string Line(ReportElement e)
        {
            var s = e.Style;
            double w = s.BorderWidth > 0 ? s.BorderWidth : 2;
            string color = string.IsNullOrEmpty(s.BorderColor) ? s.Color : s.BorderColor!;
            bool horizontal = e.Width >= e.Height;
            var (x1, y1, x2, y2) = horizontal
                ? (e.X, e.Y + e.Height / 2, e.X + e.Width, e.Y + e.Height / 2)
                : (e.X + e.Width / 2, e.Y, e.X + e.Width / 2, e.Y + e.Height);
            return $"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"{Attr(color)}\" stroke-width=\"{N(w)}\"{Dash(s.BorderStyle, w)}/>";
        }

        // ------------------------------------------------------------------ charts

        private string Chart(ReportElement e, Box b, Ctx ctx)
        {
            var s = e.Style;
            var c = e.Chart ?? new ReportChartOptions();
            var data = ReportTokenResolver.FindData(ctx.Model, e.DataSourceId);
            if (data == null) return Message("No data source", b, s, ctx);
            if (data.Error != null) return Message(data.Error, b, s, ctx, "#ef4444");
            if (c.Series.Count == 0 || string.IsNullOrEmpty(c.CategoryField)) return Message("Choose a category and at least one series", b, s, ctx);
            if (data.Rows.Count == 0) return Message("No data", b, s, ctx);

            var palette = c.Palette is { Count: > 0 } ? c.Palette : DefaultPalette.ToList();
            var categoryType = data.Columns.FirstOrDefault(x => x.Name == c.CategoryField)?.Type;
            var categories = data.Rows.Select(r => ReportFormatter.Format(r.GetValueOrDefault(c.CategoryField!), c.CategoryFormat, categoryType, ctx.Zone)).ToList();
            var series = c.Series.Select((sr, i) => new ChartSeries(
                sr.Label ?? data.Columns.FirstOrDefault(x => x.Name == sr.Field)?.Label ?? sr.Field,
                sr.Color ?? palette[i % palette.Count],
                data.Rows.Select(r => ReportFormatter.TryDouble(r.GetValueOrDefault(sr.Field), out var v) ? v : 0).ToArray(),
                data.Columns.FirstOrDefault(x => x.Name == sr.Field)?.Type)).ToList();
            string? valueType = series[0].Type;
            string FormatValue(double v) => ReportFormatter.Format(v, c.ValueFormat, valueType, ctx.Zone);

            double axisFont = Math.Max(9, s.FontSize * 0.8);
            var sb = new StringBuilder();
            bool pie = c.Kind is Kinds.Pie or Kinds.Donut;

            // Legend across the top
            if (c.ShowLegend && (series.Count > 1 || pie))
            {
                var items = pie
                    ? categories.Select((cat, i) => (cat, palette[i % palette.Count])).ToList()
                    : series.Select(sr => (sr.Name, sr.Color)).ToList();
                double lx = b.X, ly = b.Y + axisFont / 2 + 2;
                foreach (var (name, color) in items)
                {
                    string text = Ellipsis(name, 120, s, ctx, axisFont);
                    double w = 14 + ctx.Measure(text, s, axisFont) + 12;
                    if (lx + w > b.X + b.W && lx > b.X) break;
                    sb.Append($"<rect x=\"{N(lx)}\" y=\"{N(ly - 5)}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{Attr(color)}\"/>");
                    sb.Append(Label(text, lx + 14, ly, "start", s, ctx, axisFont, "normal", c.AxisColor));
                    lx += w;
                }
                double legendH = axisFont + 10;
                b = new Box(b.X, b.Y + legendH, b.W, Math.Max(0, b.H - legendH));
            }

            if (pie)
            {
                sb.Append(Pie(c, b, categories, series[0], palette, s, ctx, axisFont));
                return sb.ToString();
            }

            bool horizontal = c.Kind == Kinds.HorizontalBar;
            bool stacked = c.Stacked && c.Kind is Kinds.Bar or Kinds.HorizontalBar or Kinds.Area;
            int n = categories.Count;

            double max = 0, min = 0;
            for (int i = 0; i < n; i++)
            {
                if (stacked)
                {
                    max = Math.Max(max, series.Sum(sr => Math.Max(0, sr.Values[i])));
                    min = Math.Min(min, series.Sum(sr => Math.Min(0, sr.Values[i])));
                }
                else
                {
                    foreach (var sr in series) { max = Math.Max(max, sr.Values[i]); min = Math.Min(min, sr.Values[i]); }
                }
            }
            var ticks = ReportChartScale.NiceTicks(min, max, 5);
            double lo = ticks[0], hi = ticks[^1];

            // Axis space
            var tickLabels = ticks.Select(FormatValue).ToList();
            double valueLabelW = c.ShowAxes ? tickLabels.Max(t => ctx.Measure(t, s, axisFont)) + 8 : 0;
            double categoryLabelW = horizontal && c.ShowAxes ? Math.Min(b.W * 0.4, categories.Max(cat => ctx.Measure(cat, s, axisFont)) + 8) : 0;
            double bottom = c.ShowAxes ? axisFont + 10 : 0;
            double topPad = c.ShowValues ? axisFont + 4 : 4;
            var plot = horizontal
                ? new Box(b.X + categoryLabelW, b.Y + 4, Math.Max(1, b.W - categoryLabelW - 12), Math.Max(1, b.H - bottom - 4))
                : new Box(b.X + valueLabelW, b.Y + topPad, Math.Max(1, b.W - valueLabelW - 8), Math.Max(1, b.H - bottom - topPad));

            double Scale(double v) => horizontal
                ? plot.X + (v - lo) / (hi - lo) * plot.W
                : plot.Y + plot.H - (v - lo) / (hi - lo) * plot.H;

            // Grid and value axis
            for (int i = 0; i < ticks.Length; i++)
            {
                double p = Scale(ticks[i]);
                if (c.ShowGrid)
                    sb.Append(horizontal
                        ? $"<line x1=\"{N(p)}\" y1=\"{N(plot.Y)}\" x2=\"{N(p)}\" y2=\"{N(plot.Y + plot.H)}\" stroke=\"{Attr(c.GridColor)}\" stroke-width=\"1\"/>"
                        : HLine(plot.X, plot.X + plot.W, p, c.GridColor));
                if (c.ShowAxes)
                    sb.Append(horizontal
                        ? Label(tickLabels[i], p, plot.Y + plot.H + bottom / 2 + 2, "middle", s, ctx, axisFont, "normal", c.AxisColor)
                        : Label(tickLabels[i], plot.X - 6, p, "end", s, ctx, axisFont, "normal", c.AxisColor));
            }

            // Category axis
            double band = (horizontal ? plot.H : plot.W) / Math.Max(1, n);
            if (c.ShowAxes)
            {
                if (horizontal)
                {
                    for (int i = 0; i < n; i++)
                        sb.Append(Label(Ellipsis(categories[i], categoryLabelW - 8, s, ctx, axisFont), plot.X - 6, plot.Y + band * (i + 0.5), "end", s, ctx, axisFont, "normal", c.AxisColor));
                }
                else
                {
                    double widest = categories.Max(cat => ctx.Measure(cat, s, axisFont));
                    int every = Math.Max(1, (int)Math.Ceiling(Math.Min(widest + 8, 140) / band));
                    for (int i = 0; i < n; i += every)
                        sb.Append(Label(Ellipsis(categories[i], band * every - 4, s, ctx, axisFont), plot.X + band * (i + 0.5), plot.Y + plot.H + bottom / 2 + 2, "middle", s, ctx, axisFont, "normal", c.AxisColor));
                }
                sb.Append(horizontal
                    ? $"<line x1=\"{N(Scale(Math.Max(lo, 0)))}\" y1=\"{N(plot.Y)}\" x2=\"{N(Scale(Math.Max(lo, 0)))}\" y2=\"{N(plot.Y + plot.H)}\" stroke=\"{Attr(c.AxisColor)}\" stroke-width=\"1\"/>"
                    : HLine(plot.X, plot.X + plot.W, Scale(Math.Max(lo, 0)), c.AxisColor));
            }

            double zero = Scale(Math.Max(lo, Math.Min(0, hi)));
            if (c.Kind is Kinds.Bar or Kinds.HorizontalBar)
            {
                double inner = band * 0.8;
                double barW = stacked ? inner : inner / series.Count;
                for (int i = 0; i < n; i++)
                {
                    double posBase = 0, negBase = 0;
                    for (int k = 0; k < series.Count; k++)
                    {
                        double v = series[k].Values[i];
                        double from, to;
                        if (stacked)
                        {
                            from = v >= 0 ? posBase : negBase;
                            to = from + v;
                            if (v >= 0) posBase = to; else negBase = to;
                        }
                        else { from = 0; to = v; }
                        double a = Scale(from), z = Scale(to);
                        double offset = band * 0.1 + (stacked ? 0 : k * barW);
                        string rect = horizontal
                            ? $"<rect x=\"{N(Math.Min(a, z))}\" y=\"{N(plot.Y + band * i + offset)}\" width=\"{N(Math.Abs(z - a))}\" height=\"{N(Math.Max(0.5, barW - 1))}\" fill=\"{Attr(series[k].Color)}\"/>"
                            : $"<rect x=\"{N(plot.X + band * i + offset)}\" y=\"{N(Math.Min(a, z))}\" width=\"{N(Math.Max(0.5, barW - 1))}\" height=\"{N(Math.Abs(z - a))}\" fill=\"{Attr(series[k].Color)}\"/>";
                        sb.Append(rect);
                        if (c.ShowValues && (!stacked || k == series.Count - 1))
                        {
                            string label = FormatValue(stacked ? posBase + negBase : v);
                            sb.Append(horizontal
                                ? Label(label, Math.Max(a, z) + 4, plot.Y + band * i + offset + barW / 2, "start", s, ctx, axisFont * 0.9, "normal", c.AxisColor)
                                : Label(label, plot.X + band * i + offset + (stacked ? inner : barW) / 2, Math.Min(a, z) - axisFont * 0.6, "middle", s, ctx, axisFont * 0.9, "normal", c.AxisColor));
                        }
                    }
                }
            }
            else
            {
                double[] stackBase = new double[n];
                for (int k = 0; k < series.Count; k++)
                {
                    var points = new List<(double X, double Y)>();
                    var basePoints = new List<(double X, double Y)>();
                    for (int i = 0; i < n; i++)
                    {
                        double x = plot.X + band * (i + 0.5);
                        double v = series[k].Values[i];
                        double from = stacked ? stackBase[i] : 0;
                        double to = from + v;
                        if (stacked) stackBase[i] = to;
                        points.Add((x, Scale(stacked ? to : v)));
                        basePoints.Add((x, stacked ? Scale(from) : zero));
                    }
                    string line = string.Join(" ", points.Select(p => $"{N(p.X)},{N(p.Y)}"));
                    if (c.Kind == Kinds.Area)
                    {
                        string area = "M" + string.Join(" L", points.Select(p => $"{N(p.X)},{N(p.Y)}"))
                            + " L" + string.Join(" L", Enumerable.Reverse(basePoints).Select(p => $"{N(p.X)},{N(p.Y)}")) + " Z";
                        sb.Append($"<path d=\"{area}\" fill=\"{Attr(series[k].Color)}\" fill-opacity=\"0.25\" stroke=\"none\"/>");
                    }
                    sb.Append($"<polyline points=\"{line}\" fill=\"none\" stroke=\"{Attr(series[k].Color)}\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
                    if (n <= 60)
                        foreach (var p in points)
                            sb.Append($"<circle cx=\"{N(p.X)}\" cy=\"{N(p.Y)}\" r=\"3\" fill=\"{Attr(series[k].Color)}\"/>");
                    if (c.ShowValues)
                        for (int i = 0; i < n; i++)
                            sb.Append(Label(FormatValue(series[k].Values[i]), points[i].X, points[i].Y - axisFont * 0.8, "middle", s, ctx, axisFont * 0.9, "normal", c.AxisColor));
                }
            }
            return sb.ToString();
        }

        private record ChartSeries(string Name, string Color, double[] Values, string? Type);

        private static string Pie(ReportChartOptions c, Box b, List<string> categories, ChartSeries series, List<string> palette, ReportElementStyle s, Ctx ctx, double axisFont)
        {
            var sb = new StringBuilder();
            double total = series.Values.Where(v => v > 0).Sum();
            if (total <= 0) return Message("No data", b, s, ctx);
            double r = Math.Max(1, Math.Min(b.W, b.H) / 2 - 4);
            double cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
            double inner = c.Kind == Kinds.Donut ? r * 0.6 : 0;
            double angle = -Math.PI / 2;
            for (int i = 0; i < series.Values.Length; i++)
            {
                double v = series.Values[i];
                if (v <= 0) continue;
                double sweep = v / total * Math.PI * 2;
                string color = palette[i % palette.Count];
                sb.Append($"<path d=\"{ReportChartScale.ArcPath(cx, cy, r, inner, angle, angle + sweep)}\" fill=\"{Attr(color)}\" stroke=\"#ffffff\" stroke-width=\"1\"/>");
                if (c.ShowValues && v / total >= 0.05)
                {
                    double mid = angle + sweep / 2, lr = inner > 0 ? (r + inner) / 2 : r * 0.62;
                    sb.Append(Label((v / total * 100).ToString("0", CultureInfo.InvariantCulture) + "%", cx + Math.Cos(mid) * lr, cy + Math.Sin(mid) * lr, "middle", s, ctx, axisFont, "bold", "#ffffff"));
                }
                angle += sweep;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        public static string N(double v) => double.IsFinite(v) ? Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture) : "0";
        private static string Xml(string s) => SecurityElement.Escape(s) ?? "";
        private static string Attr(string? s) => SecurityElement.Escape(s ?? "") ?? "";
        private static string Id(string s) => new(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
    }

    public static class ReportChartScale
    {
        /// <summary>Round axis ticks covering [min, max] ("nice numbers": steps of 1, 2 or 5 x 10^n).</summary>
        public static double[] NiceTicks(double min, double max, int count)
        {
            if (max <= min) max = min + 1;
            double step = NiceNumber((max - min) / Math.Max(1, count - 1));
            double lo = Math.Floor(min / step) * step;
            double hi = Math.Ceiling(max / step) * step;
            var ticks = new List<double>();
            for (double v = lo; v <= hi + step / 2; v += step) ticks.Add(Math.Round(v, 10));
            return ticks.ToArray();
        }

        private static double NiceNumber(double x)
        {
            double exp = Math.Floor(Math.Log10(x));
            double f = x / Math.Pow(10, exp);
            double nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
            return nf * Math.Pow(10, exp);
        }

        public static string ArcPath(double cx, double cy, double r, double inner, double a0, double a1)
        {
            // A full circle can't be drawn with one arc; split it
            if (a1 - a0 >= Math.PI * 2 - 0.0001) a1 = a0 + Math.PI * 2 - 0.0001;
            int large = a1 - a0 > Math.PI ? 1 : 0;
            string P(double rad, double a) => $"{SvgReportRenderer.N(cx + Math.Cos(a) * rad)},{SvgReportRenderer.N(cy + Math.Sin(a) * rad)}";
            if (inner <= 0)
                return $"M{P(0, 0)} L{P(r, a0)} A{SvgReportRenderer.N(r)},{SvgReportRenderer.N(r)} 0 {large} 1 {P(r, a1)} Z";
            return $"M{P(r, a0)} A{SvgReportRenderer.N(r)},{SvgReportRenderer.N(r)} 0 {large} 1 {P(r, a1)} L{P(inner, a1)} A{SvgReportRenderer.N(inner)},{SvgReportRenderer.N(inner)} 0 {large} 0 {P(inner, a0)} Z";
        }
    }
}
