using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.Jellystat.V1
{
    public class MediaStream
    {
        public string? Codec { get; set; }
        public string? CodecTag { get; set; }
        public string? Language { get; set; }
        public string? ColorSpace { get; set; }
        public string? ColorTransfer { get; set; }
        public string? ColorPrimaries { get; set; }
        public string? TimeBase { get; set; }
        public string? Title { get; set; }
        public string? VideoRange { get; set; }
        public string? VideoRangeType { get; set; }
        public string? AudioSpatialFormat { get; set; }
        public string? DisplayTitle { get; set; }
        public bool? IsInterlaced { get; set; }
        public bool? IsAVC { get; set; }
        public string? ChannelLayout { get; set; }
        public int? BitRate { get; set; }
        public int? BitDepth { get; set; }
        public int? RefFrames { get; set; }
        public bool? IsDefault { get; set; }
        public bool? IsForced { get; set; }
        public bool? IsHearingImpaired { get; set; }
        public int? Height { get; set; }
        public int? Width { get; set; }
        public double? AverageFrameRate { get; set; }
        public double? RealFrameRate { get; set; }
        public double? ReferenceFrameRate { get; set; }
        public string? Profile { get; set; }
        public string? Type { get; set; }
        public string? AspectRatio { get; set; }
        public int? Index { get; set; }
        public bool? IsExternal { get; set; }
        public bool? IsTextSubtitleStream { get; set; }
        public bool? SupportsExternalStream { get; set; }
        public string? PixelFormat { get; set; }
        public double? Level { get; set; }
        public bool? IsAnamorphic { get; set; }
        public int? Channels { get; set; }
        public int? SampleRate { get; set; }
        public string? LocalizedUndefined { get; set; }
        public string? LocalizedDefault { get; set; }
        public string? LocalizedForced { get; set; }
        public string? LocalizedExternal { get; set; }
        public string? LocalizedHearingImpaired { get; set; }


        public BaseMediaStream ConvertData()
        {
            return new BaseMediaStream
            {
                Codec = Codec,
                VideoRange = VideoRange,
                DisplayTitle = DisplayTitle,
                BitRate = BitRate,
                Height = Height,
                Width = Width,
                RealFrameRate = RealFrameRate,
                Type = Type switch
                {
                    "Video" => MediaStreamType.Video,
                    "Audio" => MediaStreamType.Audio,
                    "Subtitle" => MediaStreamType.Subtitle,
                    _ => MediaStreamType.Unknown
                },
                AspectRatio = AspectRatio,
                Index = Index ?? 0
            };
        }
    }
}
