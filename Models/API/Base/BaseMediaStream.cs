using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseMediaStream
    {
        public virtual string? Codec { get; set; }
        public virtual string? VideoRange { get; set; }
        public virtual string? DisplayTitle { get; set; }
        public virtual int? BitRate { get; set; }
        public virtual int? Height { get; set; }
        public virtual int? Width { get; set; }
        public virtual double? RealFrameRate { get; set; }
        [JsonDefault(MediaStreamType.Unknown)]
        public virtual MediaStreamType Type { get; set; }
        public virtual string? AspectRatio { get; set; }
        public virtual int? Index { get; set; }
    }
}
