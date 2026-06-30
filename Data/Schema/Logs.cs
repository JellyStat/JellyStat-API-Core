using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Data.Schema
{
    public class Logs
    {
        public string Id { get; set; }
        public string? ServerId { get; set; }
        public TaskTypes Task { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public float Duration { get; set; } = 0;
        public List<string> Messages { get; set; } = new List<string>();
        public bool? Success { get; set; }

    }
}
