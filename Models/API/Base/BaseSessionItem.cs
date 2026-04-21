namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseSessionItem
    {
        public virtual string Id { get; set; }
        public virtual string ServerId { get; set; }
        public virtual bool Paused { get; set; } = false;
        public virtual string UserId { get; set; }
        public virtual string UserName { get; set; }
        public virtual string? UserImageTag { get; set; }
        public virtual string Client { get; set; }
        public virtual string DeviceId { get; set; }
        public virtual string DeviceName { get; set; }
        public virtual BaseNowPlayingItem? NowPlayingItem { get; set; }
        public virtual string ApplicationVersion { get; set; }
        public virtual BaseTranscodingInfo TranscodingInfo { get; set; }

        public virtual bool IsPaused { get => NowPlayingItem != null && Paused; }
        public virtual bool IsDirectPlay { get; set; } = true;
        public virtual string IPAddress { get; set; } = "";
        public virtual DateTime? LastPausedDate { get; set; }


    }
}
