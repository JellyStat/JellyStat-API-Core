using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.API.Emby
{
    public class Library : BaseLibrary
    {
        public string? CollectionType { private get; set; }

        public override LibraryTypes Type => CollectionType?.ToLowerInvariant() switch
        {
            "tvshows" => LibraryTypes.Series,
            "movies" => LibraryTypes.Movies,
            "music" => LibraryTypes.Music,
            "homevideos" => LibraryTypes.HomeVideos,
            "musicvideos" => LibraryTypes.MusicVideos,
            "books" => LibraryTypes.Books,
            "boxsets" => LibraryTypes.BoxSets,
            "mixed" => LibraryTypes.Mixed,
            _ => LibraryTypes.Unknown
        };


        public ImageTags? ImageTags { private get; set; }
        public ImageBlurHashes? ImageBlurHashes { private get; set; }

        public override string ImageTag => ImageTags?.Primary ?? "";
        public override string ImageHash => ImageBlurHashes?.Primary?.ContainsKey(ImageTag) == true ? ImageBlurHashes.Primary[ImageTag] : "";


    }
}
