namespace Jellystat_API_Core.Models.Jellystat
{
    public class LoginModel
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public string? ServerId { get; set; }
    }
}
