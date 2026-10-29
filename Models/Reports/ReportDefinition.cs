using System.Text.Json;

namespace Jellystat_API_Core.Models.Reports
{
    /// <summary>
    /// A saved report layout, as produced by the web UI designer. Stored as JSON in Reports.Definition and
    /// shared by every renderer (web UI, server SVG/HTML/PNG/PDF), so keep it in sync with lib/models/reports.ts in the UI.
    /// Coordinates and sizes are CSS pixels at 96 DPI.
    /// </summary>
    public class ReportDefinition
    {
        public int Version { get; set; } = 1;
        public ReportPageSettings Page { get; set; } = new();
        public List<ReportPage> Pages { get; set; } = [];
        public List<ReportDataSource> DataSources { get; set; } = [];
        public List<ReportParameter> Parameters { get; set; } = [];
        /// <summary>
        /// IANA time zone (e.g. Europe/London) for date buckets, relative date filters and displayed dates.
        /// Null = UTC.
        /// </summary>
        public string? TimeZone { get; set; }
    }

    public class ReportPageSettings
    {
        /// <summary>Preset name (A4, A3, Letter, Legal, Dashboard, Custom). Informational: Width/Height are what render.</summary>
        public string Size { get; set; } = "A4";
        public string Orientation { get; set; } = "portrait";
        public double Width { get; set; } = 794;
        public double Height { get; set; } = 1123;
        /// <summary>Designer guide only; elements are positioned absolutely and may sit in the margin.</summary>
        public double Margin { get; set; } = 32;
        public string Background { get; set; } = "#ffffff";
    }

