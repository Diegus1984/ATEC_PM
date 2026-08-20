using System.Collections.Concurrent;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Dapper;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

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

    /// <summary>
    /// Secondo contatore, per <b>indirizzo</b>: il blocco per username non ferma chi prova UNA
    /// password su CENTO nomi diversi dalla stessa macchina — ogni username resta a 1 tentativo e
    /// nessuna soglia scatta mai.
    /// <para><b>La soglia è alta apposta (30 in 5 minuti).</b> In ufficio si esce tutti dallo stesso
    /// indirizzo: un limite stretto per IP chiuderebbe fuori l'intera azienda perché tre persone
    /// hanno sbagliato la password di lunedì mattina. 30 non dà fastidio a nessuno e taglia le
    /// gambe a chi prova nomi a raffica.</para>
    /// <para>⚠️ Come il contatore per username, sta <b>in memoria</b>: si azzera a ogni riavvio del
    /// servizio, quindi anche a ogni aggiornamento. Per un blocco che sopravviva al riavvio
    /// servirebbe una tabella — vale la pena solo se il server viene esposto fuori dalla LAN.</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, (int Count, DateTime LastAttempt)> _loginAttemptsByIp = new();
    private const int MaxAttemptsPerIp = 30;

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
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string UserRole { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }

    private class EmployeePasswordRow
    {
        public int EmployeeId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        string key = (req.Username ?? "").ToLower().Trim();
        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "sconosciuto";

        // Cleanup periodico entry scadute (ogni 10 minuti)
        CleanupExpiredAttempts();

        // Limite per INDIRIZZO, prima di quello per username: è l'unico che ferma chi prova una
        // password su tanti nomi diversi (col solo contatore per username nessuna soglia scatta).
        if (_loginAttemptsByIp.TryGetValue(ip, out var daIp))
        {
            if (daIp.Count >= MaxAttemptsPerIp && DateTime.UtcNow - daIp.LastAttempt < LockoutDuration)
            {
                int attesa = (int)(LockoutDuration - (DateTime.UtcNow - daIp.LastAttempt)).TotalSeconds;
                _log.LogWarning("[Auth] Login bloccato da {Ip} — {Count} tentativi falliti in {Min} minuti.",
                    ip, daIp.Count, LockoutDuration.TotalMinutes);
                return StatusCode(429, ApiResponse<string>.Fail(
                    $"Troppi tentativi da questa postazione. Riprova tra {Math.Max(1, attesa / 60)} minuti."));
            }

            if (DateTime.UtcNow - daIp.LastAttempt >= LockoutDuration)
                _loginAttemptsByIp.TryRemove(ip, out _);
        }

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
                   first_name AS FirstName,
                   last_name AS LastName,
                   CONCAT(first_name,' ',last_name) AS FullName,
                   user_role AS UserRole,
                   password_hash AS PasswordHash
            FROM employees
            WHERE (username=@Username
                   OR LOWER(CONCAT(first_name, '.', last_name))=@Username
                   OR LOWER(CONCAT(SUBSTRING(first_name, 1, 1), '.', last_name))=@Username)
              AND status='ACTIVE'",
            new { req.Username });

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            RecordFailedAttempt(key, ip);
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
            RecordFailedAttempt(key, ip);
            return Unauthorized(ApiResponse<string>.Fail("Credenziali non valide"));
        }

        // Login riuscito — reset del contatore dell'username.
        //
        // Il contatore per INDIRIZZO invece NON si azzera: se bastasse un accesso riuscito a
        // ripulirlo, chi possiede una credenziale valida potrebbe usarla come «reset» ogni 29
        // tentativi e provarne altri 29 all'infinito. Si libera da solo dopo 5 minuti senza
        // fallimenti, che in ufficio arrivano da sé.
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
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            MustChangePassword = InitialPasswordHelper.IsInitialPassword(
                req.Password, user.FirstName, user.LastName)
        };

        _log.LogInformation("[Auth] Login riuscito: {User} (ID: {Id}, Ruolo: {Role})", user.FullName, user.EmployeeId, user.UserRole);
        return Ok(ApiResponse<LoginResponse>.Ok(response));
    }

    [HttpPost("set-credentials")]
    [Authorize]
    [RequireFeature("nav.utenti")]
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
    [Authorize]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
    {
        int employeeId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        using var c = _db.Open();
        EmployeePasswordRow? row = c.QueryFirstOrDefault<EmployeePasswordRow>(@"
            SELECT id AS EmployeeId, first_name AS FirstName, last_name AS LastName, password_hash AS PasswordHash
            FROM employees
            WHERE id=@EmployeeId AND status='ACTIVE'",
            new { EmployeeId = employeeId });

        if (row == null || string.IsNullOrEmpty(row.PasswordHash))
            return BadRequest(ApiResponse<string>.Fail("Utente non trovato"));

        return ApplyPasswordChange(row, req, c);
    }

    /// <summary>Cambio password dalla schermata di login (senza sessione): richiede username + password attuale.</summary>
    [HttpPost("change-password-login")]
    [AllowAnonymous]
    public IActionResult ChangePasswordFromLogin([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(ApiResponse<string>.Fail("Username obbligatorio"));

        using var c = _db.Open();
        EmployeePasswordRow? row = c.QueryFirstOrDefault<EmployeePasswordRow>(@"
            SELECT id AS EmployeeId, first_name AS FirstName, last_name AS LastName, password_hash AS PasswordHash
            FROM employees
            WHERE (username=@Username
                   OR LOWER(CONCAT(first_name, '.', last_name))=@Username
                   OR LOWER(CONCAT(SUBSTRING(first_name, 1, 1), '.', last_name))=@Username)
              AND status='ACTIVE'",
            new { Username = req.Username.Trim() });

        if (row == null || string.IsNullOrEmpty(row.PasswordHash))
            return BadRequest(ApiResponse<string>.Fail("Credenziali non valide"));

        return ApplyPasswordChange(row, req, c);
    }

    /// <summary>Verifica se l'utente loggato è ancora attivo (es. dopo disattivazione da admin).</summary>
    [HttpGet("session")]
    [Authorize]
    public IActionResult GetSession()
    {
        string? idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idClaim, out int employeeId))
            return Unauthorized(ApiResponse<string>.Fail("Sessione non valida"));

        using var c = _db.Open();
        string? status = c.QueryFirstOrDefault<string>(
            "SELECT status FROM employees WHERE id=@Id",
            new { Id = employeeId });

        bool active = string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        return Ok(ApiResponse<SessionStatusDto>.Ok(new SessionStatusDto
        {
            EmployeeId = employeeId,
            IsActive = active
        }));
    }

    private IActionResult ApplyPasswordChange(
        EmployeePasswordRow row, ChangePasswordRequest req, IDbConnection c)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword)
            || req.NewPassword.Length < InitialPasswordHelper.MinPasswordLength)
        {
            return BadRequest(ApiResponse<string>.Fail(
                $"La nuova password deve avere almeno {InitialPasswordHelper.MinPasswordLength} caratteri"));
        }

        if (!string.Equals(req.NewPassword, req.ConfirmNewPassword, StringComparison.Ordinal))
            return BadRequest(ApiResponse<string>.Fail("Le due password non coincidono"));

        if (string.Equals(req.NewPassword, req.CurrentPassword, StringComparison.Ordinal))
            return BadRequest(ApiResponse<string>.Fail("La nuova password deve essere diversa da quella attuale"));

        if (InitialPasswordHelper.IsInitialPassword(req.NewPassword, row.FirstName, row.LastName))
        {
            return BadRequest(ApiResponse<string>.Fail(
                "La nuova password non può essere uguale alla password iniziale (n.cognome)"));
        }

        if (!VerifyPassword(req.CurrentPassword, row.PasswordHash, row.EmployeeId, c))
            return BadRequest(ApiResponse<string>.Fail("Password attuale non corretta"));

        string newHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        c.Execute(
            "UPDATE employees SET password_hash=@Hash WHERE id=@EmployeeId",
            new { Hash = newHash, EmployeeId = row.EmployeeId });

        _log.LogInformation("[Auth] Password cambiata (bcrypt) per dipendente ID: {Id}", row.EmployeeId);
        return Ok(ApiResponse<string>.Ok("Password aggiornata"));
    }

    /// <summary>Verifica bcrypt ($2...) oppure legacy SHA256, con migrazione trasparente a bcrypt.</summary>
    private bool VerifyPassword(string password, string storedHash, int employeeId, IDbConnection c)
    {
        if (storedHash.StartsWith("$2", StringComparison.Ordinal))
            return BCrypt.Net.BCrypt.Verify(password, storedHash);

        string sha256Hash = ComputeSha256(password);
        bool valid = string.Equals(sha256Hash, storedHash, StringComparison.OrdinalIgnoreCase);
        if (!valid)
            return false;

        string bcryptHash = BCrypt.Net.BCrypt.HashPassword(password);
        c.Execute("UPDATE employees SET password_hash=@Hash WHERE id=@Id",
            new { Hash = bcryptHash, Id = employeeId });
        _log.LogInformation("[Auth] Password migrata SHA2→bcrypt per dipendente ID: {Id}", employeeId);
        return true;
    }

    // ── HELPERS ──────────────────────────────────────────────────────

    /// <summary>Calcolo SHA256 per compatibilità con hash legacy.</summary>
    private static string ComputeSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Registra un tentativo di login fallito su <b>entrambi</b> i contatori: quello per username
    /// (5 tentativi) e quello per indirizzo (30). Il secondo va aggiornato anche quando l'username
    /// non esiste nemmeno — anzi, soprattutto: è il caso di chi prova nomi a raffica.
    /// </summary>
    private void RecordFailedAttempt(string key, string ip)
    {
        _loginAttempts.AddOrUpdate(key,
            (1, DateTime.UtcNow),
            (_, old) => (old.Count + 1, DateTime.UtcNow));

        _loginAttemptsByIp.AddOrUpdate(ip,
            (1, DateTime.UtcNow),
            (_, old) => (old.Count + 1, DateTime.UtcNow));

        int currentCount = _loginAttempts.TryGetValue(key, out var updated) ? updated.Count : 0;
        int daIp = _loginAttemptsByIp.TryGetValue(ip, out var perIp) ? perIp.Count : 0;
        _log.LogWarning("[Auth] Login fallito per '{User}' da {Ip} — tentativo {Count}/{Max} (da questo indirizzo: {DaIp}/{MaxIp})",
            key, ip, currentCount, MaxAttempts, daIp, MaxAttemptsPerIp);
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

        // Anche il contatore per indirizzo, o cresce senza fine (una voce per ogni IP che ha
        // sbagliato una password, per sempre).
        foreach (var kvp in _loginAttemptsByIp)
        {
            if (kvp.Value.LastAttempt < cutoff)
                _loginAttemptsByIp.TryRemove(kvp.Key, out _);
        }
    }
}
