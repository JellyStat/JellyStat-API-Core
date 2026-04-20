namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseUser
    {
        public virtual string Name { get; set; }
        public virtual string ServerId { get; set; }
        public virtual string Id { get; set; }
        public virtual bool HasPassword { get; set; }
        public virtual bool IsAdministrator { get; set; }
        public virtual string? ImageTag { get; set; }
    }
}
