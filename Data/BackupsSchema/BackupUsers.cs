namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupUsers
    {
        public required string Id { get; set; }
        public required string Username { get; set; }
        public required string ServerId { get; set; }
        public string? ImageTag { get; set; }
    }
}
