using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.API.Jellyfin
{
    public class Item : BaseItem
    {
        public IList<MediaSources> MediaSources { private get; set; } = [];
        public string? SeriesId { private get; set; }
        public string? SeasonId { private get; set; }
        public int? ParentIndexNumber { private get; set; }
        public int? IndexNumber { private get; set; }
        public ImageTags? ImageTags { private get; set; }
        public ImageBlurHashes? ImageBlurHashes { private get; set; }
        public override IList<BaseMediaStream> MediaStreams => MediaSources.Where(ms => ms.MediaStreams.IsNotEmpty()).SelectMany(ms => ms.MediaStreams).Cast<BaseMediaStream>().ToList();
        public override string? Path { get; set; }
        public override long Size => MediaSources.Where(ms => ms.Id == Id).FirstOrDefault(defaultValue: null)?.Size ?? 0;

        public override long Bitrate => MediaSources.Where(ms => ms.Id == Id).FirstOrDefault(defaultValue: null)?.Bitrate ?? 0;

        public override string ImageTag => ImageTags?.Primary ?? "";
        public override string ImageHash => ImageBlurHashes?.Primary?.ContainsKey(ImageTag) == true ? ImageBlurHashes.Primary[ImageTag] : "";


        public override string? ParentId => Type == ItemTypes.Season ? SeriesId : (Type == ItemTypes.Episode ? SeasonId : null);
        public override int? ParentIndex => Type == ItemTypes.Episode ? ParentIndexNumber : null;
        public override int? Index => Type == ItemTypes.Season || Type == ItemTypes.Episode ? IndexNumber : null;
        public override long? RunTimeTicks => MediaSources.Where(ms => ms.Id == Id).FirstOrDefault(defaultValue: null)?.RunTimeTicks ?? 0;
        public override string Codec => MediaStreams.Where(ms => ms.Type == MediaStreamType.Video).FirstOrDefault(defaultValue: null)?.Codec ?? "";
        public override string Resolution
        {
            get
            {
                string resolution = "";
                BaseMediaStream? videoStream = MediaStreams.Where(ms => ms.Type == MediaStreamType.Video).FirstOrDefault(defaultValue: null);
                if (videoStream == null) return resolution;

                int width = videoStream.Width ?? 0;
                int height = videoStream.Height ?? 0;
                string? aspectRatio = videoStream.AspectRatio;

                resolution = VideoMetadataParser.GetResolutionLabel(width, height, aspectRatio);
                return resolution;
            }
        }





    }
}
