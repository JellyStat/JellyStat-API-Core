using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Helpers;
using Jellystat_API_Core.Models.Jellystat;
using Jellystat_API_Core.Services.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;

namespace Jellystat_API_Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext context;

        // Verified against when the username doesn't exist, so a wrong username takes as long as a wrong
        // password and response timing can't be used to find valid usernames. Same default work factor as real hashes.
        private static readonly string TimingEqualiserHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
        private readonly IAPIInstanceManager _apiInstanceManager;

        public AuthController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {
            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        /// <summary>
        /// True when the caller authenticated with an API key. Local user management is limited to signed-in
        /// administrators, so a leaked key can't create or take over a login that outlives revoking the key.
        /// </summary>
        private bool IsApiKeyCaller => HttpContext.User.HasClaim(c => c.Type == ApiKeyDefaults.ApiKeyIdClaim);

        /// <summary>Media server user id for a remote login, looked up once at login/refresh rather than per request.</summary>
        private Task<string?> FindServerUserIdAsync(string serverId, string username)
        {
            string usernameLower = username.ToLower();
            return context.Users
                .Where(u => u.ServerId == serverId && u.Username.ToLower() == usernameLower)
                .Select(u => (string?)u.Id)
                .FirstOrDefaultAsync();
        }

        private Object generateTokens(string Username, List<Permissions> permissions, string? ServerId, string? UserId = null)
        {
            var claims = new List<Claim>
           {
                new Claim("username", Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("type", "access"),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) ),
                new Claim("serverId", ServerId ?? string.Empty)
            };
            if (UserId != null) claims.Add(new Claim(ClaimsPrincipalExtensions.UserIdClaim, UserId));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Global.JwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: Global.JwtIssuer,
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds);

            claims = new List<Claim>
           {
                new Claim("username", Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) ),
                new Claim("type", "refresh"),
                new Claim("serverId", ServerId ?? string.Empty)
            };
            if (UserId != null) claims.Add(new Claim(ClaimsPrincipalExtensions.UserIdClaim, UserId));


            var refreshToken = new JwtSecurityToken(
                issuer: Global.JwtIssuer,
                claims: claims,
                expires: DateTime.Now.AddDays(7),
                signingCredentials: creds);

            return new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                refreshToken = new JwtSecurityTokenHandler().WriteToken(refreshToken)
            };
        }

        [HttpGet("Config", Name = "Config")]
        public ActionResult<List<Models.Jellystat.Server>> GetConfig()
        {
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true && HttpContext.User.IsAccessToken();

            List<iAPI> instances = _apiInstanceManager.GetAllInstances().ToList();
            // Deep-clone the server configs so changes to the returned list won't affect the original instances
            // Use ObjectCreationHandling.Replace so existing collection initializers on the model aren't populated into
            // (which would cause duplicate TaskSettings entries) — instead the collections are replaced with the deserialized ones.
            var jsonSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
            List<Models.Jellystat.Server> servers = instances
                .Select(i => JsonConvert.DeserializeObject<Models.Jellystat.Server>(JsonConvert.SerializeObject(i.config), jsonSettings))
                .Where(s => s != null)
                .Select(s => s!)
                .ToList();

            if (!Authorized)
            {
                foreach (Models.Jellystat.Server server in servers)
                {
                    server.ExternalURL = string.Empty;
                    server.URL = string.Empty;
                    server.TaskSettings = new List<TaskSettings>();
                }
            }
            return servers;
        }

        [HttpGet("/System/Info", Name = "/System/Info")]
        public async Task<ActionResult<SystemInfoModel>> GetSystemInfo()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.0.0.0";

            SystemInfoModel info = new SystemInfoModel(SystemState.Unconfigured, version);

            bool hasLocalUsers = await context.LocalUsers.CountAsync() > 0;


            ////find io file tag
            List<string> filePaths = Directory.Exists(Global.WebUIDirectory)
                ? Directory
                    .EnumerateFiles(Global.WebUIDirectory, "*.emtag", SearchOption.TopDirectoryOnly)
                    .ToList()
                : new List<string>();

            if (filePaths.Count > 0)
            {
                info.UITag = Path.GetFileNameWithoutExtension(filePaths.First());
            }
            ///


            if (hasLocalUsers)
            {
                info.State = SystemState.FirstUserCreated;

                bool hasConfiguredServers = await context.Servers.CountAsync() > 0;

                if (hasConfiguredServers)
                {
                    info.State = SystemState.Configured;
                }
            }



            return info;

        }


        [EnableRateLimiting("login")]
        [HttpPost("Login", Name = "Login")]
        public async Task<ActionResult> Login([FromBody] LoginModel loginModel)
        {
            if (loginModel == null)
            {
                return BadRequest("Invalid login data");
            }



            try
            {

                if (loginModel.ServerId == null)
                {
                    LocalUsers? localUsers = await context.LocalUsers.Where(u => u.Username == loginModel.Username).FirstOrDefaultAsync();

                    if (localUsers == null)
                    {
                        BCrypt.Net.BCrypt.Verify(loginModel.Password, TimingEqualiserHash);
                    }
                    else
                    {

                        if (BCrypt.Net.BCrypt.Verify(loginModel.Password, localUsers.Password))
                        {
                            List<Permissions> permissions = await context.Permissions.Where(p => context.UserPermissions.Where(up => up.UserId == localUsers.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();

                            var credentials = generateTokens(localUsers.Username, permissions, localUsers.ServerId);

                            return Ok(credentials);
                        }
                    }

                    return Unauthorized("Invalid username or password");
                }

                iAPI? instance = Global.APIInstanceManager.GetInstance(loginModel.ServerId!);
                if (instance == null)
                {
                    return Unauthorized("Invalid server ID");
                }

                if (instance.config.AllowRemoteAuth == false)
                {
                    return Unauthorized("Remote authentication is not allowed for this server");
                }

                bool authTrue = await instance.AuthenticateByUsername(loginModel.Username, loginModel.Password);

                if (!authTrue)
                {
                    return Unauthorized("Invalid username or password");
                }

                // Remote logins are limited to their own data, so the token needs their media server user id
                string? remoteUserId = await FindServerUserIdAsync(loginModel.ServerId!, loginModel.Username);
                if (remoteUserId == null)
                {
                    return Unauthorized("Your account hasn't been synced to Jellystat yet. Try again after the next sync.");
                }

                List<Permissions> viewPermissions = new() { new Permissions { PermissionKey = PermissionTypes.CanView.GetDisplayName() } };

                var remoteCredentials = generateTokens(loginModel.Username, viewPermissions, loginModel.ServerId!, remoteUserId);

                return Ok(remoteCredentials);

            }
            catch (Exception ex)
            {

                Debug.WriteLine(ex);

                return StatusCode(500, "An error occurred while processing your request.");

            }


        }

        [Authorize(Policy = "RefreshTokenOnly")]
        [HttpGet("Refresh", Name = "RefreshToken")]
        public async Task<IActionResult> Refresh()
        {
            string? Username = HttpContext.User.GetUsername();
            if (Username == null)
                return Unauthorized("Invalid token");
            bool isAdmin = HttpContext.User.IsAdministrator();
            string ServerId = HttpContext.User.GetServerId();

            if (isAdmin)
            {
                LocalUsers? localUsers = await context.LocalUsers.Where(u => u.Username == Username).FirstOrDefaultAsync();

                if (localUsers != null)
                {

                    List<Permissions> permissions = await context.Permissions.Where(p => context.UserPermissions.Where(up => up.UserId == localUsers.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();

                    var credentials = generateTokens(localUsers.Username, permissions, localUsers.ServerId);

                    return Ok(credentials);
                }

                return Unauthorized("Invalid username or password");

            }

            iAPI? instance = Global.APIInstanceManager.GetInstance(ServerId!);
            if (instance == null)
            {
                return Unauthorized("Invalid server ID");
            }

            // Re-check what allowed the original remote login, so turning remote auth off or removing the
            // user in Jellyfin ends their session at the next refresh instead of lasting forever.
            if (!instance.config.AllowRemoteAuth)
            {
                return Unauthorized("Remote authentication is not allowed for this server");
            }

            // Users is kept in sync with the media server (users deleted there are removed on sync)
            string? remoteUserId = await FindServerUserIdAsync(ServerId, Username);
            if (remoteUserId == null)
            {
                return Unauthorized("User no longer exists on this server");
            }

            List<Permissions> viewPermissions = new() { new Permissions { PermissionKey = PermissionTypes.CanView.GetDisplayName() } };

            var remoteCredentials = generateTokens(Username, viewPermissions, ServerId!, remoteUserId);

            return Ok(remoteCredentials);
        }

        //[Authorize]
        //[PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("UpdateUser", Name = "UpdateUser")]
        public async Task<ActionResult> UpdateUser([FromBody] LocalUser user)
        {
            if (IsApiKeyCaller) return Forbid();

            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true && HttpContext.User.IsAccessToken();
            bool isAdmin = Authorized && HttpContext.User.IsAdministrator();

            if (!isAdmin && !Global.AllowUnauthenticatedPasswordReset)
            {
                return Unauthorized();
            }

            LocalUsers? dbuser = await context.LocalUsers.Where(u => u.Id == user.Id).FirstOrDefaultAsync();
            if (dbuser == null)
            {
                return NotFound("User not found");
            }

            if (user.Username != dbuser.Username)
            {
                dbuser.Username = user.Username;

            }

            if (!user.Password.IsNullOrEmpty())
            {
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(user.Password);
                if (dbuser.Password != hashedPassword)
                {
                    dbuser.Password = hashedPassword;
                }
            }

            string? serverId = dbuser.ServerId;


            List<Permissions> permissions = await context.Permissions.Where(p => context.UserPermissions.Where(up => up.UserId == dbuser.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();
            var credentials = generateTokens(dbuser.Username, permissions, serverId);
            await context.SaveChangesAsync();

            return Ok(credentials);
        }

        //[Authorize]
        //[PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("CreateUser", Name = "CreateUser")]
        public async Task<ActionResult> CreateUser([FromBody] LocalUser user)
        {
            if (IsApiKeyCaller) return Forbid();

            bool isConfigured = Global.IsConfigured;
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true && HttpContext.User.IsAccessToken();
            bool isAdmin = Authorized && HttpContext.User.IsAdministrator();

            // Anonymous creation is only for the very first account during setup. As soon as any local user
            // exists this is admin-only, even before a server has been added (IsConfigured needs both).
            if (Global.HasLocalUsers && !isAdmin)
            {
                return Unauthorized();
            }


            if (user.Username.IsNullOrEmpty() || user.Password.IsNullOrEmpty())
            {
                return BadRequest("Username and password are required");
            }

            string username = user.Username.Trim();
            LocalUsers? dbuser = await context.LocalUsers.Where(u => u.Username.ToLower().Equals(username.ToLower())).FirstOrDefaultAsync();

            if (dbuser != null)
                return BadRequest("Username already exists");


            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    string userId = Guid.NewGuid().ToString();
                    await context.LocalUsers.AddAsync(new LocalUsers { Id = userId, Username = username, Password = BCrypt.Net.BCrypt.HashPassword(user.Password) });
                    await context.UserPermissions.AddAsync(new UserPermissions { UserId = userId, PermissionId = context.Permissions.Where(p => p.PermissionKey == PermissionTypes.Administrator.GetDisplayName()).First().Id });

                    await context.SaveChangesAsync();
                    List<Permissions> permissions = new() { new Permissions { PermissionKey = PermissionTypes.Administrator.GetDisplayName() } };
                    var credentials = generateTokens(username, permissions, null);

                    await transaction.CommitAsync();
                    Global.HasLocalUsers = true;
                    if (!isConfigured)
                    {
                        await ConfigurationHelper.CheckIsConfiguredAsync(context);
                    }
                    return Ok(credentials);

                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine(ex);
                    return BadRequest("Error occurred while creating user");
                }
            }


        }

        [HttpDelete("DeleteUser", Name = "DeleteUser")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteUser(string Id)
        {
            if (IsApiKeyCaller) return Forbid();

            LocalUsers? dbUser = await context.LocalUsers.Where(u => u.Id.Equals(Id)).FirstOrDefaultAsync();
            if (dbUser == null)
            {
                return NotFound();
            }

            int userCount = await context.LocalUsers.CountAsync();

            if (userCount == 1)
            {
                return BadRequest("All local users cannot be removed");
            }

            context.LocalUsers.Remove(dbUser);
            await context.SaveChangesAsync();

            return Ok();
        }


    }
}
