using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseNowPlayingItem
    {
        //On a series, this is the episode id
        public virtual required string Id { get; set; }
        public virtual required string Name { get; set; }
        public virtual string? Container { get; set; }
        public virtual IList<string> Genres { get; set; } = new List<string>();
        public virtual bool? IsHD { get; set; }
        public virtual bool? IsFolder { get; set; }
        public virtual string? ParentId { get; set; }
        public virtual IList<BaseMediaStream> MediaStreams { get; set; } = new List<BaseMediaStream>();
        public virtual string ImageTag { get; set; } = "";
        public virtual string ImageHash { get; set; } = "";
        public virtual string? LocationType { get; set; }
        public virtual string? MediaType { get; set; }
        public virtual int? Width { get; set; }
        public virtual int? Height { get; set; }

        [JsonDefault(ItemTypes.Unknown)]
        public virtual ItemTypes Type { get; set; }
        public virtual IList<string> Providers { get; set; } = new List<string>();
        public virtual string? SeriesName { get; set; }
        public virtual string? SeriesId { get; set; }
        public virtual string? SeasonId { get; set; }
        public virtual long RuntimeTicks { get; set; } = 0;
        public virtual int? ParentIndexNumber { get; set; }
        public virtual int? IndexNumber { get; set; }
    }
}
