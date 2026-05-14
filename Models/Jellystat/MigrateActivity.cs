using Jellystat_API_Core.Extensions;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class MigrateActivity
    {
        public required string Id { get; set; }
        public required string ServerId { get; set; }
        public required string SeriesId { get; set; }
        public required string SeasonId { get; set; }
        public required string ItemId { get; set; }

        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";


        [System.Text.Json.Serialization.JsonIgnore]
        public List<ChangeTracker> Changes { get; set; } = [];


        [System.Text.Json.Serialization.JsonIgnore]
        public bool hasChanges => Changes.IsNotEmpty();
    }
}
