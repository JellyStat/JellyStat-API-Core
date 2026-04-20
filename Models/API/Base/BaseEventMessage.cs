using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.API.Base
{
    public class BaseEventMessage<T>
    {
        //public virtual string? MessageId { get; set; }
        public virtual T? Data { get; set; }

        [JsonDefault(MessageTypes.Unknown)]
        public virtual MessageTypes MessageType { get; set; }

    }
}
