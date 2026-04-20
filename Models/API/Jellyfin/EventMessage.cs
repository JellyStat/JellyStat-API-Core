using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.API.Base;

namespace Jellystat_API_Core.Models.API.Jellyfin
{
    public class EventMessage<T> : BaseEventMessage<T>
    {
        public EventMessage(T Data, MessageTypes MessageType)
        {

            this.Data = Data;
            this.MessageType = MessageType;
        }

    }
}
