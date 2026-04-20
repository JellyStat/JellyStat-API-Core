using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.Jellystat.V1
{
    public class TranscodingInfo
    {
        public string? AudioCodec { get; set; }
        public string? VideoCodec { get; set; }
        public string? Container { get; set; }
        public bool IsVideoDirect { get; set; }
        public bool IsAudioDirect { get; set; }
        public int Bitrate { get; set; }
        //public int Framerate { get; set; }
        public double CompletionPercentage { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int AudioChannels { get; set; }
        public string? HardwareAccelerationType { get; set; }
        public List<string> TranscodeReasons { get; set; } = [];

        public BaseTranscodingInfo ConvertData()
        {
            return new BaseTranscodingInfo
            {
                AudioCodec = AudioCodec ?? string.Empty,
                VideoCodec = VideoCodec ?? string.Empty,
                Container = Container ?? string.Empty,
                IsVideoDirect = IsVideoDirect,
                IsAudioDirect = IsAudioDirect,
                Bitrate = Bitrate,
                CompletionPercentage = CompletionPercentage,
                Width = Width,
                Height = Height,
                TranscodeReasons = TranscodeReasons.ToArray()
            };
        }
    }
}
