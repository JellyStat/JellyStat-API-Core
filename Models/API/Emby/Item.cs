using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.API.Base;
using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Models.API.Emby
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
        public override long Size => MediaSources.Where(ms => ms.Id == $"mediasource_{Id}").FirstOrDefault(defaultValue: null)?.Size ?? 0;

        public override long Bitrate => MediaSources.Where(ms => ms.Id == $"mediasource_{Id}").FirstOrDefault(defaultValue: null)?.Bitrate ?? 0;

        public override string ImageTag => ImageTags?.Primary ?? "";
        public override string ImageHash => ImageBlurHashes?.Primary?.ContainsKey(ImageTag) == true ? ImageBlurHashes.Primary[ImageTag] : "";


        public override string? ParentId => Type == ItemTypes.Season ? SeriesId : (Type == ItemTypes.Episode ? SeasonId : null);
        public override int? ParentIndex => Type == ItemTypes.Episode ? ParentIndexNumber : null;
        public override int? Index => Type == ItemTypes.Season || Type == ItemTypes.Episode ? IndexNumber : null;
        public override long? RunTimeTicks => MediaSources.Where(ms => ms.Id == $"mediasource_{Id}").FirstOrDefault(defaultValue: null)?.RunTimeTicks ?? 0;

        public override string Codec => MediaStreams.Where(ms => ms.Type == MediaStreamType.Video || (Type == ItemTypes.Audio && ms.Type == MediaStreamType.Audio)).FirstOrDefault(defaultValue: null)?.Codec ?? "";
        public override string Resolution
        {
            get
            {
                string resolution = "";
                BaseMediaStream? videoStream = MediaStreams.Where(ms => ms.Type == MediaStreamType.Video).FirstOrDefault(defaultValue: null);
                string? displayTitle = videoStream?.DisplayTitle;
                if (string.IsNullOrWhiteSpace(displayTitle)) return resolution;

                List<string> displayTitleParts = Regex.Replace(displayTitle, @"[()]", "").Split(" ").Where(p => p != "-").ToList();

                string strictPattern = @"^\d+[iIpPkK]$";
                resolution = displayTitleParts.Where(input =>
                                                    !string.IsNullOrEmpty(input) &&
                                                     Regex.IsMatch(input, strictPattern)
                                                ).FirstOrDefault("");
                return resolution;
            }
        }

    }
}
