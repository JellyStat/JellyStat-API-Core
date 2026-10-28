using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Data.Schema
{
    public class ApiKeys
    {
        public required string Id { get; set; }
        public required string Name { get; set; }

        /// <summary>SHA-256 (hex) of the full key. The key itself is never stored.</summary>
        public required string KeyHash { get; set; }

        /// <summary>First characters of the key, safe to display so keys can be told apart.</summary>
        public required string KeyPrefix { get; set; }

        /// <summary>Administrator = full access, CanView = read only (scoped to ServerId).</summary>
        public PermissionTypes Permission { get; set; } = PermissionTypes.CanView;

        /// <summary>Required for CanView keys; the existing view-only scoping uses the serverId claim.</summary>
        public string? ServerId { get; set; }
        public Server? Server { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}
