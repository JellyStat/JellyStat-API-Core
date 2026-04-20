using Jellystat_API_Core.Models.API.Base;
using Newtonsoft.Json;

namespace Jellystat_API_Core.Models.API.Emby
{
    public class MediaSources : BaseMediaSources
    {
        [JsonIgnore]
        public override IList<BaseMediaStream> MediaStreams
        {
            get => MediaStreamsInternal.Cast<BaseMediaStream>().ToList();
            set => MediaStreamsInternal = value?.Cast<MediaStream>().ToList() ?? [];
        }

        [JsonProperty("MediaStreams")]
        public List<MediaStream> MediaStreamsInternal { get; set; } = [];
    }
}
