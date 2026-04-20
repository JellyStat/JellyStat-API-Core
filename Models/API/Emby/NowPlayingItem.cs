using Jellystat_API_Core.Models.API.Base;
using Newtonsoft.Json;

namespace Jellystat_API_Core.Models.API.Emby
{
    public class NowPlayingItem : BaseNowPlayingItem
    {

        [JsonProperty("MediaStreams")]
        public List<MediaStream> MediaStreamsInternal { get; set; } = [];
        public ImageTags? ImageTags { private get; set; }
        public ImageBlurHashes? ImageBlurHashes { private get; set; }

        [JsonIgnore]
        public override IList<BaseMediaStream> MediaStreams
        {
            get => MediaStreamsInternal.Cast<BaseMediaStream>().ToList();
            set => MediaStreamsInternal = value?.Cast<MediaStream>().ToList() ?? [];
        }
        public override string ImageTag => ImageTags?.Primary ?? "";
        public override string ImageHash => ImageBlurHashes?.Primary?.ContainsKey(ImageTag) == true ? ImageBlurHashes.Primary[ImageTag] : "";

        public Dictionary<string, string>? ProviderIds { get; set; }

        public override IList<string> Providers
        {
            get => ProviderIds?.Keys.ToList() ?? [];
        }

    }
}
