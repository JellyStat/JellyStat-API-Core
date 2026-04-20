namespace Jellystat_API_Core.Models.API.Base
{
    public class BasePaginatedResponse<T>
    {
        public virtual IList<T> Items { get; set; } = new List<T>();

        public virtual int TotalRecordCount { get; set; }
        public virtual int StartIndex { get; set; }
    }
}
