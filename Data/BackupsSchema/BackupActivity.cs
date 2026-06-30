namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupActivity
    {
        public required string Id { get; set; }
        public required string ServerId { get; set; }
        public required string Name { get; set; }
        public string? SeriesName { get; set; }
        public required string UserId { get; set; }
        public required string UserName { get; set; }
        public string? Client { get; set; }
        public string? Device { get; set; }
        public string? DeviceId { get; set; }
        public required string ItemId { get; set; }
        public string? SeriesId { get; set; }
        public string? SeasonId { get; set; }
        public long Duration { get; set; }
        public bool DirectPlay { get; set; }
        public string? PlayMethod { get; set; }
        public required string MediaStreams { get; set; }
        public string? TranscodingInfo { get; set; }
        public bool Imported { get; set; }
        public DateTime DateCreated { get; set; }
        public string? Container { get; set; }
        public required string IpAddress { get; set; }
        public long RuntimeTicks { get; set; }
    }
}
