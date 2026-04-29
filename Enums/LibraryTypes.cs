
using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Enums
{
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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
