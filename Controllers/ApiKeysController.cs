using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellystat_API_Core.Controllers
{
    /// <summary>
    /// Manage API keys for external access. Requires an administrator signed in with a normal token:
    /// API keys cannot create or revoke other API keys, so a leaked key can't mint replacements.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiKeysController : ControllerBase
    {
        private readonly ApplicationDbContext context;
        private readonly IAPIInstanceManager _apiInstanceManager;

        public ApiKeysController(ApplicationDbContext context, IAPIInstanceManager apiInstanceManager)
        {
            this.context = context;
            _apiInstanceManager = apiInstanceManager;
        }

        private bool IsApiKeyCaller => HttpContext.User.HasClaim(c => c.Type == ApiKeyDefaults.ApiKeyIdClaim);

        private static ApiKeyInfo ToInfo(ApiKeys k) => new()
        {
            Id = k.Id,
            Name = k.Name,
            KeyPrefix = k.KeyPrefix,
            Permission = k.Permission,
            ServerId = k.ServerId,
            CreatedAt = k.CreatedAt,
            LastUsedAt = k.LastUsedAt,
            CreatedBy = k.CreatedBy
        };

        [HttpGet("List", Name = "ListApiKeys")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<List<ApiKeyInfo>>> List()
        {
            if (IsApiKeyCaller) return Forbid();

            var keys = await context.ApiKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync();
            return keys.Select(ToInfo).ToList();
        }

        [HttpPost("Create", Name = "CreateApiKey")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<CreatedApiKey>> Create([FromBody] CreateApiKeyModel model)
        {
            if (IsApiKeyCaller) return Forbid();

            string name = (model.Name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 100)
                return BadRequest("A key name of 1-100 characters is required");

            if (model.Permission != PermissionTypes.Administrator && model.Permission != PermissionTypes.CanView)
                return BadRequest("Permission must be Administrator or CanView");

            string? serverId = null;
            if (model.Permission == PermissionTypes.CanView)
            {
                // Read-only keys use the existing view-only scoping, which is tied to one server.
                if (string.IsNullOrWhiteSpace(model.ServerId))
                    return BadRequest("A ServerId is required for read-only keys");
                if (_apiInstanceManager.GetInstance(model.ServerId) == null)
                    return BadRequest("Unknown ServerId");
                serverId = model.ServerId;
            }

            if (await context.ApiKeys.AnyAsync(k => k.Name.ToLower() == name.ToLower()))
                return BadRequest("A key with this name already exists");

            string key = ApiKeyHelper.Generate();
            var entity = new ApiKeys
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                KeyHash = ApiKeyHelper.Hash(key),
                KeyPrefix = ApiKeyHelper.DisplayPrefix(key),
                Permission = model.Permission,
                ServerId = serverId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = HttpContext.User.GetUsername()
            };

            context.ApiKeys.Add(entity);
            await context.SaveChangesAsync();

            var info = ToInfo(entity);
            return Ok(new CreatedApiKey
            {
                Id = info.Id,
                Name = info.Name,
                KeyPrefix = info.KeyPrefix,
                Permission = info.Permission,
                ServerId = info.ServerId,
                CreatedAt = info.CreatedAt,
                LastUsedAt = info.LastUsedAt,
                CreatedBy = info.CreatedBy,
                Key = key
            });
        }

        [HttpDelete("Delete", Name = "DeleteApiKey")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> Delete(string Id)
        {
            if (IsApiKeyCaller) return Forbid();

            int removed = await context.ApiKeys.Where(k => k.Id == Id).ExecuteDeleteAsync();
            return removed == 0 ? NotFound("API key not found") : Ok();
        }
    }
}
