namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseImageBlurHashes
    {
        public virtual IDictionary<string, string>? Backdrop { get; set; }
        public virtual IDictionary<string, string>? Primary { get; set; }
        public virtual IDictionary<string, string>? Logo { get; set; }
        public virtual IDictionary<string, string>? Thumb { get; set; }
    }
}
