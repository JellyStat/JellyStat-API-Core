using Jellystat_API_Core.Classes.API.@interface;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Models.Jellystat;
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

        private Object generateTokens(string Username, List<Permissions> permissions, string? ServerId)
        {
            var claims = new[]
           {
                new Claim(JwtRegisteredClaimNames.Sub, Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("type", "access"),
                new Claim("permissions", JsonConvert.SerializeObject(permissions.Select(p=>p.PermissionKey).ToList() ) )
            };

            if (ServerId != null)
            {
                claims = claims.Append(new Claim("serverId", ServerId)).ToArray();
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Jwt:Issuer"],
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds);

            claims = new[]
           {
                new Claim(JwtRegisteredClaimNames.Sub, Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("type", "refresh")
            };

            if (ServerId != null)
            {
                claims = claims.Append(new Claim("serverId", ServerId)).ToArray();
            }

            var refreshToken = new JwtSecurityToken(
                issuer: HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Jwt:Issuer"],
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
        public async Task<IActionResult> Login([FromBody] LoginModel loginModel)
        {
            if (loginModel == null)
            {
                return BadRequest("Invalid login data");
            }



            try
            {
                if (loginModel.ServerId == null)
                {
                    List<LocalUsers> localUsers = await Global.Database.context.LocalUsers.Where(u => u.Username == loginModel.Username).ToListAsync();

                    if (localUsers.Count > 0)
                    {
                        LocalUsers user = localUsers.First();

                        if (BCrypt.Net.BCrypt.Verify(loginModel.Password, user.Password))
                        {
                            List<Permissions> permissions = await Global.Database.context.Permissions.Where(p => Global.Database.context.UserPermissions.Where(up => up.UserId == user.Id).Select(up => up.PermissionId).Contains(p.Id)).ToListAsync();

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
    }
}
