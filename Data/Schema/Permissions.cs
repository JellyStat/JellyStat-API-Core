
namespace Jellystat_API_Core.Data.Schema
{
    public class Permissions
    {

        public int Id { get; set; }

        public string PermissionKey { get; set; }

        public string Description { get; set; } = "";

        public bool Active { get; set; } = true;


    }
}
