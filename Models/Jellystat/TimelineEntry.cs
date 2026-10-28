using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.Jellystat
{
    /// <summary>
    /// One entry on a user's activity timeline: a continuous "run" of plays of the same title
    /// (a movie, or one or more consecutive seasons of a show).
    /// </summary>
    public class TimelineEntry
    {
        public required string ServerId { get; set; }

        /// <summary>Series name for episodes, item name otherwise.</summary>
        public required string Title { get; set; }

        /// <summary>Id used for the poster and the link: the series id for episodes, the item id otherwise.</summary>
        public required string ItemId { get; set; }

        public required string LibraryId { get; set; }
        public LibraryTypes LibraryType { get; set; } = LibraryTypes.Unknown;
        public bool IsSeries { get; set; }

        /// <summary>Lowest / highest season number watched in this run (null for non-episodic content).</summary>
        public int? FirstSeason { get; set; }
        public int? LastSeason { get; set; }

        public int EpisodeCount { get; set; }
        public DateTime FirstActivityDate { get; set; }
        public DateTime LastActivityDate { get; set; }

        /// <summary>Total playback duration in seconds.</summary>
        public long TotalPlayDuration { get; set; }

        public string? ImageHash { get; set; }
    }
}
