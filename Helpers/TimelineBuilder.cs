using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;

namespace Jellystat_API_Core.Helpers
{
    /// <summary>
    /// Minimal projection of a play needed to build a timeline.
    /// </summary>
    public record TimelineActivityRow(
        string ItemId,
        string? SeriesId,
        string Title,
        DateTime DateCreated,
        long Duration,
        ItemTypes ItemType,
        int? SeasonNumber,
        string LibraryId);

    /// <summary>
    /// Builds a user's activity timeline from their plays. Pure and database-free so it can be unit tested.
    ///
    /// Rules (ported from Jellystat v1 fs_get_user_activity + the client side groupAdjacentSeasons):
    ///  1. Plays are ordered by date. A new run starts when the title differs from the previous play
    ///     (any other title in between interrupts a run) or when more than one month passed since the previous play.
    ///  2. Inside a run, episodes are grouped by season. A season (or a movie) with less than
    ///     <see cref="MinimumSeconds"/> of total playback is ignored; a run with nothing left is dropped.
    ///  3. Episodes with no known season are ignored.
    ///  4. A run becomes a single entry spanning the lowest to the highest remaining season.
    /// Entries are returned newest first.
    /// </summary>
    public static class TimelineBuilder
    {
        public const long MinimumSeconds = 20;

        public static List<TimelineEntry> Build(
            IEnumerable<TimelineActivityRow> rows,
            IReadOnlyDictionary<string, LibraryTypes> libraryTypes,
            string serverId,
            long minimumSeconds = MinimumSeconds)
        {
            var ordered = rows
                .OrderBy(r => r.DateCreated)
                .ThenBy(r => r.ItemId, StringComparer.Ordinal)
                .ToList();

            var runs = new List<List<TimelineActivityRow>>();
            List<TimelineActivityRow>? current = null;
            TimelineActivityRow? previous = null;

            foreach (var row in ordered)
            {
                bool startNewRun = current == null
                    || previous == null
                    || !string.Equals(previous.Title, row.Title, StringComparison.Ordinal)
                    || row.DateCreated > previous.DateCreated.AddMonths(1);

                if (startNewRun)
                {
                    current = new List<TimelineActivityRow>();
                    runs.Add(current);
                }

                current!.Add(row);
                previous = row;
            }

            var entries = new List<TimelineEntry>();
            foreach (var run in runs)
            {
                var entry = BuildEntry(run, libraryTypes, serverId, minimumSeconds);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries.OrderByDescending(e => e.LastActivityDate).ToList();
        }

        private static TimelineEntry? BuildEntry(
            List<TimelineActivityRow> run,
            IReadOnlyDictionary<string, LibraryTypes> libraryTypes,
            string serverId,
            long minimumSeconds)
        {
            // Episodes need a season number to be placed on the timeline.
            var usable = run
                .Where(r => r.ItemType != ItemTypes.Episode || r.SeasonNumber != null)
                .ToList();

            // Group by season for episodes; everything else is a single group.
            var keptRows = usable
                .GroupBy(r => r.ItemType == ItemTypes.Episode ? r.SeasonNumber : null)
                .Where(g => g.Sum(r => r.Duration) >= minimumSeconds)
                .SelectMany(g => g)
                .OrderBy(r => r.DateCreated)
                .ToList();

            if (keptRows.Count == 0)
            {
                return null;
            }

            var episodeRows = keptRows.Where(r => r.ItemType == ItemTypes.Episode).ToList();
            bool isSeries = episodeRows.Count > 0;
            var last = keptRows[^1];

            return new TimelineEntry
            {
                ServerId = serverId,
                Title = last.Title,
                ItemId = isSeries ? (last.SeriesId ?? last.ItemId) : last.ItemId,
                LibraryId = last.LibraryId,
                LibraryType = libraryTypes.TryGetValue(last.LibraryId, out var type) ? type : LibraryTypes.Unknown,
                IsSeries = isSeries,
                FirstSeason = isSeries ? episodeRows.Min(r => r.SeasonNumber) : null,
                LastSeason = isSeries ? episodeRows.Max(r => r.SeasonNumber) : null,
                EpisodeCount = keptRows.Select(r => r.ItemId).Distinct().Count(),
                FirstActivityDate = keptRows[0].DateCreated,
                LastActivityDate = last.DateCreated,
                TotalPlayDuration = keptRows.Sum(r => r.Duration),
            };
        }
    }
}
