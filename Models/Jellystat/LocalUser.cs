namespace Jellystat_API_Core.Models.Jellystat
{
    public class LocalUser
    {
        public string? OldUsername { get; set; }
        public required string Username { get; set; }
        public string? OldPassword { get; set; }
        public string? Password { get; set; }
    }
}
