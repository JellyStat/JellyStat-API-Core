using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class ActivityStaging
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; }
        public string Name { get; set; }
        public string? SeriesName { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string? Client { get; set; }
        public string? Device { get; set; }
        public string? DeviceId { get; set; }
        public string ItemId { get; set; }
        public string? SeriesId { get; set; }
        public string? SeasonId { get; set; }
        public long Duration { get; set; } = 0;
        public bool DirectPlay { get; set; } = true;
        public string PlayMethod { get; set; }
        public IList<BaseMediaStream> MediaStreams { get; set; } = [];
        public BaseTranscodingInfo? TranscodingInfo { get; set; }
        public bool Imported { get; set; } = false;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime LastPaused { get; set; } = DateTime.UtcNow;
        public string? Container { get; set; }
        public string IpAddress { get; set; }
        public bool IsPaused { get; set; } = false;
        public long RuntimeTicks { get; set; } = 0;
        // Only kept for webhook payloads
        public ItemTypes ItemType { get; set; } = ItemTypes.Unknown;
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }

        public ActivityStaging() { }

    }
}
