using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseLibrary
    {
        public virtual string Id { get; set; }
        public virtual string ServerId { get; set; }
        public virtual string Name { get; set; }

        [JsonDefault(LibraryTypes.Unknown)]
        public virtual LibraryTypes Type { get; set; }
        public virtual string ImageTag { get; set; }
        public virtual string ImageHash { get; set; }
        public virtual bool archived { get; set; } = false;
    }
}
