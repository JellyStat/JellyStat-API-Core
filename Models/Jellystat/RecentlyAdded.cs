using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Data.Schema.Views;
using System.Diagnostics.CodeAnalysis;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class RecentlyAdded : ItemsWithParentData
    {
        public List<Items> Grouped { get; set; } = new List<Items>();


        [SetsRequiredMembers]
        public RecentlyAdded(ItemsWithParentData item)
        {

            Name = item.Name;
            ServerId = item.ServerId;
            Id = item.Id;
            LibraryId = item.LibraryId;
            DateCreated = item.DateCreated;
            MediaStreams = item.MediaStreams;
            Duration = item.Duration;
            Type = item.Type;
            ImageTag = item.ImageTag;
            ImageHash = item.ImageHash;
            Genres = item.Genres;
            Path = item.Path;
            Size = item.Size;
            Bitrate = item.Bitrate;
            ParentId = item.ParentId;
            ParentIndex = item.ParentIndex;
            Index = item.Index;
            archived = item.archived;
            Codec = item.Codec;
            Resolution = item.Resolution;
            RootId = item.RootId;
            Parent = item.Parent;
        }

        [SetsRequiredMembers]
        public RecentlyAdded(ItemsWithParentData item, List<Items>? grouped = null)
        {
            Name = item.Name;
            ServerId = item.ServerId;
            Id = item.Id;
            LibraryId = item.LibraryId;
            DateCreated = item.DateCreated;
            MediaStreams = item.MediaStreams;
            Duration = item.Duration;
            Type = item.Type;
            ImageTag = item.ImageTag;
            ImageHash = item.ImageHash;
            Genres = item.Genres;
            Path = item.Path;
            Size = item.Size;
            Bitrate = item.Bitrate;
            ParentId = item.ParentId;
            ParentIndex = item.ParentIndex;
            Index = item.Index;
            archived = item.archived;
            Codec = item.Codec;
            Resolution = item.Resolution;
            RootId = item.RootId;
            Parent = item.Parent;


            if (grouped != null)
            {
                Grouped = grouped;
            }
        }
    }
}
