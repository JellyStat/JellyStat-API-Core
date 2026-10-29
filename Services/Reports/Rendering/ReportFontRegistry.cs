using SkiaSharp;
using Svg.Skia.TypefaceProviders;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    public class ReportFontFace
    {
        public required string File { get; set; }
        public int Weight { get; set; } = 400;
        public bool Italic { get; set; }
    }

    public class ReportFontFamily
    {
        public required string Family { get; set; }
        /// <summary>sans-serif | serif | monospace | display</summary>
        public string Category { get; set; } = "sans-serif";
        public List<ReportFontFace> Files { get; set; } = [];
    }

    public interface IReportFontRegistry
    {
        IReadOnlyList<ReportFontFamily> Families { get; }
        /// <summary>The first family of a CSS font-family list that is bundled, if any.</summary>
        ReportFontFamily? Find(string? fontFamilyList);
        /// <summary>The closest bundled face (CSS matching: same style first, then nearest weight).</summary>
        SKTypeface? Match(string? fontFamilyList, int weight, bool italic);
        /// <summary>Full path of a bundled font file, or null when the name isn't one of them.</summary>
        string? FilePath(string file);
        /// <summary>@font-face rules embedding the bundled fonts the report uses (for HTML/SVG exports).</summary>
        string FontFaceCss(IEnumerable<string?> fontFamilyLists);
    }

    /// <summary>
    /// The fonts in /Fonts (see Fonts/README.md). Server renders draw with them and the web UI loads the same files,
    /// so a report looks the same everywhere without depending on what the host has installed.
    /// </summary>
    public class ReportFontRegistry : IReportFontRegistry
    {
        private readonly string _directory;
        private readonly Dictionary<string, ReportFontFamily> _families;
        private readonly Dictionary<string, string> _files;
        private readonly ConcurrentDictionary<string, SKTypeface?> _typefaces = new();

        public ReportFontRegistry(ILogger<ReportFontRegistry> logger)
            : this(Path.Combine(AppContext.BaseDirectory, "Fonts"), logger) { }

        public ReportFontRegistry(string directory, ILogger<ReportFontRegistry>? logger = null)
        {
            _directory = directory;
            List<ReportFontFamily> families = [];
            string manifest = Path.Combine(directory, "fonts.json");
            if (File.Exists(manifest))
            {
                families = JsonSerializer.Deserialize<List<ReportFontFamily>>(File.ReadAllText(manifest), ReportJson.Options) ?? [];
                foreach (var f in families) f.Files = f.Files.Where(x => File.Exists(Path.Combine(directory, x.File))).ToList();
                families = families.Where(f => f.Files.Count > 0).ToList();
            }
            else
            {
                logger?.LogWarning("No report fonts found at {Path}; reports will use system fonts", manifest);
            }
            Families = families;
            _families = families.ToDictionary(f => f.Family, StringComparer.OrdinalIgnoreCase);
            _files = families.SelectMany(f => f.Files).ToDictionary(x => x.File, x => Path.Combine(directory, x.File), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ReportFontFamily> Families { get; }

        public string? FilePath(string file) => _files.GetValueOrDefault(file);

        public ReportFontFamily? Find(string? fontFamilyList)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyList)) return null;
            foreach (var raw in fontFamilyList.Split(','))
            {
                string name = raw.Trim().Trim('"', '\'');
                if (_families.TryGetValue(name, out var family)) return family;
            }
            return null;
        }

        public SKTypeface? Match(string? fontFamilyList, int weight, bool italic)
        {
            var family = Find(fontFamilyList);
            if (family == null) return null;
            var face = family.Files
                .OrderBy(f => f.Italic == italic ? 0 : 1)
                // CSS-like: for bold-ish requests prefer heavier faces, for light ones lighter
                .ThenBy(f => Math.Abs(f.Weight - weight) + (weight >= 500 ? (f.Weight < weight ? 1 : 0) : (f.Weight > weight ? 1 : 0)))
                .First();
            return _typefaces.GetOrAdd(face.File, file => SKTypeface.FromFile(Path.Combine(_directory, file)));
        }

        public string FontFaceCss(IEnumerable<string?> fontFamilyLists)
        {
            var used = fontFamilyLists.Select(Find).Where(f => f != null).Distinct().ToList();
            var sb = new StringBuilder();
            foreach (var family in used)
            {
                foreach (var face in family!.Files)
                {
                    string data = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(_directory, face.File)));
                    sb.Append($"@font-face{{font-family:'{family.Family}';font-weight:{face.Weight};font-style:{(face.Italic ? "italic" : "normal")};src:url(data:font/ttf;base64,{data}) format('truetype')}}");
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Lets Svg.Skia draw with the bundled fonts; returns null for anything else so the system fonts are used.</summary>
    public class ReportFontTypefaceProvider : ITypefaceProvider
    {
        private readonly IReportFontRegistry _fonts;

        public ReportFontTypefaceProvider(IReportFontRegistry fonts)
        {
            _fonts = fonts;
        }

        public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight, SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle) =>
            _fonts.Match(fontFamily, (int)fontWeight, fontStyle != SKFontStyleSlant.Upright);
    }
}
