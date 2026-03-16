using System.Collections.Concurrent;
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
    private readonly ILogger<AuthController> _log;

    // Rate limiting: username → (tentativi falliti, ultimo tentativo)
    private static readonly ConcurrentDictionary<string, (int Count, DateTime LastAttempt)> _loginAttempts = new();
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public AuthController(DbService db, IConfiguration config, ILogger<AuthController> log)
    {
        _db = db;
        _config = config;
        _log = log;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        string key = (req.Username ?? "").ToLower().Trim();

        // Check rate limit
        if (_loginAttempts.TryGetValue(key, out var attempt))
        {
            if (attempt.Count >= MaxAttempts && DateTime.UtcNow - attempt.LastAttempt < LockoutDuration)
            {
                int remainingSeconds = (int)(LockoutDuration - (DateTime.UtcNow - attempt.LastAttempt)).TotalSeconds;
                _log.LogWarning("[Auth] Login bloccato per '{User}' — troppi tentativi. Riprova tra {Sec}s", key, remainingSeconds);
                return StatusCode(429, ApiResponse<string>.Fail($"Troppi tentativi. Riprova tra {remainingSeconds / 60} minuti."));
            }

            // Reset se è passato il lockout
            if (DateTime.UtcNow - attempt.LastAttempt >= LockoutDuration)
                _loginAttempts.TryRemove(key, out _);
        }

        using var c = _db.Open();
        var user = c.QueryFirstOrDefault<LoginResponse>(
            "SELECT id AS EmployeeId, CONCAT(first_name,' ',last_name) AS FullName, user_role AS UserRole FROM employees WHERE username=@Username AND password_hash=SHA2(@Password,256) AND status='ACTIVE'",
            req);

        if (user == null)
        {
            // Incrementa contatore tentativi falliti
            _loginAttempts.AddOrUpdate(key,
                (1, DateTime.UtcNow),
                (_, old) => (old.Count + 1, DateTime.UtcNow));

            int currentCount = _loginAttempts.TryGetValue(key, out var updated) ? updated.Count : 0;
            _log.LogWarning("[Auth] Login fallito per '{User}' — tentativo {Count}/{Max}", key, currentCount, MaxAttempts);

            if (currentCount >= MaxAttempts)
                return StatusCode(429, ApiResponse<string>.Fail($"Troppi tentativi. Account bloccato per {LockoutDuration.TotalMinutes} minuti."));

            return Unauthorized(ApiResponse<string>.Fail("Credenziali non valide"));
        }

        // Login riuscito — reset contatore
        _loginAttempts.TryRemove(key, out _);

        var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken("ATEC.PM", "ATEC.PM",
            new[] {
                new Claim(ClaimTypes.NameIdentifier, user.EmployeeId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.UserRole)
            },
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256));

        user.Token = new JwtSecurityTokenHandler().WriteToken(token);

        _log.LogInformation("[Auth] Login riuscito: {User} (ID: {Id}, Ruolo: {Role})", user.FullName, user.EmployeeId, user.UserRole);
        return Ok(ApiResponse<LoginResponse>.Ok(user));
    }

    [HttpPost("set-credentials")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public IActionResult SetCredentials([FromBody] SetCredentialsRequest req)
    {
        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employees WHERE id=@EmployeeId AND status='ACTIVE'",
            new { req.EmployeeId });

        if (exists == 0)
            return NotFound(ApiResponse<string>.Fail("Dipendente non trovato"));

        c.Execute(
            "UPDATE employees SET username=@Username, password_hash=SHA2(@Password,256) WHERE id=@EmployeeId",
            req);

        _log.LogInformation("[Auth] Credenziali impostate per dipendente ID: {Id}", req.EmployeeId);
        return Ok(ApiResponse<string>.Ok("Credenziali impostate"));
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
    {
        int employeeId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        using var c = _db.Open();

        int valid = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employees WHERE id=@Id AND password_hash=SHA2(@OldPassword,256)",
            new { Id = employeeId, req.OldPassword });

        if (valid == 0)
            return BadRequest(ApiResponse<string>.Fail("Password attuale non corretta"));

        c.Execute(
            "UPDATE employees SET password_hash=SHA2(@NewPassword,256) WHERE id=@Id",
            new { Id = employeeId, req.NewPassword });

        _log.LogInformation("[Auth] Password cambiata per dipendente ID: {Id}", employeeId);
        return Ok(ApiResponse<string>.Ok("Password aggiornata"));
    }
}
