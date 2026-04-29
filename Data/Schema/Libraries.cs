using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Data.Schema
{
    public class Libraries
    {
        public required string Id { get; set; }
        public required string ServerId { get; set; }
        public required string Name { get; set; }
        public required LibraryTypes Type { get; set; }
        public string? ImageTag { get; set; }
        public string? ImageHash { get; set; }
        public bool archived { get; set; } = false;

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Server? Server { get; set; }
    }
}
