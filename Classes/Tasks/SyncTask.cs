using Jellystat_API_Core.Classes.API.@interface;

namespace Jellystat_API_Core.Classes.Tasks
{
    public class SyncTask
    {

        public iAPI api { get; set; }

        public SyncTask(iAPI api)
        {
            this.api = api;
        }



    }
}
