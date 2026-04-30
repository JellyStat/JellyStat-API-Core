using Jellystat_API_Core.Data.Schema;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class TrackedLibraries : Libraries
    {
        public required bool Tracked { get; set; }
    }
}
