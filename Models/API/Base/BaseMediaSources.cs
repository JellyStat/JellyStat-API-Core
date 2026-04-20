namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseMediaSources
    {
        public virtual string? Id { get; set; }
        public virtual string? Path { get; set; }
        public virtual string? Type { get; set; }
        public virtual string? Container { get; set; }
        public virtual long? Size { get; set; }
        public virtual string? Name { get; set; }
        public virtual string? ETag { get; set; }
        public virtual long? RunTimeTicks { get; set; }
        public virtual IList<BaseMediaStream> MediaStreams { get; set; } = [];
        public virtual int? Bitrate { get; set; }
    }
}
