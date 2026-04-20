using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Data.Schema
{
    public class Items
    {
        public required string Name { get; set; }
        public required string ServerId { get; set; }
        public required string Id { get; set; }
        public required string LibraryId { get; set; }
        public required DateTime? DateCreated { get; set; } = DateTime.Now;
        public IList<BaseMediaStream> MediaStreams { get; set; } = [];
        public long Duration { get; set; } = 0;

        [JsonDefault(ItemTypes.Unknown)]
        public ItemTypes Type { get; set; }
        public string ImageTag { get; set; } = "";
        public string ImageHash { get; set; } = "";
        public IList<string> Genres { get; set; } = [];
        public string? Path { get; set; }
        public long Size { get; set; }
        public long Bitrate { get; set; }
        public string? ParentId { get; set; }
        public int? ParentIndex { get; set; }
        public int? Index { get; set; }

        public bool archived { get; set; } = false;


        public Server? Server { get; set; }


        public Libraries? Library { get; set; }
    }
}
