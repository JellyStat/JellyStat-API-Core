namespace Jellystat_API_Core.Data.Schema.Views
{
    public class ItemsWithStats : Items
    {
        public int PlayCount { get; set; }
        public int PlayDuration { get; set; }
        public DateTime? LastPlayedDate { get; set; }
    }
}
