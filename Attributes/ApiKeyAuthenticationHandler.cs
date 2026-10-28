using Jellystat_API_Core.Data;
using Jellystat_API_Core.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Jellystat_API_Core.Attributes
{
    public static class ApiKeyDefaults
    {
        public const string Scheme = "ApiKey";
        public const string PolicyScheme = "JwtOrApiKey";
        public const string HeaderName = "x-api-token";
        public const string ApiKeyIdClaim = "apiKeyId";
    }

    /// <summary>
    /// Authenticates requests carrying an API key in the x-api-token header and turns the key into a
    /// principal that looks like an access token, so the existing permission policies apply unchanged.
    /// </summary>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(1);
        private readonly ApplicationDbContext _db;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ApplicationDbContext db) : base(options, logger, encoder)
        {
            _db = db;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
                return AuthenticateResult.NoResult();

            string hash = ApiKeyHelper.Hash(values.ToString().Trim());
            var key = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyHash == hash);
            if (key == null)
                return AuthenticateResult.Fail("Invalid API key");

            if (DateTime.UtcNow - (key.LastUsedAt ?? DateTime.MinValue) > LastUsedGranularity)
            {
                var now = DateTime.UtcNow;
                await _db.ApiKeys.Where(k => k.Id == key.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));
            }

            var claims = new[]
            {
                new Claim("username", key.Name),
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, key.Id),
                new Claim("type", "access"),
                new Claim("permissions", JsonSerializer.Serialize(new[] { key.Permission.ToString() })),
                new Claim("serverId", key.ServerId ?? string.Empty),
                new Claim(ApiKeyDefaults.ApiKeyIdClaim, key.Id)
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}
