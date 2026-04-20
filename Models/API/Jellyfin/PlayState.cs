using Jellystat_API_Core.Models.API.Base;
using Newtonsoft.Json;

namespace Jellystat_API_Core.Models.API.Jellyfin
{
    public class PlayState : BasePlayState
    {

        [JsonProperty("PlayMethod")]
        public string? PlayMethodInternal { get; set; }


        [JsonIgnore]
        public override string? PlayMethod
        {
            get => PlayMethodInternal;
            set => PlayMethodInternal = value;
        }
    }
}
