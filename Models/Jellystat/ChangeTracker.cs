namespace Jellystat_API_Core.Models.Jellystat
{
    public class ChangeTracker
    {
        public required string PropertyName { get; set; }
        public required string OldValue { get; set; }
        public required string NewValue { get; set; }

        public ChangeTracker() { }
    }
}
