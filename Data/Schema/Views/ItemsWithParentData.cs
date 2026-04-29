namespace Jellystat_API_Core.Data.Schema.Views
{
    public class ItemsWithParentData : Items
    {
        public Items? Parent { get; set; }
    }
}
