using Jellystat_API_Core.Data;

namespace Jellystat_API_Core.Repositories
{
    public class DatabaseRepository : iDatabaseRepository
    {


        public ApplicationDbContext context { get; }

        public DatabaseRepository(ApplicationDbContext _context)
        {
            context = _context;
        }


    }
}
