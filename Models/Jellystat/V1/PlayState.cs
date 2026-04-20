namespace Jellystat_API_Core.Models.Jellystat.V1
{
    public class PlayState
    {
        public long PositionTicks { get; set; }
        public bool CanSeek { get; set; }
        public bool IsPaused { get; set; }
        public bool IsMuted { get; set; }
        public int VolumeLevel { get; set; }
        public int AudioStreamIndex { get; set; }
        public required string MediaSourceId { get; set; }
        public required string PlayMethod { get; set; }
        public required string RepeatMode { get; set; }
        public string? PlaybackOrder { get; set; } = "Default";
    }
}
