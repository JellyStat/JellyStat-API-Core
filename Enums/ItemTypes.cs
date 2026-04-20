using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]

    public enum ItemTypes
    {
        Movie,
        Audio,
        Series,
        Season,
        Episode,
        Folder,
        Trailer,
        Unknown
    }
}
