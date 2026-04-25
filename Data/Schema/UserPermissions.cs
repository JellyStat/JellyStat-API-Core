namespace Jellystat_API_Core.Data.Schema
{
    public class UserPermissions
    {
        public required int PermissionId { get; set; }
        public required string UserId { get; set; }

        public Permissions? Permissions { get; set; }
        public LocalUsers? User { get; set; }
    }
}
