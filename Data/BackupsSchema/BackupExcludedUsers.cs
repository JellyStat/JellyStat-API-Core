namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupExcludedUsers
    {
        public required string ServerId { get; set; }
        public required string UserId { get; set; }
    }
}
