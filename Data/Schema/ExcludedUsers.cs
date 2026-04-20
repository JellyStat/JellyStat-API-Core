namespace Jellystat_API_Core.Data.Schema
{
    public class ExcludedUsers
    {
        public required string ServerId { get; set; }
        public required string UserId { get; set; }

        public Server? Server { get; set; }
        public Users? User { get; set; }
    }
}
