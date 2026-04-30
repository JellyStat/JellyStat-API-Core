using Jellystat_API_Core.Data.Schema;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class TrackedUsers : Users
    {
        public required bool Tracked { get; set; }
    }
}
