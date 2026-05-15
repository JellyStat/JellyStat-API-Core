using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.WebSocket
{
    public class JellystatWebsocketMessage<T>
    {
        public WebsocketServerTypes Type { get; set; }
        public required T Data { get; set; }

        public JellystatWebsocketMessage()
        {
        }
    }
}
