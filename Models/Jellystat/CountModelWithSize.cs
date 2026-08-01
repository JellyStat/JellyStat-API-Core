namespace Jellystat_API_Core.Models.Jellystat
{
    public class CountModelWithSize<T> : CountModel<T>
    {
        public long Size { get; set; }
    }
}
