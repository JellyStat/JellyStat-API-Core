using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Data.Schema
{
    public class Activity
    {
        public string Id { get; set; } = new Guid().ToString();
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
        public string? Container { get; set; }
        public string IpAddress { get; set; }
        public long RuntimeTicks { get; set; } = 0;

        public double CompletionPercentage
        {
            get
            {
                if (RuntimeTicks == 0) return 0;
                return ((double)Duration.ToTicks() / RuntimeTicks) * 100;
            }
        }



        public Activity() { }

    }
}
