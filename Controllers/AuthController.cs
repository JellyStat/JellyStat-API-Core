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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Extensions;
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
        private readonly IAPIInstanceManager _apiInstanceManager;

        public AuthController(IAPIInstanceManager apiInstanceManager, ApplicationDbContext context)
        {
            _apiInstanceManager = apiInstanceManager;
            this.context = context;
        }


        private Object generateTokens(string Username, List<Permissions> permissions, string? ServerId)
        {
            var claims = new[]
           {
                new Claim("username", Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("type", "access"),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) ),
                new Claim("serverId", ServerId ?? string.Empty)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Global.JwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: Global.JwtIssuer,
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds);

            claims = new[]
           {
                new Claim("username", Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) ),
                new Claim("type", "refresh"),
                new Claim("serverId", ServerId ?? string.Empty)
            };


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
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true;

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

                    if (localUsers != null)
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

                List<Permissions> viewPermissions = new() { new Permissions { PermissionKey = PermissionTypes.CanView.GetDisplayName() } };

                var remoteCredentials = generateTokens(loginModel.Username, viewPermissions, loginModel.ServerId!);

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



            List<Permissions> viewPermissions = new() { new Permissions { PermissionKey = PermissionTypes.CanView.GetDisplayName() } };

            var remoteCredentials = generateTokens(Username, viewPermissions, ServerId!);

            return Ok(remoteCredentials);
        }

        //[Authorize]
        //[PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("UpdateUser", Name = "UpdateUser")]
        public async Task<ActionResult> UpdateUser([FromBody] LocalUser user)
        {
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true;
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
            bool isConfigured = Global.IsConfigured;
            bool Authorized = HttpContext.User.Identity?.IsAuthenticated == true;
            bool isAdmin = Authorized && HttpContext.User.IsAdministrator();

            if (isConfigured && !isAdmin)
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
