using Jellystat_API_Core.Models.Reports;
using SkiaSharp;
using Svg.Skia;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    public static class ReportFormats
    {
        public const string Json = "json";
        public const string Svg = "svg";
        public const string Html = "html";
        public const string Png = "png";
        public const string Pdf = "pdf";

        public static readonly string[] All = [Json, Svg, Html, Png, Pdf];
    }

    public class ReportExportOptions
    {
        public string Format { get; set; } = ReportFormats.Pdf;
        /// <summary>1-based page for SVG/PNG; null or 0 = every page (stacked vertically).</summary>
        public int? Page { get; set; }
        /// <summary>PNG pixel density, 1-4.</summary>
        public double Scale { get; set; } = 2;
    }

    public record ReportFile(byte[] Content, string ContentType, string FileName);

    public interface IReportExporter
    {
        ReportFile Export(ReportRenderModel model, ReportExportOptions options);

        /// <summary>Fetches the report's media server images first (except for JSON), then exports.</summary>
        Task<ReportFile> ExportAsync(ReportRenderModel model, ReportExportOptions options, Datasets.ReportScope scope, CancellationToken cancellationToken = default);
    }

    public class ReportExporter : IReportExporter
    {
        private const double PageGap = 24;
        private readonly SvgReportRenderer _renderer;
        private readonly IReportImageResolver _images;
        private readonly IReportFontRegistry _fonts;

        public ReportExporter(SvgReportRenderer renderer, IReportImageResolver images, IReportFontRegistry fonts)
        {
            _renderer = renderer;
            _images = images;
            _fonts = fonts;
        }

        /// <summary>Bundled fonts the report uses, as embedded @font-face rules, so SVG/HTML files look right anywhere.</summary>
        private string FontCss(ReportRenderModel model) =>
            _fonts.FontFaceCss(model.Definition.Pages.SelectMany(p => p.Elements).Where(e => !e.Hidden).Select(e => e.Style.FontFamily));

        public async Task<ReportFile> ExportAsync(ReportRenderModel model, ReportExportOptions options, Datasets.ReportScope scope, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(options.Format, ReportFormats.Json, StringComparison.OrdinalIgnoreCase))
                await _images.ResolveAsync(model, scope, cancellationToken);
            return Export(model, options);
        }

        public ReportFile Export(ReportRenderModel model, ReportExportOptions options)
        {
            if (model.Definition.Pages.Count == 0) throw new ReportDefinitionException("The report has no pages");
            string format = (options.Format ?? ReportFormats.Pdf).ToLowerInvariant();
            string baseName = FileName(model.Name) + "-" + model.GeneratedAt.ToString("yyyyMMdd-HHmm");
            var pages = PageIndexes(model, format is ReportFormats.Pdf or ReportFormats.Html or ReportFormats.Json ? null : options.Page);

            switch (format)
            {
                case ReportFormats.Json:
                    return new ReportFile(JsonSerializer.SerializeToUtf8Bytes(model, ReportJson.Options), "application/json", baseName + ".json");

                case ReportFormats.Svg:
                    string svg = Stack(model, pages, new SvgRenderOptions { AllowRemoteImages = true });
                    string css = FontCss(model);
                    if (css.Length > 0)
                    {
                        int end = svg.IndexOf('>') + 1;
                        svg = svg[..end] + $"<defs><style>{css}</style></defs>" + svg[end..];
                    }
                    return new ReportFile(Encoding.UTF8.GetBytes(svg), "image/svg+xml", baseName + ".svg");

                case ReportFormats.Html:
                    return new ReportFile(Encoding.UTF8.GetBytes(Html(model, pages)), "text/html; charset=utf-8", baseName + ".html");

                case ReportFormats.Png:
                    return new ReportFile(Png(model, pages, Math.Clamp(options.Scale, 1, 4)), "image/png", baseName + ".png");

                case ReportFormats.Pdf:
                    return new ReportFile(Pdf(model, pages), "application/pdf", baseName + ".pdf");
            }
            throw new ReportDefinitionException($"Unknown format '{options.Format}'. Use one of: {string.Join(", ", ReportFormats.All)}");
        }

        private static List<int> PageIndexes(ReportRenderModel model, int? page)
        {
            int count = model.Definition.Pages.Count;
            if (page is null or 0) return Enumerable.Range(0, count).ToList();
            if (page < 1 || page > count) throw new ReportDefinitionException($"Page {page} doesn't exist; the report has {count}");
            return [page.Value - 1];
        }

        /// <summary>Several pages as one SVG, one under the other.</summary>
        private string Stack(ReportRenderModel model, List<int> pages, SvgRenderOptions options)
        {
            if (pages.Count == 1) return _renderer.RenderPage(model, pages[0], options);
            var p = model.Definition.Page;
            double height = pages.Count * p.Height + (pages.Count - 1) * PageGap;
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{SvgReportRenderer.N(p.Width)}\" height=\"{SvgReportRenderer.N(height)}\" viewBox=\"0 0 {SvgReportRenderer.N(p.Width)} {SvgReportRenderer.N(height)}\">");
            for (int i = 0; i < pages.Count; i++)
            {
                // Nested <svg> elements keep each page's ids and clipping separate
                string page = _renderer.RenderPage(model, pages[i], options);
                sb.Append(page.Replace("<svg ", $"<svg y=\"{SvgReportRenderer.N(i * (p.Height + PageGap))}\" "));
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        private string Html(ReportRenderModel model, List<int> pages)
        {
            var p = model.Definition.Page;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append($"<title>{WebUtility.HtmlEncode(model.Name)}</title><style>");
            sb.Append("html,body{margin:0;background:#e5e7eb}body{display:flex;flex-direction:column;align-items:center;gap:24px;padding:24px 16px}");
            sb.Append($".page{{width:100%;max-width:{SvgReportRenderer.N(p.Width)}px;box-shadow:0 4px 24px rgba(0,0,0,.15);background:{WebUtility.HtmlEncode(p.Background)}}}.page svg{{display:block;width:100%;height:auto}}");
            sb.Append($"@page{{size:{SvgReportRenderer.N(p.Width * 0.75)}pt {SvgReportRenderer.N(p.Height * 0.75)}pt;margin:0}}");
            sb.Append("@media print{html,body{background:none;padding:0;gap:0}.page{box-shadow:none;max-width:none;break-after:page}}");
            sb.Append(FontCss(model));
            sb.Append("</style></head><body>");
            foreach (int i in pages)
                sb.Append("<div class=\"page\">").Append(_renderer.RenderPage(model, i, new SvgRenderOptions { AllowRemoteImages = true })).Append("</div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>The SKSvg owns the picture: dispose it once the picture has been drawn.</summary>
        private SKSvg Picture(ReportRenderModel model, int page, out SKPicture picture, List<ReportLinkArea>? links = null)
        {
            string svg = _renderer.RenderPage(model, page, new SvgRenderOptions { ResolveFonts = true, Links = links });
            var skSvg = new SKSvg();
            // Bundled report fonts first; anything they don't cover falls through to the system fonts
            skSvg.Settings.TypefaceProviders?.Insert(0, new ReportFontTypefaceProvider(_fonts));
            picture = skSvg.FromSvg(svg) ?? throw new InvalidOperationException("Rendering the report page failed");
            return skSvg;
        }

        private byte[] Png(ReportRenderModel model, List<int> pages, double scale)
        {
            var p = model.Definition.Page;
            double height = pages.Count * p.Height + (pages.Count - 1) * PageGap;
            int w = (int)Math.Ceiling(p.Width * scale), h = (int)Math.Ceiling(height * scale);
            if ((long)w * h > 100_000_000) throw new ReportDefinitionException("The image would be too large; export fewer pages or a lower scale");

            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create an image surface");
            var canvas = surface.Canvas;
            canvas.Clear(pages.Count > 1 ? new SKColor(0xE5, 0xE7, 0xEB) : SKColors.Transparent);
            canvas.Scale((float)scale);
            for (int i = 0; i < pages.Count; i++)
            {
                using var svg = Picture(model, pages[i], out var picture);
                canvas.Save();
                canvas.Translate(0, (float)(i * (p.Height + PageGap)));
                canvas.DrawPicture(picture);
                canvas.Restore();
            }
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        /// <summary>Vector PDF: each page's SVG is drawn straight onto a PDF canvas (CSS px to points is 0.75).</summary>
        private byte[] Pdf(ReportRenderModel model, List<int> pages)
        {
            var p = model.Definition.Page;
            using var stream = new MemoryStream();
            var metadata = new SKDocumentPdfMetadata
            {
                Title = model.Name,
                Creator = "Jellystat",
                Producer = "Jellystat",
                Creation = model.GeneratedAt,
                Modified = model.GeneratedAt
            };
            using (var document = SKDocument.CreatePdf(stream, metadata))
            {
                foreach (int i in pages)
                {
                    var links = new List<ReportLinkArea>();
                    using var svg = Picture(model, i, out var picture, links);
                    var canvas = document.BeginPage((float)(p.Width * 0.75), (float)(p.Height * 0.75));
                    canvas.Scale(0.75f);
                    canvas.DrawPicture(picture);
                    // Clickable areas for the report's hyperlinks (mapped through the canvas scale like the drawing)
                    foreach (var link in links)
                        canvas.DrawUrlAnnotation(SKRect.Create((float)link.X, (float)link.Y, (float)link.Width, (float)link.Height), link.Url);
                    document.EndPage();
                }
                document.Close();
            }
            return stream.ToArray();
        }

        private static string FileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat([' ', '#', '%', '&']).ToHashSet();
            string clean = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('-');
            return clean.Length == 0 ? "report" : clean.Length > 80 ? clean[..80] : clean;
        }
    }

    public static class ReportJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
