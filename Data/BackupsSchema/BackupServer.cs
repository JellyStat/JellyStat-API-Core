namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupServer
    {
        public required string Id { get; set; }
        public required string URL { get; set; }
        public required string ExternalURL { get; set; }
        public required string APIKey { get; set; }
        public required string Name { get; set; }
        public int Type { get; set; }
        public bool AllowRemoteAuth { get; set; }
        public required string TaskSettings { get; set; }
    }
}

