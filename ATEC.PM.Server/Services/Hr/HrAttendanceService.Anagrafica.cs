using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Anagrafica» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── RETTIFICHE ────────────────────────────────────────────────────────────

    public string? AddAdjustment(HrAdjustmentRequest req, int autoreId)
    {
        string direction = (req.Direction ?? "").Trim().ToUpperInvariant();
        if (direction is not ("IN" or "OUT"))
            return "Verso non valido: ammessi IN e OUT.";
        if (string.IsNullOrWhiteSpace(req.Reason))
            return "Il motivo della rettifica è obbligatorio.";
        if (req.EmployeeId == autoreId)
            return "Non puoi rettificare il tuo cartellino: la correzione la registra un'altra persona.";

        using MySqlConnection c = _db.Open();
        int esiste = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employees WHERE id = @Id AND status <> 'TERMINATED'",
            new { Id = req.EmployeeId });
        if (esiste == 0) return "Dipendente non trovato o cessato.";

        c.Execute(@"
            INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, reason, created_by)
            VALUES (@EmployeeId, @WorkDate, @PunchedAt, @Direction, 'ADJUSTMENT', @Reason, @Autore)",
            new
            {
                req.EmployeeId,
                WorkDate = req.PunchedAt.Date,
                req.PunchedAt,
                Direction = direction,
                Reason = req.Reason.Trim(),
                Autore = autoreId,
            });

        RicalcolaConVicine(c, req.EmployeeId, req.PunchedAt.Date);
        return null;
    }

    public string? DeleteAdjustment(long id, int autoreId)
    {
        using MySqlConnection c = _db.Open();
        var riga = c.QueryFirstOrDefault<RowToDelete>(
            @"SELECT employee_id AS EmployeeId, work_date AS WorkDate, source AS Source,
                     punched_at AS PunchedAt, direction AS Direction, reason AS Reason
              FROM hr_punches WHERE id = @Id",
            new { Id = id });

        if (riga == null) return "Timbratura non trovata.";
        if (!string.Equals(riga.Source, "ADJUSTMENT", StringComparison.OrdinalIgnoreCase))
            return "Si possono eliminare solo le rettifiche: il grezzo del rilevatore resta.";
        if (riga.EmployeeId == autoreId)
            return "Non puoi eliminare una rettifica sul tuo cartellino.";

        c.Execute("DELETE FROM hr_punches WHERE id = @Id", new { Id = id });
        _logger.LogInformation(
            "[HR] Rettifica eliminata da dipendente {Autore}: era {Verso} del {Orario:yyyy-MM-dd HH:mm} " +
            "sul cartellino di {Dipendente}, motivo «{Reason}».",
            autoreId, riga.Direction, riga.PunchedAt, riga.EmployeeId, riga.Reason);

        RicalcolaConVicine(c, riga.EmployeeId, riga.WorkDate);
        return null;
    }

    // ── MAPPATURA DIPENDENTI ↔ ECOS ───────────────────────────────────────────

    public List<HrMappingRowDto> GetEcosMapping()
    {
        using MySqlConnection c = _db.Open();
        return c.Query<HrMappingRowDto>(
            @"SELECT id AS EmployeeId,
                     CONCAT_WS(' ', first_name, last_name) AS Name,
                     ecos_empl_code AS EcosEmplCode
              FROM employees
              WHERE status = 'ACTIVE'
                AND emp_type = 'INTERNAL'
                AND user_role <> 'ADMIN'
                AND first_name NOT LIKE '[%'
              ORDER BY last_name, first_name").ToList();
    }

    public string? UpdateEcosMapping(int employeeId, string? ecosEmplCode)
    {
        string? code = EmployeeHrConfig.NormalizeEcosCode(ecosEmplCode);

        using MySqlConnection c = _db.Open();
        string? error = EmployeeHrConfig.ValidateEcosCode(c, employeeId, code);
        if (error != null)
            return error;

        try
        {
            int rowsAffected = c.Execute(
                "UPDATE employees SET ecos_empl_code = @Code WHERE id = @Id",
                new { Code = code, Id = employeeId });
            return rowsAffected == 0 ? "Dipendente non trovato." : null;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return $"Il codice Ecos {code} è appena stato collegato a un altro dipendente.";
        }
    }

    public void RecalculateAllEmployeeDays(MySqlConnection c, int employeeId)
    {
        List<DateTime> giorni = c.Query<DateTime>(
                @"SELECT DISTINCT work_date FROM hr_punches
                  WHERE employee_id = @EmployeeId ORDER BY work_date",
                new { EmployeeId = employeeId })
            .ToList();

        foreach (DateTime work_date in giorni)
            RecalculateDay(c, employeeId, work_date);
    }

    // ── STATO ─────────────────────────────────────────────────────────────────

    public HrStatusDto GetStatus()
    {
        using MySqlConnection c = _db.Open();
        var conteggi = c.QuerySingle<(long Punches, long Days, int Collegati, int Attivi)>(@"
            SELECT (SELECT COUNT(*) FROM hr_punches) AS Punches,
                   (SELECT COUNT(*) FROM hr_days) AS Days,
                   (SELECT COUNT(*) FROM employees
                     WHERE status = 'ACTIVE' AND emp_type = 'INTERNAL' AND ecos_empl_code IS NOT NULL AND ecos_empl_code <> '') AS Collegati,
                   (SELECT COUNT(*) FROM employees
                     WHERE status = 'ACTIVE' AND emp_type = 'INTERNAL' AND user_role <> 'ADMIN' AND first_name NOT LIKE '[%') AS Attivi");

        return new HrStatusDto
        {
            Configured = _ecos.Configured,
            ImportInProgress = ImportInProgress,
            LastImport = LastImport,
            LastResult = LastResult,
            TotalPunches = conteggi.Punches,
            TotalDays = conteggi.Days,
            LinkedEmployees = conteggi.Collegati,
            ActiveEmployees = conteggi.Attivi,
            LastBadgeRead = LeggiConfigData(c, BadgeKey),
            Progress = SnapshotProgresso(),
        };
    }

    /// <summary>
    /// Registra la lettura riuscita dell'anagrafica badge (voce 11 del port). Si chiama
    /// nell'unico punto in cui i badge si leggono davvero: nell'originale il pulsante
    /// «Solo Badge» non la scriveva, e la data a video restava indietro per sempre.
    /// </summary>
    public void MarkBadgeRead()
    {
        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO app_config (config_key, config_value, description)
            VALUES (@K, @V, 'HR: ultima lettura riuscita dell''anagrafica badge da Ecos')
            ON DUPLICATE KEY UPDATE config_value = @V",
            new { K = BadgeKey, V = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
    }

    private static DateTime? LeggiConfigData(MySqlConnection c, string chiave)
    {
        string? valore = c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key = @K", new { K = chiave });
        return DateTime.TryParse(valore, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime data)
            ? data
            : null;
    }

    // ── ATTREZZI ──────────────────────────────────────────────────────────────

    private static Dictionary<string, int> MappaEcos(MySqlConnection c)
    {
        var mappa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var riga in c.Query<(int Id, string Codice)>(
            "SELECT id AS Id, ecos_empl_code AS Codice FROM employees WHERE ecos_empl_code IS NOT NULL AND ecos_empl_code <> ''"))
        {
            mappa[riga.Codice.Trim()] = riga.Id;
        }
        return mappa;
    }

    internal static DateTime NuovoCursore(IReadOnlyList<EcosPunch> timbrature, DateTime inizio)
    {
        DateTime? massimo = timbrature
            .Where(t => t.UpdateDate.HasValue)
            .Select(t => t.UpdateDate!.Value)
            .DefaultIfEmpty()
            .Max();

        return massimo is { } m && m != default
            ? m - MargineCursore
            : inizio - MargineCursoreOrologioNostro;
    }

    private static DateTime? LeggiCursore(MySqlConnection c)
    {
        string? valore = c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key = @K", new { K = CursoreKey });
        return DateTime.TryParse(valore, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime data)
            ? data
            : null;
    }

    private static void ScriviCursore(MySqlConnection c, DateTime valore)
    {
        c.Execute(@"
            INSERT INTO app_config (config_key, config_value, description)
            VALUES (@K, @V, 'HR: importate da Ecos le timbrature con UpdateDate >= di questo istante')
            ON DUPLICATE KEY UPDATE config_value = @V",
            new { K = CursoreKey, V = valore.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
    }

    internal static int MinutesFrom(string? durata)
    {
        if (string.IsNullOrWhiteSpace(durata)) return 0;
        string[] parti = durata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parti.Length != 2) return 0;
        if (!parti[0].EndsWith('h') || !parti[1].EndsWith('m')) return 0;
        if (!int.TryParse(parti[0][..^1], out int ore)) return 0;
        if (!int.TryParse(parti[1][..^1], out int minuti)) return 0;
        return ore * 60 + minuti;
    }

    internal static string? BandsJson(TimesheetDay cart)
    {
        Dictionary<string, string> nonZero = cart.Fasce
            .Where(f => f.Value is not ("0h 0m" or "---") && !string.IsNullOrEmpty(f.Value))
            .ToDictionary(f => f.Key, f => f.Value);
        return nonZero.Count == 0 ? null : JsonSerializer.Serialize(nonZero);
    }

    private static Dictionary<string, string> LeggiFasce(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static IEnumerable<T[]> ABlocchi<T>(IEnumerable<T> valori, int dimensione)
    {
        var blocco = new List<T>(dimensione);
        foreach (T v in valori)
        {
            blocco.Add(v);
            if (blocco.Count == dimensione)
            {
                yield return blocco.ToArray();
                blocco.Clear();
            }
        }
        if (blocco.Count > 0) yield return blocco.ToArray();
    }

    private static HrImportResultDto Fallito(string messaggio) =>
        new() { Success = false, Message = messaggio };

    private sealed class RigaEsistente
    {
        public long Id { get; set; }
        public string? ExternalId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public DateTime PunchedAt { get; set; }
        public string Direction { get; set; } = "";
        public string? Location { get; set; }
    }

    private sealed class DayRow
    {
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string? ClockIn1 { get; set; }
        public string? ClockOut1 { get; set; }
        public string? ClockIn2 { get; set; }
        public string? ClockOut2 { get; set; }
        public int RegularMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int BreakMinutes { get; set; }
        public string? BandsJson { get; set; }
        public string Note { get; set; } = "";
        public bool HasAnomaly { get; set; }
    }

    private sealed class RowToDelete
    {
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string Source { get; set; } = "";
        public DateTime PunchedAt { get; set; }
        public string Direction { get; set; } = "";
        public string? Reason { get; set; }
    }

    private sealed class PunchRow
    {
        public long Id { get; set; }
        public DateTime WorkDate { get; set; }
        public DateTime PunchedAt { get; set; }
        public string Direction { get; set; } = "";
        public string Source { get; set; } = "";
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
    }
}
