using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseItem
    {
        public virtual string Name { get; set; }
        public virtual string ServerId { get; set; }
        public virtual string Id { get; set; }
        public virtual DateTime? DateCreated { get; set; }
        public virtual IList<BaseMediaStream> MediaStreams { get; set; } = [];
        public virtual long? RunTimeTicks { get; set; }

        [JsonDefault(ItemTypes.Unknown)]
        public virtual ItemTypes Type { get; set; }
        public virtual string ImageTag { get; set; }
        public virtual string ImageHash { get; set; }
        public virtual string? LocationType { get; set; }
        public virtual IDictionary<string, string>? ProviderIds { get; set; }
        public virtual IList<string> Genres { get; set; } = [];
        public virtual string? Path { get; set; }
        public virtual long Size { get; set; }
        public virtual long Bitrate { get; set; }
        public virtual string? ParentId { get; set; }
        public virtual int? ParentIndex { get; set; }
        public virtual int? Index { get; set; }


    }
}
