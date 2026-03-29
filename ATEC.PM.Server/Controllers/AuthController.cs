using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    private static DateTime _lastCleanup = DateTime.UtcNow;

    public AuthController(DbService db, IConfiguration config, ILogger<AuthController> log)
    {
        _db = db;
        _config = config;
        _log = log;
    }

    // ── DTO interno per query login (include hash) ──────────────────
    private class LoginUserRow
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = "";
        public string UserRole { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        string key = (req.Username ?? "").ToLower().Trim();

        // Cleanup periodico entry scadute (ogni 10 minuti)
        CleanupExpiredAttempts();

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

        // Query SENZA check password — la verifica avviene in C# (bcrypt non può essere verificato in SQL)
        var user = c.QueryFirstOrDefault<LoginUserRow>(@"
            SELECT id AS EmployeeId,
                   CONCAT(first_name,' ',last_name) AS FullName,
                   user_role AS UserRole,
                   password_hash AS PasswordHash
            FROM employees
            WHERE username=@Username AND status='ACTIVE'",
            new { req.Username });

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            RecordFailedAttempt(key);
            return Unauthorized(ApiResponse<string>.Fail("Credenziali non valide"));
        }

        // Verifica password: dual-hash (bcrypt o legacy SHA2)
        bool passwordValid;
        if (user.PasswordHash.StartsWith("$2"))
        {
            // Password già migrata a bcrypt
            passwordValid = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        }
        else
        {
            // Legacy SHA2 — verifica e migra automaticamente a bcrypt
            string sha256Hash = ComputeSha256(req.Password);
            passwordValid = string.Equals(sha256Hash, user.PasswordHash, StringComparison.OrdinalIgnoreCase);

            if (passwordValid)
            {
                // Migrazione trasparente: riscrivi hash in bcrypt
                string bcryptHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
                c.Execute("UPDATE employees SET password_hash=@Hash WHERE id=@Id",
                    new { Hash = bcryptHash, Id = user.EmployeeId });
                _log.LogInformation("[Auth] Password migrata SHA2→bcrypt per dipendente ID: {Id}", user.EmployeeId);
            }
        }

        if (!passwordValid)
        {
            RecordFailedAttempt(key);
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

        LoginResponse response = new()
        {
            EmployeeId = user.EmployeeId,
            FullName = user.FullName,
            UserRole = user.UserRole,
            Token = new JwtSecurityTokenHandler().WriteToken(token)
        };

        _log.LogInformation("[Auth] Login riuscito: {User} (ID: {Id}, Ruolo: {Role})", user.FullName, user.EmployeeId, user.UserRole);
        return Ok(ApiResponse<LoginResponse>.Ok(response));
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

        // Hash con bcrypt
        string hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        c.Execute(
            "UPDATE employees SET username=@Username, password_hash=@Hash WHERE id=@EmployeeId",
            new { req.Username, Hash = hash, req.EmployeeId });

        _log.LogInformation("[Auth] Credenziali impostate (bcrypt) per dipendente ID: {Id}", req.EmployeeId);
        return Ok(ApiResponse<string>.Ok("Credenziali impostate"));
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
    {
        int employeeId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        using var c = _db.Open();

        string? storedHash = c.ExecuteScalar<string?>(
            "SELECT password_hash FROM employees WHERE id=@Id",
            new { Id = employeeId });

        if (string.IsNullOrEmpty(storedHash))
            return BadRequest(ApiResponse<string>.Fail("Utente non trovato"));

        // Verifica vecchia password con dual-hash
        bool oldPasswordValid;
        if (storedHash.StartsWith("$2"))
            oldPasswordValid = BCrypt.Net.BCrypt.Verify(req.OldPassword, storedHash);
        else
            oldPasswordValid = string.Equals(ComputeSha256(req.OldPassword), storedHash, StringComparison.OrdinalIgnoreCase);

        if (!oldPasswordValid)
            return BadRequest(ApiResponse<string>.Fail("Password attuale non corretta"));

        // Nuova password sempre in bcrypt
        string newHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        c.Execute("UPDATE employees SET password_hash=@Hash WHERE id=@Id",
            new { Hash = newHash, Id = employeeId });

        _log.LogInformation("[Auth] Password cambiata (bcrypt) per dipendente ID: {Id}", employeeId);
        return Ok(ApiResponse<string>.Ok("Password aggiornata"));
    }

    // ── HELPERS ──────────────────────────────────────────────────────

    /// <summary>Calcolo SHA256 per compatibilità con hash legacy.</summary>
    private static string ComputeSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Registra un tentativo di login fallito.</summary>
    private void RecordFailedAttempt(string key)
    {
        _loginAttempts.AddOrUpdate(key,
            (1, DateTime.UtcNow),
            (_, old) => (old.Count + 1, DateTime.UtcNow));

        int currentCount = _loginAttempts.TryGetValue(key, out var updated) ? updated.Count : 0;
        _log.LogWarning("[Auth] Login fallito per '{User}' — tentativo {Count}/{Max}", key, currentCount, MaxAttempts);
    }

    /// <summary>Pulizia periodica delle entry di rate limiting scadute (evita memory leak).</summary>
    private static void CleanupExpiredAttempts()
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(10)) return;
        _lastCleanup = DateTime.UtcNow;

        DateTime cutoff = DateTime.UtcNow - LockoutDuration;
        foreach (var kvp in _loginAttempts)
        {
            if (kvp.Value.LastAttempt < cutoff)
                _loginAttempts.TryRemove(kvp.Key, out _);
        }
    }
}
