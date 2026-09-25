namespace Jellystat_API_Core.Models.Jellystat
{
    public class LocalUser
    {
        public string? Id { get; set; }
        public required string Username { get; set; }
        public string? Password { get; set; }
        public string? ServerId { get; set; }
    }
}