    public class ReportPage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Page 1";
        /// <summary>Paint order: later elements are drawn on top.</summary>
        public List<ReportElement> Elements { get; set; } = [];
    }

    public static class ReportElementTypes
    {
        public const string Text = "text";
        public const string Metric = "metric";
        public const string Table = "table";
        public const string Chart = "chart";
        public const string Image = "image";
        public const string Shape = "shape";
        public const string Line = "line";
        public const string Cards = "cards";
    }

    public class ReportElement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Type { get; set; } = ReportElementTypes.Text;
        public string? Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 200;
        public double Height { get; set; } = 40;
        public bool Hidden { get; set; }
        public bool Locked { get; set; }
        public double Opacity { get; set; } = 1;
        public ReportElementStyle Style { get; set; } = new();

        /// <summary>text: content, with {{tokens}} (see ReportTokenResolver).</summary>
        public string? Text { get; set; }

        /// <summary>metric, table, chart: the data source the element reads.</summary>
        public string? DataSourceId { get; set; }

        // metric
        public string? ValueField { get; set; }
        public string? Label { get; set; }
        public string? Format { get; set; }
        public string? Prefix { get; set; }
        public string? Suffix { get; set; }

        // table
        public List<ReportTableColumn>? Columns { get; set; }
        public ReportTableOptions? Table { get; set; }

        // chart
        public ReportChartOptions? Chart { get; set; }

        // image: data: URI (http(s) URLs only render in the browser, the server never fetches them)
        public string? Src { get; set; }
        /// <summary>contain | cover | fill</summary>
        public string? Fit { get; set; }

        /// <summary>image: a picture from the media server (poster, backdrop or user avatar) instead of Src.</summary>
        public ReportImageBinding? Media { get; set; }

        /// <summary>
        /// text, image, metric: makes the element a hyperlink. A URL with {{tokens}}; for a metric with the "link"
        /// format, {{value}} is its value and an empty link uses the value itself as the URL.
        /// </summary>
        public string? Link { get; set; }

        // shape: rect | ellipse
        public string? Shape { get; set; }

        /// <summary>table, cards: rows that don't fit continue on extra pages added after this page.</summary>
        public bool Grow { get; set; }
        /// <summary>Where a growing element starts on those extra pages (px from the top); its bottom edge stays put.</summary>
        public double? ContinueTop { get; set; }
        /// <summary>Also draw this element on the extra pages growing elements add (titles, footers, page numbers).</summary>
        public bool Repeat { get; set; }

        /// <summary>table, cards, metric: styles applied to rows (or cells) that match.</summary>
        public List<ReportCondition>? Conditions { get; set; }

        /// <summary>cards: one card per row, with an optional poster/avatar and text lines.</summary>
        public ReportCardOptions? Cards { get; set; }
    }

    public class ReportCondition
    {
        public string Field { get; set; } = "";
        /// <summary>eq, neq, gt, gte, lt, lte, contains, notContains, isNull, isNotNull</summary>
        public string Operator { get; set; } = "eq";
        public JsonElement? Value { get; set; }
        /// <summary>table: the column to style; empty styles the whole row.</summary>
        public string? Target { get; set; }
        public string? Color { get; set; }
        public string? Background { get; set; }
        public bool? Bold { get; set; }
    }

    public class ReportCardOptions
    {
        public int Columns { get; set; } = 4;
        public double CardHeight { get; set; } = 240;
        public double Gap { get; set; } = 12;
        /// <summary>Column holding the media item (or user) id of the picture; empty for no picture.</summary>
        public string? ImageField { get; set; }
        /// <summary>item | user</summary>
        public string ImageKind { get; set; } = "item";
        /// <summary>Primary | Backdrop</summary>
        public string ImageType { get; set; } = "Primary";
        /// <summary>top | left | none</summary>
        public string ImagePosition { get; set; } = "top";
        /// <summary>The picture's share of the card height (top) or width (left).</summary>
        public double ImageSize { get; set; } = 0.72;
        /// <summary>Text lines; {{row.FIELD}} and {{row.FIELD|format}} insert the card's row.</summary>
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Value { get; set; }
        public string? CardBackground { get; set; } = "#f3f4f6";
        public double CardRadius { get; set; } = 8;
        public int? MaxItems { get; set; }
    }

    public class ReportElementStyle
    {
        public double FontSize { get; set; } = 14;
        public string FontFamily { get; set; } = "Inter, Segoe UI, DejaVu Sans, Arial, sans-serif";
        /// <summary>normal | bold | 100-900</summary>
        public string FontWeight { get; set; } = "normal";
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string Color { get; set; } = "#111827";
        /// <summary>left | center | right</summary>
        public string TextAlign { get; set; } = "left";
        /// <summary>top | middle | bottom</summary>
        public string VerticalAlign { get; set; } = "top";
        public double LineHeight { get; set; } = 1.3;
        public string? Background { get; set; }
        public string? BorderColor { get; set; }
        public double BorderWidth { get; set; }
        /// <summary>solid | dashed | dotted</summary>
        public string BorderStyle { get; set; } = "solid";
        public double BorderRadius { get; set; }
        public double Padding { get; set; }
    }

    /// <summary>
    /// An image fetched from the report's media server. The id comes from Parameter, else the first row of
    /// DataSourceId's Field column, else the fixed Id.
    /// </summary>
    public class ReportImageBinding
    {
        /// <summary>item | user</summary>
        public string Type { get; set; } = "item";
        /// <summary>Primary (poster/avatar) | Backdrop (items only)</summary>
        public string ImageType { get; set; } = "Primary";
        public string? Id { get; set; }
        public string? DataSourceId { get; set; }
        public string? Field { get; set; }
        public string? Parameter { get; set; }
    }

    public class ReportTableColumn
    {
        public string Field { get; set; } = "";
        public string? Header { get; set; }
        /// <summary>Relative width weight; columns share the table width in proportion.</summary>
        public double Width { get; set; } = 1;
        public string Align { get; set; } = "left";
        public string? Format { get; set; }
        /// <summary>
        /// "link" format: the URL, with {{value}} for the cell and {{row.FIELD}} for other columns of the row
        /// (plus the usual tokens). Empty uses the cell value itself as the URL.
        /// </summary>
        public string? Link { get; set; }
    }

    public class ReportTableOptions
    {
        public bool ShowHeader { get; set; } = true;
        public string HeaderBackground { get; set; } = "#f3f4f6";
        public string HeaderColor { get; set; } = "#111827";
        public string? StripeBackground { get; set; } = "#f9fafb";
        public string GridColor { get; set; } = "#e5e7eb";
        public double RowHeight { get; set; } = 26;
        /// <summary>Rows beyond what fits in the element height are always clipped; this caps it further.</summary>
        public int? MaxRows { get; set; }
    }

    public static class ReportChartKinds
    {
        public const string Bar = "bar";
        public const string HorizontalBar = "horizontalBar";
        public const string Line = "line";
        public const string Area = "area";
        public const string Pie = "pie";
        public const string Donut = "donut";
    }

    public class ReportChartOptions
    {
        public string Kind { get; set; } = ReportChartKinds.Bar;
        public string? CategoryField { get; set; }
        public string? CategoryFormat { get; set; }
        public List<ReportChartSeries> Series { get; set; } = [];
        public string? ValueFormat { get; set; }
        public bool Stacked { get; set; }
        public bool ShowLegend { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowValues { get; set; }
        public bool ShowAxes { get; set; } = true;
        public string AxisColor { get; set; } = "#6b7280";
        public string GridColor { get; set; } = "#e5e7eb";
        public List<string>? Palette { get; set; }
    }

    public class ReportChartSeries
    {
        public string Field { get; set; } = "";
        public string? Label { get; set; }
        public string? Color { get; set; }
    }

    public class ReportParameter
    {
        public string Name { get; set; } = "";
        public string? Label { get; set; }
        /// <summary>string | number | date | boolean | lookup</summary>
        public string Type { get; set; } = "string";
        public JsonElement? DefaultValue { get; set; }
        /// <summary>lookup: where the dropdown's choices come from.</summary>
        public ReportParameterLookup? Lookup { get; set; }
    }

    /// <summary>A parameter whose choices are the distinct values of a column of one of the report's data sources.</summary>
    public class ReportParameterLookup
    {
        public string DataSourceId { get; set; } = "";
        /// <summary>Column whose value is passed to filters.</summary>
        public string ValueField { get; set; } = "";
        /// <summary>Column shown in the dropdown; defaults to ValueField.</summary>
        public string? LabelField { get; set; }
        /// <summary>Allow several choices; the value becomes an array and equals/not-equals rules become in/not in.</summary>
        public bool Multiple { get; set; }
    }

    public class ReportParameterOption
    {
        public object? Value { get; set; }
        public required string Label { get; set; }
    }
}
