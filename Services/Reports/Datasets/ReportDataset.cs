using Jellystat_API_Core.Data;
using System.Reflection;

namespace Jellystat_API_Core.Services.Reports.Datasets
{
    /// <summary>Who a report runs for: the server it reads, and the one media user it is limited to (null = all users).</summary>
    public record ReportScope(string ServerId, string? ScopedUserId);

    public interface IReportDataset
    {
        string Key { get; }
        string Label { get; }
        string Description { get; }
        Type RowType { get; }
        IReadOnlyList<ReportFieldInfo> Fields { get; }
        PropertyInfo? GetProperty(string field);
        IQueryable Query(ApplicationDbContext db, ReportScope scope);
    }

    /// <summary>
    /// A flat, report-friendly view of Jellystat data. TRow's [ReportField] properties are the dataset's fields;
    /// Build returns the EF Core query (already limited to the scope) that every report query starts from.
    /// </summary>
    public abstract class ReportDataset<TRow> : IReportDataset where TRow : class
    {
        private readonly Dictionary<string, PropertyInfo> _properties;

        protected ReportDataset()
        {
            _properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            var fields = new List<ReportFieldInfo>();
            foreach (var prop in typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<ReportFieldAttribute>();
                if (attr == null) continue;

                _properties[prop.Name] = prop;
                Type underlying = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                fields.Add(new ReportFieldInfo
                {
                    Key = prop.Name,
                    Label = attr.Label,
                    Type = attr.Type,
                    Groupable = attr.Groupable,
                    Aggregatable = attr.Aggregatable,
                    Nullable = !prop.PropertyType.IsValueType || System.Nullable.GetUnderlyingType(prop.PropertyType) != null,
                    Description = attr.Description,
                    Options = underlying.IsEnum ? Enum.GetNames(underlying).ToList() : null
                });
            }
            Fields = fields;
        }

        public abstract string Key { get; }
        public abstract string Label { get; }
        public abstract string Description { get; }
        public Type RowType => typeof(TRow);
        public IReadOnlyList<ReportFieldInfo> Fields { get; }

        public PropertyInfo? GetProperty(string field) => _properties.GetValueOrDefault(field);

        public IQueryable Query(ApplicationDbContext db, ReportScope scope) => Build(db, scope);

        protected abstract IQueryable<TRow> Build(ApplicationDbContext db, ReportScope scope);
    }
}
