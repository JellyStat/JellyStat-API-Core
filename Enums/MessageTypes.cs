using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Jellystat_API_Core.Enums
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageTypes
    {
        Sessions,
        SessionsStart,
        ForceKeepAlive,
        KeepAlive,
        Unknown
    }
}
