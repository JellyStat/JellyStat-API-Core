using Jellystat_API_Core.Models.API.Base;
using Newtonsoft.Json;

namespace Jellystat_API_Core.Models.API.Emby
{
    public class SessionItem : BaseSessionItem
    {

        [JsonProperty("NowPlayingItem")]
        public NowPlayingItem? NowPlayingItemInternal { private get; set; }

        [JsonProperty("TranscodingInfo")]
        public TranscodingInfo? TranscodingInfoInternal { private get; set; }


        [JsonIgnore]
        public override BaseNowPlayingItem? NowPlayingItem
        {
            get => NowPlayingItemInternal;
            set => NowPlayingItemInternal = value as NowPlayingItem;
        }

        [JsonIgnore]
        public override BaseTranscodingInfo? TranscodingInfo
        {
            get => TranscodingInfoInternal;
            set => TranscodingInfoInternal = value as TranscodingInfo;

        }

        public PlayState? PlayState { private get; set; }

        public override bool Paused
        {
            get => PlayState?.IsPaused ?? false;
            set
            {
                PlayState ??= new PlayState();
                PlayState.IsPaused = value;
            }
        }

        public override bool IsDirectPlay
        {
            get => PlayState?.PlayMethod == "DirectPlay";
            set
            {
                PlayState ??= new PlayState();
                PlayState.PlayMethod = value ? "DirectPlay" : "Transcode";
            }
        }

        public string? RemoteEndPoint { private get; set; }

        public override string IPAddress { get => RemoteEndPoint ?? ""; set => RemoteEndPoint = value; }

        public string? UserPrimaryImageTag { private get; set; }

        public override string? UserImageTag { get => UserPrimaryImageTag; set => UserPrimaryImageTag = value; }



    }
}
