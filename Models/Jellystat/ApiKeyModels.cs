using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class CreateApiKeyModel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Administrator (full access) or CanView (read only). Defaults to CanView.</summary>
        public PermissionTypes Permission { get; set; } = PermissionTypes.CanView;

        /// <summary>Required when Permission is CanView.</summary>
        public string? ServerId { get; set; }
    }

    public class ApiKeyInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public PermissionTypes Permission { get; set; }
        public string? ServerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    /// <summary>Returned once on creation. Key is the only time the full value is visible.</summary>
    public class CreatedApiKey : ApiKeyInfo
    {
        public string Key { get; set; } = string.Empty;
    }
}
