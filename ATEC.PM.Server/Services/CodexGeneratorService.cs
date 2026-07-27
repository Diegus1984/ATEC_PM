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

            // Calcola prossimo codice considerando codex_items (ENTRAMBE le colonne: la
            // ricodifica manuale assegna codici nuovi della stessa famiglia+giorno in
            // codice_nuovo, e i due spazi non devono mai collidere) e le prenotazioni attive.
            string today = DateTime.Now.ToString("ddMMyy");
            string codePrefix = $"{prefix}{today}";

            int maxSeq = conn.ExecuteScalar<int?>(@"
                SELECT MAX(CAST(RIGHT(x.code, 3) AS UNSIGNED)) FROM (
                    SELECT codice AS code FROM codex_items WHERE codice LIKE @Pattern
                    UNION ALL
                    SELECT codice_nuovo FROM codex_items WHERE codice_nuovo LIKE @Pattern
                    UNION ALL
                    SELECT reserved_code FROM codex_reservations
                    WHERE reserved_code LIKE @Pattern AND status = 'RESERVED'
                ) x", new { Pattern = codePrefix + "%" }, tx) ?? 0;

            int nextSeq = maxSeq + 1;
            string newCode = $"{codePrefix}{nextSeq:D3}";

            // Verifica che non esista già (codice o codice_nuovo), se sì incrementa
            while (conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM codex_items WHERE codice = @Code OR codice_nuovo = @Code",
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
            try { conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{prefix}" }); } 
            catch (Exception ex) { _log.LogDebug(ex, "Impossibile rilasciare il lock codex_reserve_{Prefix} durante il rollback", prefix); }
            throw;
        }
    }

    /// <summary>
    /// Prenota il prossimo codice per la RICODIFICA (colonna codice_nuovo): stessa meccanica
    /// di ReserveNextCode (GET_LOCK + codex_reservations, regola famiglia+ddMMyy+progressivo),
    /// ma il progressivo considera anche i codici nuovi già assegnati. Più operatori possono
    /// ricodificare in parallelo senza ricevere lo stesso codice: il codice resta prenotato
    /// (TTL 10 min) finché il dialog non salva (che libera la prenotazione) o annulla (release).
    /// </summary>
    public (string Code, int ReservationId) ReserveNextNewCode(string family, string userName)
    {
        if (string.IsNullOrWhiteSpace(family))
            throw new ArgumentException("Family is required", nameof(family));

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.ExecuteScalar("SELECT GET_LOCK(@LockKey, 30)",
                new { LockKey = $"codex_reserve_{family}" }, tx);

            // Pulisci prenotazioni di giorni precedenti o scadute (TTL passato).
            conn.Execute(@"DELETE FROM codex_reservations
                WHERE DATE(reserved_at) < CURDATE()
                   OR (status = 'RESERVED' AND expires_at < NOW())", transaction: tx);

            string codePrefix = $"{family}{DateTime.Now:ddMMyy}";

            int maxSeq = conn.ExecuteScalar<int?>(@"
                SELECT MAX(CAST(RIGHT(x.code, 3) AS UNSIGNED)) FROM (
                    SELECT codice AS code FROM codex_items WHERE codice LIKE @Pattern
                    UNION ALL
                    SELECT codice_nuovo FROM codex_items WHERE codice_nuovo LIKE @Pattern
                    UNION ALL
                    SELECT reserved_code FROM codex_reservations
                    WHERE reserved_code LIKE @Pattern AND status = 'RESERVED'
                ) x", new { Pattern = codePrefix + "%" }, tx) ?? 0;

            int nextSeq = maxSeq + 1;
            string newCode = $"{codePrefix}{nextSeq:D3}";
            while (conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM codex_items WHERE codice = @Code OR codice_nuovo = @Code",
                new { Code = newCode }, tx) > 0)
            {
                nextSeq++;
                newCode = $"{codePrefix}{nextSeq:D3}";
            }

            int reservationId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_reservations (prefix, reserved_code, reserved_by, reserved_at, expires_at, status)
                VALUES (@Prefix, @Code, @User, NOW(), @Expires, 'RESERVED');
                SELECT LAST_INSERT_ID()",
                new
                {
                    Prefix = family,
                    Code = newCode,
                    User = userName,
                    Expires = DateTime.Now.Add(ReservationTtl)
                }, tx);

            tx.Commit();

            conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)",
                new { LockKey = $"codex_reserve_{family}" });

            _log.LogInformation("[CodexGenerator] Reserved NEW code {Code} (reservation #{Id})", newCode, reservationId);
            return (newCode, reservationId);
        }
        catch
        {
            tx.Rollback();
            try { conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{family}" }); }
            catch (Exception ex) { _log.LogDebug(ex, "Impossibile rilasciare il lock codex_reserve_{Family} durante il rollback", family); }
            throw;
        }
    }

    /// <summary>
    /// Fase 1 dell'assegnazione MASSIVA (pagina Ricodifica): per le righe selezionate
    /// senza codice nuovo PRENOTA N codici progressivi della famiglia (regola
    /// famiglia+ddMMyy+seq), sotto lo stesso lock del generatore. Non scrive nulla su
    /// codex_items: ritorna l'anteprima vecchio→nuovo che l'operatore conferma nel form
    /// (fase 2, commit) o annulla (release). Le righe già ricodificate vengono SALTATE.
    /// </summary>
    public (List<(int ItemId, string OldCode, string Descr, string NewCode, int ReservationId)> Items, int Skipped)
        ReserveNextNewCodesBulk(string family, IReadOnlyCollection<int> ids, string userName)
    {
        if (string.IsNullOrWhiteSpace(family))
            throw new ArgumentException("Family is required", nameof(family));
        var result = new List<(int, string, string, string, int)>();
        if (ids.Count == 0) return (result, 0);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.ExecuteScalar("SELECT GET_LOCK(@LockKey, 30)",
                new { LockKey = $"codex_reserve_{family}" }, tx);

            conn.Execute(@"DELETE FROM codex_reservations
                WHERE DATE(reserved_at) < CURDATE()
                   OR (status = 'RESERVED' AND expires_at < NOW())", transaction: tx);

            // Selezionate ma già ricodificate → saltate (il conteggio torna all'operatore).
            var targets = conn.Query<(int Id, string Codice, string Descr)>(@"
                SELECT id, codice, COALESCE(descr,'') FROM codex_items
                WHERE id IN @Ids AND (codice_nuovo IS NULL OR codice_nuovo = '')
                ORDER BY codice, id", new { Ids = ids }, tx).ToList();
            int skipped = ids.Count - targets.Count;
            if (targets.Count == 0)
            {
                tx.Rollback();
                conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{family}" });
                return (result, skipped);
            }

            string codePrefix = $"{family}{DateTime.Now:ddMMyy}";
            int seq = conn.ExecuteScalar<int?>(@"
                SELECT MAX(CAST(RIGHT(x.code, 3) AS UNSIGNED)) FROM (
                    SELECT codice AS code FROM codex_items WHERE codice LIKE @Pattern
                    UNION ALL
                    SELECT codice_nuovo FROM codex_items WHERE codice_nuovo LIKE @Pattern
                    UNION ALL
                    SELECT reserved_code FROM codex_reservations
                    WHERE reserved_code LIKE @Pattern AND status = 'RESERVED'
                ) x", new { Pattern = codePrefix + "%" }, tx) ?? 0;

            DateTime expires = DateTime.Now.Add(ReservationTtl);
            foreach (var target in targets)
            {
                string newCode;
                do
                {
                    seq++;
                    newCode = $"{codePrefix}{seq:D3}";
                } while (conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM codex_items WHERE codice = @Code OR codice_nuovo = @Code",
                    new { Code = newCode }, tx) > 0);

                int reservationId = conn.ExecuteScalar<int>(@"
                    INSERT INTO codex_reservations (prefix, reserved_code, reserved_by, reserved_at, expires_at, status)
                    VALUES (@Prefix, @Code, @User, NOW(), @Expires, 'RESERVED');
                    SELECT LAST_INSERT_ID()",
                    new { Prefix = family, Code = newCode, User = userName, Expires = expires }, tx);

                result.Add((target.Id, target.Codice, target.Descr, newCode, reservationId));
            }

            tx.Commit();
            conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{family}" });

            _log.LogInformation("[CodexGenerator] Bulk reserve: {Count} codici prenotati (famiglia {Family}), {Skipped} saltati",
                result.Count, family, skipped);
            return (result, skipped);
        }
        catch
        {
            tx.Rollback();
            try { conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)", new { LockKey = $"codex_reserve_{family}" }); }
            catch (Exception ex) { _log.LogDebug(ex, "Impossibile rilasciare il lock codex_reserve_{Family} durante il rollback", family); }
            throw;
        }
    }

    /// <summary>
    /// Rilascia una prenotazione senza confermarla.
    /// </summary>
    public void ReleaseReservation(int reservationId)
    {
        using var conn = _db.Open();
        conn.Execute("DELETE FROM codex_reservations WHERE id = @Id", new { Id = reservationId });
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

            // Inserisci in codex_items. remote_id NULL = non ancora presente sul Codex remoto:
            // il sync lo riaggancerà per codice quando comparirà sul remoto (vedi CodexSyncService).
            // Famiglie nuove (201/211/221): codice_nuovo = codice fin dalla nascita — invariante
            // «codice ATEC = codice_nuovo» per mapping Danea / orfani / picker (migrazione v46).
            bool newFamily = reservation.prefix is "201" or "211" or "221";
            int newId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_items
                (remote_id, codice, codice_nuovo, descr, code_forn, fornitore, prezzo_forn, iva,
                 produttore, data, note, categoria, barcode, tipologia,
                 extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn)
                VALUES
                (NULL, @Code, @CodiceNuovo, @Descr, '', '', 0, '', '', @Date, '', '', '', '', '', '', '', '', '', 0, '', '', '');
                SELECT LAST_INSERT_ID()",
                new
                {
                    Code = reservation.reserved_code,
                    CodiceNuovo = newFamily ? reservation.reserved_code : null,
                    Descr = description,
                    Date = DateTime.Today,
                }, tx);

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
        // 201/211/221 = famiglie della NUOVA codifica commerciale (ampliamento 21/07/2026):
        // gli articoli nuovi nascono direttamente con queste famiglie dal generatore standard.
        return new List<(string, string)>
        {
            ("101", "Particolari a disegno"),
            ("201", "Commerciale generico"),
            ("211", "Commerciale elettrico"),
            ("221", "Commerciale pneumatico"),
            ("301", "Elemento di fissaggio"),
            ("401", "Materia prima"),
            ("501", "Gruppo meccanico"),
            ("601", "Assieme meccanico"),
            ("701", "Layout meccanico")
        };
    }
}