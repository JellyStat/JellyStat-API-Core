using SkiaSharp;
using System.Collections.Concurrent;

namespace Jellystat_API_Core.Services.Reports.Rendering
{
    public interface IReportTextMeasurer
    {
        double Measure(string text, string fontFamily, double fontSize, string fontWeight, bool italic);
        /// <summary>The first family in a CSS font-family list that is installed, for renderers that can't fall back themselves.</summary>
        string ResolveFamily(string fontFamily);
    }

    /// <summary>Measures text with the same fonts Skia will draw with, so server-side wrapping matches the PNG/PDF output.</summary>
    public class SkiaTextMeasurer : IReportTextMeasurer
    {
        private readonly IReportFontRegistry? _fonts;

        public SkiaTextMeasurer(IReportFontRegistry? fonts = null)
        {
            _fonts = fonts;
        }

        private static readonly ConcurrentDictionary<string, string> Families = new();
        private static readonly ConcurrentDictionary<(string, int, bool), SKTypeface> Typefaces = new();
        private static readonly Lazy<HashSet<string>> Installed = new(() =>
            new HashSet<string>(SKFontManager.Default.GetFontFamilies(), StringComparer.OrdinalIgnoreCase));

        public string ResolveFamily(string fontFamily) => _fonts?.Find(fontFamily)?.Family ?? Families.GetOrAdd(fontFamily ?? "", list =>
        {
            foreach (var raw in list.Split(','))
            {
                string name = raw.Trim().Trim('"', '\'');
                if (name.Length > 0 && Installed.Value.Contains(name)) return name;
            }
            return SKTypeface.Default.FamilyName;
        });

        public double Measure(string text, string fontFamily, double fontSize, string fontWeight, bool italic)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var typeface = _fonts?.Match(fontFamily, Weight(fontWeight), italic) ?? Typefaces.GetOrAdd((ResolveFamily(fontFamily), Weight(fontWeight), italic), key =>
                SKTypeface.FromFamilyName(key.Item1, new SKFontStyle(key.Item2, (int)SKFontStyleWidth.Normal, key.Item3 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright))
                ?? SKTypeface.Default);
            using var font = new SKFont(typeface, (float)fontSize);
            return font.MeasureText(text);
        }

        public static int Weight(string? weight) => weight?.ToLowerInvariant() switch
        {
            null or "" or "normal" => 400,
            "bold" => 700,
            "lighter" => 300,
            "bolder" => 800,
            _ => int.TryParse(weight, out int w) ? Math.Clamp(w, 100, 900) : 400
        };
    }
}
