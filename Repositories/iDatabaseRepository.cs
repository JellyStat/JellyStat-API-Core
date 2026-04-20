using Jellystat_API_Core.Data;

namespace Jellystat_API_Core.Repositories
{
    public interface iDatabaseRepository
    {
        public ApplicationDbContext context { get; }
    }
}
