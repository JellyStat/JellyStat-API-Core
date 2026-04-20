namespace Jellystat_API_Core.Data.Schema
{
    public class ExcludedLibraries
    {
        public required string ServerId { get; set; }
        public required string LibraryId { get; set; }

        public Server? Server { get; set; }
        public Libraries? Library { get; set; }
    }
}
