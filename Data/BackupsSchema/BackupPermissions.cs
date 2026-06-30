namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupPermissions
    {
        public int Id { get; set; }
        public required string PermissionKey { get; set; }
        public required string Description { get; set; }
        public bool Active { get; set; }
    }
}
