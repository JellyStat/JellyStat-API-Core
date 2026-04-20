namespace Jellystat_API_Core.Models.API.Base
{
    public class BasePlayState
    {
        public virtual bool IsPaused { get; set; } = false;
        public virtual string PlayMethod { get; set; } = "";
    }
}
