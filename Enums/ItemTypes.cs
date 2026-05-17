
namespace Jellystat_API_Core.Enums
{
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]

    public enum ItemTypes
    {
        Movie,
        Audio,
        Series,
        Season,
        Episode,
        Folder,
        Trailer,
        TvChannel,
        Unknown
    }
}
