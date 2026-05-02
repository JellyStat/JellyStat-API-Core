namespace Jellystat_API_Core.Data.Schema
{
    public class ItemsPath
    {
        public string AncestorId { get; set; }
        public string DescendantId { get; set; }
        public int Depth { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Items? Ancestor { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Items? Descendant { get; set; }
    }
}
