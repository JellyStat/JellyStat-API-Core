using Jellystat_API_Core.Data.Schema;

namespace Jellystat_API_Core.Models.Jellystat.V1
{
    public class ActivityV1
    {
        public required string Id { get; set; }
        public bool IsPaused { get; set; }
        public required string UserId { get; set; }
        public required string UserName { get; set; }
        public required string Client { get; set; }
        public required string DeviceName { get; set; }
        public required string DeviceId { get; set; }
        public required string ApplicationVersion { get; set; }
        public required string NowPlayingItemId { get; set; }
        public required string NowPlayingItemName { get; set; }
        public string? SeasonId { get; set; }
        public string? SeriesName { get; set; }
        public string? EpisodeId { get; set; }
        public int PlaybackDuration { get; set; }
        public DateTime ActivityDateInserted { get; set; }
        public string? PlayMethod { get; set; }
        public List<MediaStream>? MediaStreams { get; set; } = [];
        public TranscodingInfo? TranscodingInfo { get; set; }
        public string? OriginalContainer { get; set; }
        public string? RemoteEndPoint { get; set; }
        public string? ServerId { get; set; }
        public bool Imported { get; set; }



        public Activity ConvertData()
        {
            return new Activity
            {
                Id = Id,
                ServerId = ServerId ?? "",
                Name = NowPlayingItemName,
                SeriesName = SeriesName,
                UserId = UserId,
                UserName = UserName,
                Client = Client,
                Device = DeviceName,
                DeviceId = DeviceId,
                ItemId = EpisodeId != null ? EpisodeId : NowPlayingItemId,
                SeriesId = EpisodeId != null ? NowPlayingItemId : null,
                SeasonId = SeasonId,
                Duration = PlaybackDuration,
                DirectPlay = PlayMethod == "DirectPlay" || PlayMethod == "DirectStream",
                MediaStreams = MediaStreams?.Select(ms => ms.ConvertData()).ToList() ?? [],
                TranscodingInfo = TranscodingInfo?.ConvertData(),
                Imported = Imported,
                DateCreated = ActivityDateInserted,
                Container = OriginalContainer,
                IpAddress = RemoteEndPoint ?? "",
                RuntimeTicks = 0
            };


        }

    }
}
