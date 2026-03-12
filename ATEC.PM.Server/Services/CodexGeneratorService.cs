using Dapper;

namespace ATEC.PM.Server.Services;

public class CodexGeneratorService
{
    private readonly DbService _db;
    private readonly ILogger<CodexGeneratorService> _log;
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(10);

    public CodexGeneratorService(DbService db, ILogger<CodexGeneratorService> log)
    {
        _db = db;
        _log = log;
    }
    /// <summary>
    /// Formatta il codice per la visualizzazione: 101120326001 → 101120326.001
    /// </summary>
    public static string FormatCodeForDisplay(string code)
    {
        if (code.Length > 3)
            return code.Substring(0, code.Length - 3) + "." + code.Substring(code.Length - 3);
        return code;
    }

    /// <summary>
    /// Prenota il prossimo codice disponibile per un prefisso, senza inserirlo in codex_items.
    /// </summary>
    public (string Code, int ReservationId) ReserveNextCode(string prefix, string userName)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix is required", nameof(prefix));

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.ExecuteScalar("SELECT GET_LOCK(@LockKey, 30)",
                new { LockKey = $"codex_reserve_{prefix}" }, tx);

            // Pulisci prenotazioni con data precedente a oggi
            conn.Execute("DELETE FROM codex_reservations WHERE DATE(reserved_at) < CURDATE()", transaction: tx);

            // Calcola prossimo codice considerando sia codex_items che prenotazioni attive
            string today = DateTime.Now.ToString("ddMMyy");
            string codePrefix = $"{prefix}{today}";

            string? maxFromItemsCode = conn.ExecuteScalar<string?>(@"
                SELECT MAX(codice) FROM codex_items WHERE codice LIKE @Pattern",
                new { Pattern = codePrefix + "%" }, tx);

            int maxFromItems = 0;
            if (!string.IsNullOrEmpty(maxFromItemsCode) && maxFromItemsCode.Length >= 3)
            {
                string suffix = maxFromItemsCode.Substring(maxFromItemsCode.Length - 3);
                if (int.TryParse(suffix, out int s)) maxFromItems = s;
            }

            string? maxFromReservationsCode = conn.ExecuteScalar<string?>(@"
                SELECT MAX(reserved_code) FROM codex_reservations 
                WHERE reserved_code LIKE @Pattern AND status = 'RESERVED'",
                new { Pattern = codePrefix + "%" }, tx);

            int maxFromReservations = 0;
            if (!string.IsNullOrEmpty(maxFromReservationsCode) && maxFromReservationsCode.Length >= 3)
            {
                string suffix = maxFromReservationsCode.Substring(maxFromReservationsCode.Length - 3);
                if (int.TryParse(suffix, out int s)) maxFromReservations = s;
            }

            int nextSeq = Math.Max(maxFromItems, maxFromReservations) + 1;
            string newCode = $"{codePrefix}{nextSeq:D3}";

            // Verifica che non esista già in codex_items, se sì incrementa
            while (conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM codex_items WHERE codice = @Code",
                new { Code = newCode }, tx) > 0)
            {
                nextSeq++;
                newCode = $"{codePrefix}{nextSeq:D3}";
            }

            // Inserisci prenotazione
            int reservationId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_reservations (prefix, reserved_code, reserved_by, reserved_at, expires_at, status)
                VALUES (@Prefix, @Code, @User, NOW(), @Expires, 'RESERVED');
                SELECT LAST_INSERT_ID()",
                new
                {
                    Prefix = prefix,
                    Code = newCode,
                    User = userName,
                    Expires = DateTime.Now.Add(ReservationTtl)
                }, tx);

            tx.Commit();

            conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)",
                new { LockKey = $"codex_reserve_{prefix}" });

            _log.LogInformation("[CodexGenerator] Reserved {Code} (reservation #{Id})", newCode, reservationId);
            return (newCode, reservationId);
        }
        catch
        {
            tx.Rollback();
            try { conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{prefix}" }); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Rilascia una prenotazione senza confermarla.
    /// </summary>
    public void ReleaseReservation(int reservationId)
    {
        using var conn = _db.Open();
        conn.Execute(@"
            UPDATE codex_reservations SET status = 'RELEASED'
            WHERE id = @Id AND status = 'RESERVED'",
            new { Id = reservationId });

        _log.LogInformation("[CodexGenerator] Released reservation #{Id}", reservationId);
    }

    /// <summary>
    /// Conferma una prenotazione: inserisce fisicamente in codex_items.
    /// </summary>
    public (string Code, int ItemId) ConfirmReservation(int reservationId, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // Leggi la prenotazione
            var reservation = conn.QueryFirstOrDefault<(string reserved_code, string prefix, string status)>(@"
                SELECT reserved_code, prefix, status
                FROM codex_reservations WHERE id = @Id",
                new { Id = reservationId }, tx);

            if (reservation.reserved_code == null)
                throw new InvalidOperationException("Prenotazione non trovata");

            if (reservation.status != "RESERVED")
                throw new InvalidOperationException($"Prenotazione non valida (status: {reservation.status})");

            // Inserisci in codex_items
            int newId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_items 
                (remote_id, codice, descr, code_forn, fornitore, prezzo_forn, iva, 
                 produttore, data, note, categoria, barcode, tipologia, 
                 extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn)
                VALUES 
                (0, @Code, @Descr, '', '', 0, '', '', @Date, '', '', '', '', '', '', '', '', '', 0, '', '', '');
                SELECT LAST_INSERT_ID()",
                new { Code = reservation.reserved_code, Descr = description, Date = DateTime.Today }, tx);

            // Segna come confermata (verrà cancellata dalla pulizia giornaliera)
            conn.Execute(@"
                UPDATE codex_reservations SET status = 'CONFIRMED'
                WHERE id = @Id",
                new { Id = reservationId }, tx);

            tx.Commit();

            _log.LogInformation("[CodexGenerator] Confirmed {Code} → codex_items #{ItemId}", reservation.reserved_code, newId);
            return (reservation.reserved_code, newId);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public List<(string Code, string Description)> GetAvailablePrefixes()
    {
        return new List<(string, string)>
        {
            ("101", "Particolare a disegno generico"),
            ("102", "Particolare a disegno stampato"),
            ("201", "Particolare commerciale generico"),
            ("202", "Particolare commerciale rilavorato"),
            ("203", "Particolare commerciale robot"),
            ("301", "Elemento di fissaggio"),
            ("401", "Materia prima"),
            ("501", "Gruppo meccanico generico"),
            ("601", "Assieme meccanico generico"),
            ("701", "Layout meccanico generico")
        };
    }
}