namespace Jellystat_API_Core.Data.Schema
{
    public class Users
    {
        public required string Id { get; set; }
        public required string Username { get; set; }

        public required string ServerId { get; set; }
        public Server? Server { get; set; }

        public string? ImageTag { get; set; }
    }
}
