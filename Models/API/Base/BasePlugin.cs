namespace Jellystat_API_Core.Models.API.Base
{
    public class BasePlugin
    {
        public virtual string Id { get; set; }
        public virtual string Name { get; set; }
        public virtual string Version { get; set; }
        public virtual string? Description { get; set; }
        public virtual string? ConfigurationFileName { get; set; }
    }
}
