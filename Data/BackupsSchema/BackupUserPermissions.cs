namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupUserPermissions
    {
        public int PermissionId { get; set; }
        public required string UserId { get; set; }
    }

}
