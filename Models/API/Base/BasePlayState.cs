namespace Jellystat_API_Core.Models.API.Base
{
    public class BasePlayState
    {
        public virtual bool IsPaused { get; set; } = false;
        public virtual string PlayMethod { get; set; } = "";
        public virtual int SubtitleStreamIndex { get; set; }
        public virtual int AudioStreamIndex { get; set; }
        public virtual long PositionTicks { get; set; } = 0;
    }
}
