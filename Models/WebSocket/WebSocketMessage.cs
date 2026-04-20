namespace Jellystat_API_Core.Models.WebSocket
{
    public class WebSocketMessage
    {
        public string ClientId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public bool IsBinary { get; set; }
        public byte[]? BinaryData { get; set; }
    }
}
