using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.API.Emby
{
    public class User : BaseUser
    {
        public string? PrimaryImageTag { private get; set; }
        public override string? ImageTag { get => PrimaryImageTag; set => PrimaryImageTag = value; }
    }
}
