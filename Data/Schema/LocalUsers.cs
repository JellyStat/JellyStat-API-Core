namespace Jellystat_API_Core.Data.Schema
{
    public class LocalUsers
    {
        public required string Id { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
        public string? ServerId { get; set; }
        public string? ImageTag { get; set; }
    }
}
