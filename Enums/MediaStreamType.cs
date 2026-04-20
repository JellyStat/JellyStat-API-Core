using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]

    public enum MediaStreamType
    {
        Main,
        Video,
        Audio,
        Subtitle,
        Unknown
    }
}
