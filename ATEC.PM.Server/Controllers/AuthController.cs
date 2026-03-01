using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DbService _db;
    private readonly IConfiguration _config;

    public AuthController(DbService db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        using var c = _db.Open();
        var user = c.QueryFirstOrDefault<LoginResponse>(
            "SELECT id AS EmployeeId, CONCAT(first_name,' ',last_name) AS FullName, user_role AS UserRole FROM employees WHERE username=@Username AND password_hash=SHA2(@Password,256) AND status='ACTIVE'",
            req);

        if (user == null)
            return Unauthorized(ApiResponse<string>.Fail("Credenziali non valide"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken("ATEC.PM", "ATEC.PM",
            new[] {
                new Claim(ClaimTypes.NameIdentifier, user.EmployeeId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.UserRole)
            },
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        user.Token = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(ApiResponse<LoginResponse>.Ok(user));
    }
}
