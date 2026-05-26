namespace Jellystat_API_Core.Helpers
{
    public static class VideoMetadataParser
    {
        // Define a clean blueprint for our quality configurations
        private class ResolutionMatch
        {
            public int MinEffectiveWidth { get; init; }
            public string Label { get; init; }
        }

        // Comprehensive list covering all common distribution, streaming, and broadcast labels
        // Sorted strictly from highest resolution down to lowest
        private static readonly List<ResolutionMatch> QualityRegistry = new()
    {
        new ResolutionMatch { MinEffectiveWidth = 7500, Label = "8K" },
        new ResolutionMatch { MinEffectiveWidth = 3800, Label = "4K" },
        new ResolutionMatch { MinEffectiveWidth = 2500, Label = "1440p" },
        new ResolutionMatch { MinEffectiveWidth = 1900, Label = "1080p" },
        new ResolutionMatch { MinEffectiveWidth = 1260, Label = "720p" },
        new ResolutionMatch { MinEffectiveWidth = 960,  Label = "576p" }, // PAL Standard / Wide SD
        new ResolutionMatch { MinEffectiveWidth = 700,  Label = "480p" }, // NTSC Standard Definition
        new ResolutionMatch { MinEffectiveWidth = 480,  Label = "360p" }, // Low-Bandwidth Streaming
        new ResolutionMatch { MinEffectiveWidth = 320,  Label = "240p" }  // Legacy Mobile / Web Baseline
    };

        public static string GetResolutionLabel(int width, int height, string? aspectRatioStr)
        {
            // 1. Hard validation gate
            if (width <= 0 || height <= 0)
            {
                return "Unknown";
            }

            double aspectRatio = ParseAspectRatio(aspectRatioStr);
            if (aspectRatio <= 0)
            {
                aspectRatio = (double)width / height;
            }

            // 2. Cross-reference dimension with aspect ratio
            // This calculates the implied horizontal width if the file has anamorphic stretching
            double calculatedWidth = height * aspectRatio;

            // 3. Determine the absolute maximum dimension index 
            // Handles ultra-wide landscape, standard 16:9, and flipped portrait videos uniformly
            double maxDimension = Math.Max(width, height);
            int effectiveWidth = (int)Math.Max(maxDimension, calculatedWidth);

            // 4. Scan the registry for the matching bracket
            foreach (var target in QualityRegistry)
            {
                if (effectiveWidth >= target.MinEffectiveWidth)
                {
                    return target.Label;
                }
            }

            // 5. Explicitly return "Unknown" if it falls below the minimum threshold (240p)
            //return $"Unknown {width}x{height} - effectiveWidth {effectiveWidth}";
            return "SD";
        }

        /// <summary>
        /// Safely converts an aspect ratio string like "16:9" or "1.7778" into a double.
        /// Returns 0.0 if null, empty, or malformed.
        /// </summary>
        private static double ParseAspectRatio(string? aspectRatioStr)
        {
            if (string.IsNullOrWhiteSpace(aspectRatioStr))
            {
                return 0.0;
            }

            // Handle standard fractional strings like "16:9"
            if (aspectRatioStr.Contains(':'))
            {
                string[] parts = aspectRatioStr.Split(':');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], out double num) &&
                    double.TryParse(parts[1], out double den) &&
                    den > 0)
                {
                    return num / den;
                }
            }

            // Fallback: If the string is already a pre-calculated double (e.g. "1.7777")
            if (double.TryParse(aspectRatioStr, out double result))
            {
                return result;
            }

            return 0.0;
        }
    }
}
