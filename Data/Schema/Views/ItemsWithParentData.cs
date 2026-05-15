namespace Jellystat_API_Core.Data.Schema.Views
{
    public class ItemsWithParentData : Items
    {
        public string? RootId { get; set; }
        public Items? Parent { get; set; }
    }
}
