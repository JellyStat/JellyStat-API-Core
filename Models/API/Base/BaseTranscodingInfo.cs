namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseTranscodingInfo
    {
        public virtual string VideoCodec { get; set; }
        public virtual string AudioCodec { get; set; }
        public virtual string Container { get; set; }
        public virtual bool IsVideoDirect { get; set; }
        public virtual bool IsAudioDirect { get; set; }
        public virtual int Bitrate { get; set; }
        public virtual double CompletionPercentage { get; set; }
        public virtual int Width { get; set; }
        public virtual int Height { get; set; }
        public virtual string[] TranscodeReasons { get; set; }
    }
}
