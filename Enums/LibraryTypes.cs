using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LibraryTypes
    {
        [JsonPropertyName("TV Shows")]
        Series,
        BoxSets,
        Movies,
        Music,
        [JsonPropertyName("Home Videos")]
        HomeVideos,
        [JsonPropertyName("Music Videos")]
        MusicVideos,
        Books,
        Mixed,
        Unknown
    }
}
