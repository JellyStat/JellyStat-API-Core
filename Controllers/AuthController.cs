using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Extensions;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Jellystat_API_Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        ApplicationDbContext context { get; set; }
        public AuthController(ApplicationDbContext context)
        {
            this.context = context;
        }


        private Object generateTokens(string Username, List<Permissions> permissions, string? ServerId)
        {
            var claims = new[]
           {
                new Claim("username", Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("type", "access"),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) )
            };

            if (ServerId != null)
            {
                claims = claims.Append(new Claim("serverId", ServerId)).ToArray();
            }

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
                new Claim("type", "refresh")
            };

            if (ServerId != null)
            {
                claims = claims.Append(new Claim("serverId", ServerId)).ToArray();
            }

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
                    List<LocalUsers> localUsers = await context.LocalUsers.Where(u => u.Username == loginModel.Username).ToListAsync();

                    if (localUsers.Count > 0)
                    {
                        LocalUsers user = localUsers.First();

                        if (BCrypt.Net.BCrypt.Verify(loginModel.Password, user.Password))
                        {
                            List<Permissions> permissions = await context.Permissions.Where(p => context.UserPermissions.Where(up => up.UserId == user.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();

                            var credentials = generateTokens(user.Username, permissions, null);

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

        [Authorize]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("UpdateUser", Name = "UpdateUser")]
        public async Task<ActionResult> UpdateUser([FromBody] LocalUser user)
        {
            LocalUsers? dbuser = null;
            if (user.OldUsername != null)
            {
                dbuser = await context.LocalUsers.Where(u => u.Username == user.OldUsername).FirstOrDefaultAsync();
                if (dbuser == null)
                    return NotFound("User not found");

                dbuser.Username = user.Username;
                await context.SaveChangesAsync();

            }
            else if (user.Password != null && user.OldPassword != null)
            {
                dbuser = await context.LocalUsers.Where(u => u.Username == user.Username).FirstOrDefaultAsync();

                if (dbuser == null || !BCrypt.Net.BCrypt.Verify(user.OldPassword, dbuser.Password))
                    return Unauthorized("Invalid Username or password");

                dbuser.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                await context.SaveChangesAsync();
            }
            else
            {
                return BadRequest("Some fields are missing for the required operation");
            }


            List<Permissions> permissions = await context.Permissions.Where(p => context.UserPermissions.Where(up => up.UserId == dbuser.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();
            var credentials = generateTokens(dbuser.Username, permissions, null);

            return Ok(credentials);
        }

        [Authorize]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        [HttpPost("CreateUser", Name = "CreateUser")]
        public async Task<ActionResult> CreateUser([FromBody] LocalUser user)
        {
            if (user.Username == null || user.Password == null)
            {
                return BadRequest("Username and password are required");
            }
            LocalUsers? dbuser = await context.LocalUsers.Where(u => u.Username == user.OldUsername).FirstOrDefaultAsync();

            if (dbuser != null)
                return BadRequest("Username already exists");


            using (var transaction = context.Database.BeginTransaction())
            {

                try
                {
                    string userId = Guid.NewGuid().ToString();
                    await context.LocalUsers.AddAsync(new LocalUsers { Id = userId, Username = user.Username, Password = BCrypt.Net.BCrypt.HashPassword(user.Password) });
                    await context.UserPermissions.AddAsync(new UserPermissions { UserId = userId, PermissionId = context.Permissions.Where(p => p.PermissionKey == PermissionTypes.Administrator.GetDisplayName()).First().Id });

                    List<Permissions> permissions = new() { new Permissions { PermissionKey = PermissionTypes.Administrator.GetDisplayName() } };
                    var credentials = generateTokens(user.Username, permissions, null);

                    await transaction.CommitAsync();
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

    }
}
