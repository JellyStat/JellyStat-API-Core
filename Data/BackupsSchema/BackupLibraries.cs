namespace Jellystat_API_Core.Data.BackupsSchema
{
    public class BackupLibraries
    {
        public required string Id { get; set; }
        public required string ServerId { get; set; }
        public required string Name { get; set; }
        public required int Type { get; set; }
        public string? ImageTag { get; set; }
        public string? ImageHash { get; set; }
        public bool archived { get; set; } = false;
    }
}
