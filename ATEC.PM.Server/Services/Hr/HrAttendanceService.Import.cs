using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Import» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── IMPORT DA ECOS ────────────────────────────────────────────────────────

    public async Task<HrImportResultDto> ImportAsync(bool full, CancellationToken ct = default)
    {
        if (!_ecos.Configured)
            return Fallito("Credenziali Ecos non configurate (sezione Ecos di appsettings): import impossibile.");

        if (!await _gate.WaitAsync(0, ct))
            return Fallito("Import già in corso.");

        ImportInProgress = true;
        ProgressoInizio(full ? "Import completo" : "Import incrementale");
        try
        {
            DateTime inizio = DateTime.Now;

            using MySqlConnection c = _db.Open();
            DateTime? cursore = full ? null : LeggiCursore(c);
            ProgressoLog(cursore == null
                ? "Nessun cursore: si scarica tutto lo storico"
                : $"Dal cursore: timbrature modificate dal {cursore:dd/MM/yyyy HH:mm}");

            ProgressoFase("[1/4] Richiesta token…", 10);
            string token = await _ecos.TokenAsync(ct);
            ProgressoLog("✅ Token ottenuto");

            ProgressoFase("[2/4] Scaricamento timbrature…", 25);
            List<EcosPunch> timbrature = await _ecos.GetPunchesAsync(token, cursore, ct, ProgressoLog);
            ProgressoScaricate(timbrature.Count);

            // Import assenze approvate da Ecos
            ProgressoFase("[3/4] Scaricamento assenze approvate…", 45);
            List<EcosAbsenceRequest> assenze = new();
            try
            {
                assenze = await _ecos.GetAbsenceRequestsAsync(token, cursore, ct);
                ProgressoLog($"✅ {assenze.Count} richieste assenza scaricate");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HR] Scarico assenze Ecos non riuscito: {Msg}", ex.Message);
                ProgressoLog($"⚠ Assenze non scaricate: {ex.Message} — l'import delle timbrature prosegue");
            }

            ProgressoFase("[4/4] Confronto e scrittura…", 60);
            HrImportResultDto esito = ImportPunches(c, timbrature, full);

            if (assenze.Count > 0)
            {
                var (absNuove, absAggiornate) = SyncAbsences(c, assenze);
                if (absNuove > 0 || absAggiornate > 0)
                {
                    esito.Message += $"; assenze Ecos: {absNuove} nuove, {absAggiornate} aggiornate";
                }
            }

            ScriviCursore(c, NuovoCursore(timbrature, inizio));
            LastImport = inizio;
            LastResult = esito.Message;
            _logger.LogInformation("[HR] Import Ecos completato: {Msg}", esito.Message);
            ProgressoFine($"=== COMPLETATO === {esito.Message}", esito);
            return esito;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] Import Ecos fallito: {Msg}", ex.Message);
            LastResult = $"ERRORE: {ex.Message}";
            ProgressoFine($"❌ ERRORE: {ex.Message}", null);
            return Fallito(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[HR] Import fallito: {Msg}", ex.Message);
            LastResult = $"ERRORE: {ex.Message}";
            ProgressoFine($"❌ ERRORE: {ex.Message}", null);
            return Fallito($"Import fallito: {ex.Message}");
        }
        finally
        {
            ImportInProgress = false;
            lock (_progressoLock) _progresso.Running = false;
            _gate.Release();
        }
    }

    private void ProgressoScaricate(int quante)
    {
        lock (_progressoLock)
        {
            _progresso.Downloaded = quante;
            AggiungiRiga($"✅ {quante} timbrature scaricate");
        }
    }

    // ── RISINCRONIZZAZIONE MIRATA (PIANO-HR-PORT-ORIGINALE.md, B1 e B2) ───────

    /// <summary>
    /// Riscarica da Ecos una <b>finestra</b> di calendario e la rimette in pari: un giorno di
    /// una persona (voce 2, port di <c>btnResyncDay_Click</c>) o un mese intero (voce 5).
    ///
    /// <para>Si scarica il mese con il filtro <c>YearMonth</c> — come faceva il VB — quindi
    /// dentro quella finestra si ha la <b>fotografia completa</b>: le righe che su Ecos non
    /// ci sono più si tolgono anche qui, come fa l'import <i>completo</i> e non
    /// l'incrementale (che quella fotografia non ce l'ha).</para>
    ///
    /// <para>🪤 <b>Il cursore non si tocca.</b> È un ripescaggio mirato, non un avanzamento
    /// dell'import: spostarlo aprirebbe una finestra cieca sul mese corrente. È l'errore che
    /// l'originale faceva (riscriveva <c>last_stamp_sync</c> anche dopo una sync forzata di
    /// un mese passato).</para>
    ///
    /// <para>🪤 Le timbrature si <b>inseriscono e aggiornano</b> su tutto il mese scaricato,
    /// ma si <b>cancellano</b> solo dentro la finestra chiesta: così una timbratura che Ecos
    /// ha spostato in un altro giorno viene ricollocata invece di sparire.</para>
    /// </summary>
    /// <param name="conAssenze">
    /// Vero per la sincronizzazione di un mese: si riscaricano anche le richieste di assenza
    /// approvate, come faceva <c>btnCarica_Click</c> del VB (timbrature <b>e</b> assenze).
    /// Per la singola giornata resta falso, fedele a <c>btnResyncDay_Click</c>, che le
    /// assenze non le toccava.
    /// </param>
    public async Task<HrImportResultDto> ImportWindowAsync(
        int? employeeId, DateTime dal, DateTime al, CancellationToken ct = default,
        bool conAssenze = false)
    {
        if (!_ecos.Configured)
            return Fallito("Credenziali Ecos non configurate: risincronizzazione impossibile.");

        dal = dal.Date;
        al = al.Date;
        if (al < dal) (dal, al) = (al, dal);
        if ((al - dal).TotalDays > 366)
            return Fallito("Si risincronizza al massimo un anno per volta.");

        if (!await _gate.WaitAsync(0, ct))
            return Fallito("Import già in corso.");

        ImportInProgress = true;
        string titolo = dal == al
            ? $"Risincronizzazione del {dal:dd/MM/yyyy}"
            : $"Sincronizzazione dal {dal:dd/MM/yyyy} al {al:dd/MM/yyyy}";
        ProgressoInizio(titolo);
        try
        {
            using MySqlConnection c = _db.Open();

            string? codiceEcos = null;
            if (employeeId is { } id)
            {
                codiceEcos = c.ExecuteScalar<string?>(
                    "SELECT ecos_empl_code FROM employees WHERE id = @Id", new { Id = id });
                if (string.IsNullOrWhiteSpace(codiceEcos))
                {
                    ProgressoFine("❌ Dipendente non collegato a Ecos", null);
                    return Fallito("Dipendente non collegato a Ecos: non ha timbrature da riscaricare.");
                }
            }

            ProgressoFase("[1/3] Richiesta token…", 10);
            string token = await _ecos.TokenAsync(ct);
            ProgressoLog("✅ Token ottenuto");

            ProgressoFase("[2/3] Scaricamento timbrature del periodo…", 30);
            var timbrature = new List<EcosPunch>();
            foreach ((int anno, int mese) in MesiDellIntervallo(dal, al))
            {
                ProgressoLog($"  {NomiMesi[mese - 1]} {anno}…");
                timbrature.AddRange(await _ecos.GetPunchesMonthAsync(token, anno, mese, ct, ProgressoLog));
            }

            // 🪤 Zero righe dall'intero mese non è un mese vuoto: è molto più probabile che
            // il filtro non abbia funzionato o che Ecos abbia risposto a vuoto. Qui dentro
            // vale la fotografia completa, quindi proseguire vorrebbe dire cancellare il
            // mese di TUTTI in silenzio. Ci si ferma senza toccare niente.
            //
            // La rete vale SOLO per la finestra larga (tutti i dipendenti). Quando la
            // persona è indicata — «Risincronizza questo giorno» — la cancellazione è già
            // ristretta a lei e a quel giorno, e fermarsi tradirebbe proprio quello che
            // l'utente ha chiesto: togliere la timbratura che su Ecos non c'è più.
            if (timbrature.Count == 0 && employeeId is null)
            {
                ProgressoFine("Nessuna timbratura ricevuta da Ecos: niente è stato modificato.", null);
                return new HrImportResultDto
                {
                    Success = true,
                    Message = "Ecos non ha restituito nessuna timbratura per il periodo: "
                              + "niente è stato modificato.",
                };
            }

            // Il mese scaricato serve intero per gli aggiornamenti; se si chiede una sola
            // persona però si tengono solo le sue, altrimenti la finestra cancellerebbe
            // righe altrui che non sono state chieste.
            if (codiceEcos != null)
            {
                timbrature = timbrature
                    .Where(t => string.Equals(t.EmplCode, codiceEcos, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            ProgressoScaricate(timbrature.Count);

            ProgressoFase("[3/3] Confronto e scrittura…", 60);
            HrImportResultDto esito = ImportPunches(
                c, timbrature, full: false, finestra: new FinestraImport(employeeId, dal, al));

            if (conAssenze)
            {
                // Le assenze non hanno un filtro di periodo sull'API: si scarica e si tiene
                // ciò che tocca la finestra. L'upsert è per ecos_absence_id, quindi rifarlo
                // non duplica. Se Ecos non risponde, le timbrature restano importate.
                try
                {
                    List<EcosAbsenceRequest> assenze = await _ecos.GetAbsenceRequestsAsync(token, null, ct);
                    List<EcosAbsenceRequest> nellaFinestra = assenze
                        .Where(a => a.DateBegin.Date <= al && a.DateEnd.Date >= dal)
                        .ToList();

                    if (nellaFinestra.Count > 0)
                    {
                        var (absNuove, absAggiornate) = SyncAbsences(c, nellaFinestra);
                        ProgressoLog($"✅ assenze del periodo: {absNuove} nuove, {absAggiornate} aggiornate");
                        if (absNuove > 0 || absAggiornate > 0)
                            esito.Message += $"; assenze Ecos: {absNuove} nuove, {absAggiornate} aggiornate";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[HR] Scarico assenze Ecos non riuscito: {Msg}", ex.Message);
                    ProgressoLog($"⚠ Assenze non scaricate: {ex.Message} — le timbrature sono state importate");
                }
            }

            LastImport = DateTime.Now;
            LastResult = esito.Message;
            _logger.LogInformation(
                "[HR] {Titolo}: {Msg} (il cursore non è stato toccato).", titolo, esito.Message);
            ProgressoFine($"=== COMPLETATO === {esito.Message}", esito);
            return esito;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] {Titolo} fallita: {Msg}", titolo, ex.Message);
            LastResult = $"ERRORE: {ex.Message}";
            ProgressoFine($"❌ ERRORE: {ex.Message}", null);
            return Fallito(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[HR] {Titolo} fallita: {Msg}", titolo, ex.Message);
            LastResult = $"ERRORE: {ex.Message}";
            ProgressoFine($"❌ ERRORE: {ex.Message}", null);
            return Fallito($"Risincronizzazione fallita: {ex.Message}");
        }
        finally
        {
            ImportInProgress = false;
            lock (_progressoLock) _progresso.Running = false;
            _gate.Release();
        }
    }

    /// <summary>I mesi di calendario toccati dall'intervallo: Ecos filtra per <c>YearMonth</c>.</summary>
    internal static IEnumerable<(int Anno, int Mese)> MesiDellIntervallo(DateTime dal, DateTime al)
    {
        var corrente = new DateTime(dal.Year, dal.Month, 1);
        var fine = new DateTime(al.Year, al.Month, 1);
        while (corrente <= fine)
        {
            yield return (corrente.Year, corrente.Month);
            corrente = corrente.AddMonths(1);
        }
    }

    /// <summary>
    /// La finestra di calendario su cui vale la fotografia completa: dentro si può cancellare
    /// quello che Ecos non ha più, fuori no.
    /// </summary>
    internal sealed record FinestraImport(int? EmployeeId, DateTime Dal, DateTime Al);

    internal (int Added, int Updated) SyncAbsences(
        MySqlConnection c, IReadOnlyList<EcosAbsenceRequest> requests)
    {
        Dictionary<string, int> mappa = MappaEcos(c);
        int added = 0, updated = 0;

        foreach (EcosAbsenceRequest r in requests)
        {
            if (!mappa.TryGetValue(r.EmplCode, out int employeeId))
                continue;

            string absenceType = r.CategoryCode switch
            {
                "F" => "VACATION",
                "P" => "PERMIT",
                "M" or "MA" => "SICKNESS",
                "I" or "IN" => "INJURY",
                _ => "OTHER",
            };

            string status = r.StatusCode switch
            {
                "ACCEPTED" => "APPROVED",
                "REJECTED" => "REJECTED",
                "CANCELLED" => "CANCELLED",
                _ => "PENDING",
            };

            decimal? hours = r.FullDay ? null : r.Duration;

            var existing = c.QueryFirstOrDefault<(int Id, string Status, decimal? Hours, DateTime DateFrom, DateTime DateTo)>(
                "SELECT id, status, hours, date_from, date_to FROM hr_absences WHERE ecos_absence_id = @EcosId",
                new { EcosId = r.AbsenceRequestId });

            if (existing == default)
            {
                c.Execute(@"
                    INSERT INTO hr_absences
                        (employee_id, date_from, date_to, hours, is_full_day, absence_type, status, source, ecos_absence_id, notes)
                    VALUES
                        (@EmployeeId, @DateFrom, @DateTo, @Hours, @IsFullDay, @AbsenceType, @Status, 'ECOS', @EcosId, @Notes)",
                    new
                    {
                        EmployeeId = employeeId,
                        DateFrom = r.DateBegin,
                        DateTo = r.DateEnd,
                        Hours = hours,
                        IsFullDay = r.FullDay,
                        AbsenceType = absenceType,
                        Status = status,
                        EcosId = r.AbsenceRequestId,
                        Notes = string.IsNullOrWhiteSpace(r.CategoryDesc) ? null : $"ECOS: {r.CategoryDesc}"
                    });
                added++;
            }
            else if (existing.Status != status || existing.Hours != hours || existing.DateFrom != r.DateBegin || existing.DateTo != r.DateEnd)
            {
                c.Execute(@"
                    UPDATE hr_absences
                    SET date_from = @DateFrom, date_to = @DateTo, hours = @Hours, is_full_day = @IsFullDay,
                        absence_type = @AbsenceType, status = @Status
                    WHERE id = @Id",
                    new
                    {
                        DateFrom = r.DateBegin,
                        DateTo = r.DateEnd,
                        Hours = hours,
                        IsFullDay = r.FullDay,
                        AbsenceType = absenceType,
                        Status = status,
                        Id = existing.Id
                    });
                updated++;
            }

            SyncToResourcePlanner(c, employeeId, r.DateBegin, r.DateEnd, absenceType, isApproved: status == "APPROVED");
        }

        return (added, updated);
    }

    internal HrImportResultDto ImportPunches(
        MySqlConnection c, IReadOnlyList<EcosPunch> timbrature, bool full = false,
        FinestraImport? finestra = null)
    {
        Dictionary<string, int> mappa = MappaEcos(c);

        var perId = new Dictionary<string, EcosPunch>(StringComparer.OrdinalIgnoreCase);
        var nonAbbinati = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EcosPunch t in timbrature)
        {
            if (!mappa.ContainsKey(t.EmplCode)) nonAbbinati.Add($"{t.EmplCode} — {t.Name}");
            perId[t.ExternalId] = t;
        }

        var esistenti = new Dictionary<string, RigaEsistente>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] blocco in ABlocchi(perId.Keys, 500))
        {
            foreach (RigaEsistente riga in c.Query<RigaEsistente>(
                @"SELECT external_id AS ExternalId, id AS Id, employee_id AS EmployeeId,
                         work_date AS WorkDate, punched_at AS PunchedAt, direction AS Direction, location AS Location
                  FROM hr_punches
                  WHERE source = 'ECOS' AND external_id IN @Ids",
                new { Ids = blocco }))
            {
                if (riga.ExternalId != null)
                    esistenti[riga.ExternalId] = riga;
            }
        }

        int nuove = 0, aggiornate = 0, rimosse = 0;
        var giorniToccati = new HashSet<(int EmployeeId, DateTime WorkDate)>();
        var daRifare = new HashSet<(int EmployeeId, DateTime WorkDate)>();

        using (MySqlTransaction tran = c.BeginTransaction())
        {
            foreach (EcosPunch t in perId.Values)
            {
                bool mappato = mappa.TryGetValue(t.EmplCode, out int employeeIdMappato);
                DateTime work_date = t.PunchedAt.Date;

                if (!esistenti.TryGetValue(t.ExternalId, out RigaEsistente? vecchia))
                {
                    if (!mappato) continue;

                    c.Execute(@"
                        INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id, location)
                        VALUES (@EmployeeId, @WorkDate, @PunchedAt, @Direction, 'ECOS', @ExternalId, @Location)",
                        new { EmployeeId = employeeIdMappato, WorkDate = work_date, t.PunchedAt, t.Direction, t.ExternalId, t.Location },
                        tran);
                    nuove++;
                    SegnaConVicine(giorniToccati, daRifare, employeeIdMappato, work_date);
                    continue;
                }

                int employeeId = mappato ? employeeIdMappato : vecchia.EmployeeId;
                if (vecchia.PunchedAt != t.PunchedAt
                    || !string.Equals(vecchia.Direction, t.Direction, StringComparison.OrdinalIgnoreCase)
                    || vecchia.EmployeeId != employeeId
                    || !string.Equals(vecchia.Location ?? "", t.Location ?? "", StringComparison.Ordinal))
                {
                    c.Execute(@"
                        UPDATE hr_punches
                        SET employee_id = @EmployeeId, work_date = @WorkDate, punched_at = @PunchedAt,
                            direction = @Direction, location = @Location
                        WHERE id = @Id",
                        new { EmployeeId = employeeId, WorkDate = work_date, t.PunchedAt, t.Direction, t.Location, vecchia.Id },
                        tran);
                    aggiornate++;
                    SegnaConVicine(giorniToccati, daRifare, vecchia.EmployeeId, vecchia.WorkDate);
                    SegnaConVicine(giorniToccati, daRifare, employeeId, work_date);
                }
            }

            if (finestra != null)
                rimosse = RimuoviSpariteNellaFinestra(c, tran, perId.Keys, finestra, giorniToccati, daRifare);
            else if (full)
                rimosse = RimuoviCancellateSuEcos(c, tran, perId.Keys, mappa.Values, giorniToccati, daRifare);

            tran.Commit();
        }

        foreach (var sospesa in c.Query<(int EmployeeId, DateTime WorkDate)>(
            "SELECT employee_id AS EmployeeId, work_date AS WorkDate FROM hr_days WHERE note = 'Giornata in corso' AND work_date < CURDATE()"))
        {
            giorniToccati.Add(sospesa);
            daRifare.Add(sospesa);
        }

        // 🪤 Si RIFANNO anche le vicine — una timbratura che arriva oggi può chiudere la
        // notte di ieri — ma il numero che si mostra è quello delle giornate cambiate
        // davvero: contando anche le vicine, un import di dieci giornate ne dichiarerebbe
        // trenta.
        int ricalcolate = 0;
        foreach ((int employeeId, DateTime work_date) in daRifare)
        {
            bool scritta = RecalculateDay(c, employeeId, work_date);
            if (scritta && giorniToccati.Contains((employeeId, work_date))) ricalcolate++;
        }

        int riparate = RepairDays(c);

        // Le rimosse non stanno nel DTO di esito: le si porta a video da qui, altrimenti il
        // contatore dell'avanzamento resterebbe a zero anche quando qualcosa è stato tolto.
        lock (_progressoLock) _progresso.Removed = rimosse;

        string messaggio =
            $"{nuove} timbrature nuove, {aggiornate} aggiornate, {ricalcolate} giornate ricalcolate"
            + (rimosse > 0 ? $", {rimosse} cancellate su Ecos rimosse" : "")
            + (riparate > 0 ? $", {riparate} giornate rimesse in pari" : "")
            + (nonAbbinati.Count > 0 ? $"; {nonAbbinati.Count} codici Ecos senza dipendente collegato" : "");

        return new HrImportResultDto
        {
            Success = true,
            Message = messaggio,
            PunchesAdded = nuove,
            PunchesUpdated = aggiornate,
            DaysRecalculated = ricalcolate + riparate,
            Unmatched = nonAbbinati.ToList(),
        };
    }

    /// <summary>
    /// Dentro la finestra chiesta si ha la fotografia completa di Ecos: quello che là non
    /// c'è più si toglie anche qui. Tocca <b>solo</b> le righe <c>ECOS</c>: le rettifiche
    /// (<c>source='ADJUSTMENT'</c>) sono nostre e non si cancellano mai da qui.
    /// </summary>
    private int RimuoviSpariteNellaFinestra(
        MySqlConnection c, MySqlTransaction tran,
        IEnumerable<string> idVisti, FinestraImport finestra,
        HashSet<(int, DateTime)> giorniToccati, HashSet<(int, DateTime)> daRifare)
    {
        var visti = new HashSet<string>(idVisti, StringComparer.OrdinalIgnoreCase);

        string filtroDipendente = finestra.EmployeeId.HasValue ? " AND employee_id = @EmployeeId" : "";
        List<RigaEsistente> nostre = c.Query<RigaEsistente>(
            @"SELECT id AS Id, external_id AS ExternalId, employee_id AS EmployeeId, work_date AS WorkDate,
                     punched_at AS PunchedAt, direction AS Direction, location AS Location
              FROM hr_punches
              WHERE source = 'ECOS' AND work_date BETWEEN @Dal AND @Al" + filtroDipendente,
            new { finestra.Dal, finestra.Al, finestra.EmployeeId }, tran).ToList();

        List<RigaEsistente> sparite = nostre
            .Where(r => r.ExternalId != null && !visti.Contains(r.ExternalId))
            .ToList();
        if (sparite.Count == 0) return 0;

        foreach (long[] blocco in ABlocchi(sparite.Select(r => r.Id), 500))
            c.Execute("DELETE FROM hr_punches WHERE id IN @Ids", new { Ids = blocco }, tran);

        foreach (RigaEsistente r in sparite)
            SegnaConVicine(giorniToccati, daRifare, r.EmployeeId, r.WorkDate);

        _logger.LogInformation(
            "[HR] Risincronizzazione {Dal:dd/MM/yyyy}-{Al:dd/MM/yyyy}: {N} timbrature non più su Ecos rimosse.",
            finestra.Dal, finestra.Al, sparite.Count);
        return sparite.Count;
    }

    private int RimuoviCancellateSuEcos(
        MySqlConnection c, MySqlTransaction tran,
        IEnumerable<string> idVisti, IEnumerable<int> dipendentiMappati,
        HashSet<(int, DateTime)> giorniToccati, HashSet<(int, DateTime)> daRifare)
    {
        var visti = new HashSet<string>(idVisti, StringComparer.OrdinalIgnoreCase);
        int[] dipendenti = dipendentiMappati.Distinct().ToArray();
        if (dipendenti.Length == 0) return 0;

        List<RigaEsistente> nostre = c.Query<RigaEsistente>(
            @"SELECT id AS Id, external_id AS ExternalId, employee_id AS EmployeeId, work_date AS WorkDate,
                     punched_at AS PunchedAt, direction AS Direction, location AS Location
              FROM hr_punches
              WHERE source = 'ECOS' AND employee_id IN @Dipendenti",
            new { Dipendenti = dipendenti }, tran).ToList();

        List<RigaEsistente> sparite = nostre
            .Where(r => r.ExternalId != null && !visti.Contains(r.ExternalId))
            .ToList();
        if (sparite.Count == 0) return 0;

        foreach (long[] blocco in ABlocchi(sparite.Select(r => r.Id), 500))
            c.Execute("DELETE FROM hr_punches WHERE id IN @Ids", new { Ids = blocco }, tran);

        foreach (RigaEsistente r in sparite)
            SegnaConVicine(giorniToccati, daRifare, r.EmployeeId, r.WorkDate);

        _logger.LogInformation(
            "[HR] {N} timbrature cancellate su Ecos rimosse anche qui (import full).", sparite.Count);
        return sparite.Count;
    }

    public int RepairDays(MySqlConnection c)
    {
        List<(int EmployeeId, DateTime WorkDate)> daRifare = c.Query<(int, DateTime)>(
            @"SELECT t.employee_id, t.work_date
              FROM (SELECT employee_id, work_date, MAX(created_at) AS ultima
                    FROM hr_punches GROUP BY employee_id, work_date) t
              LEFT JOIN hr_days g
                     ON g.employee_id = t.employee_id AND g.work_date = t.work_date
              WHERE g.id IS NULL
                 OR g.rules_version < @Versione
                 -- 🪤 Non basta guardare le timbrature della giornata: con il terzo turno
                 -- una giornata dipende anche da quelle del giorno prima e del giorno dopo.
                 -- Se il ricalcolo a catena salta (un riavvio nel mezzo), è questa la rete.
                 OR g.calculated_at < (SELECT MAX(p.created_at)
                                         FROM hr_punches p
                                        WHERE p.employee_id = t.employee_id
                                          AND p.work_date BETWEEN DATE_SUB(t.work_date, INTERVAL 1 DAY)
                                                              AND DATE_ADD(t.work_date, INTERVAL 1 DAY))
              ORDER BY t.work_date DESC
              LIMIT @Limite",
            new { Versione = TimesheetRules.Version, Limite = MaxDaysToRepair }).ToList();

        foreach ((int employeeId, DateTime work_date) in daRifare)
            RecalculateDay(c, employeeId, work_date);

        int orfani = c.Execute(
            @"DELETE g FROM hr_days g
              LEFT JOIN hr_punches t
                     ON t.employee_id = g.employee_id AND t.work_date = g.work_date
              WHERE t.id IS NULL");

        int totale = daRifare.Count + orfani;
        if (totale > 0)
            _logger.LogInformation(
                "[HR] Rimesse in pari {N} giornate ({Orfane} cartellini orfani rimossi).", totale, orfani);
        return totale;
    }

    // ── RICALCOLO GIORNATE ────────────────────────────────────────────────────

    /// <returns>
    /// true se la giornata ha prodotto un cartellino; false se non c'era niente da
    /// calcolare (giorno senza timbrature: il cartellino, se c'era, viene tolto).
    /// </returns>
    public bool RecalculateDay(MySqlConnection c, int employeeId, DateTime work_date)
    {
        List<RawPunch> grezze = c.Query<(DateTime PunchedAt, string Direction)>(
                @"SELECT punched_at AS PunchedAt, direction AS Direction
                  FROM hr_punches
                  WHERE employee_id = @EmployeeId AND work_date = @WorkDate
                  ORDER BY punched_at",
                new { EmployeeId = employeeId, WorkDate = work_date.Date })
            .Select(t => new RawPunch(t.PunchedAt, t.Direction))
            .ToList();

        if (grezze.Count == 0)
        {
            c.Execute("DELETE FROM hr_days WHERE employee_id = @EmployeeId AND work_date = @WorkDate",
                new { EmployeeId = employeeId, WorkDate = work_date.Date });
            return false;
        }

        bool countsOvertime = c.ExecuteScalar<bool?>(
                "SELECT hr_counts_overtime FROM employees WHERE id = @EmployeeId",
                new { EmployeeId = employeeId })
            ?? true;
        TimesheetDay cart = TimesheetEngine.Calcola(
            work_date.Date, grezze, DateTime.Today, new TimesheetEngine.EmployeeConfig(countsOvertime),
            NightContextOf(c, employeeId, work_date.Date));

        c.Execute(@"
            INSERT INTO hr_days
                (employee_id, work_date, clock_in_1, clock_out_1, clock_in_2, clock_out_2,
                 regular_minutes, overtime_minutes, break_minutes, bands_json, note, has_anomaly,
                 calculated_at, rules_version)
            VALUES
                (@EmployeeId, @WorkDate, @ClockIn1, @ClockOut1, @ClockIn2, @ClockOut2,
                 @RegularMinutes, @OvertimeMinutes, @BreakMinutes, @BandsJson, @Note, @HasAnomaly,
                 NOW(), @RulesVersion)
            ON DUPLICATE KEY UPDATE
                clock_in_1 = VALUES(clock_in_1), clock_out_1 = VALUES(clock_out_1),
                clock_in_2 = VALUES(clock_in_2), clock_out_2 = VALUES(clock_out_2),
                regular_minutes = VALUES(regular_minutes), overtime_minutes = VALUES(overtime_minutes),
                break_minutes = VALUES(break_minutes), bands_json = VALUES(bands_json),
                note = VALUES(note), has_anomaly = VALUES(has_anomaly),
                calculated_at = NOW(), rules_version = VALUES(rules_version)",
            new
            {
                EmployeeId = employeeId,
                WorkDate = work_date.Date,
                ClockIn1 = cart.Entrata1,
                ClockOut1 = cart.Uscita1,
                ClockIn2 = cart.Entrata2,
                ClockOut2 = cart.Uscita2,
                RegularMinutes = MinutesFrom(cart.RegularHours),
                OvertimeMinutes = MinutesFrom(cart.Overtime),
                BreakMinutes = MinutesFrom(cart.BreakTime),
                BandsJson = BandsJson(cart),
                cart.Note,
                cart.HasAnomaly,
                RulesVersion = TimesheetRules.Version,
            });

        return true;
    }

    /// <summary>
    /// Le due timbrature che confinano con la giornata: l'ultima di ieri e la prima di
    /// domani. Servono a <see cref="NightShift"/> per capire se un'entrata serale o
    /// un'uscita mattutina sono i due monconi dello stesso turno di notte.
    ///
    /// <para>Una query sola: <c>RecalculateDay</c> gira in ciclo su tutte le giornate da
    /// rimettere in pari, e due andate e ritorno per giornata si sentirebbero.</para>
    /// </summary>
    private static NightContext NightContextOf(MySqlConnection c, int employeeId, DateTime work_date)
    {
        DateTime ieri = work_date.Date.AddDays(-1);
        DateTime domani = work_date.Date.AddDays(1);

        List<(DateTime WorkDate, DateTime PunchedAt, string Direction)> vicine =
            c.Query<(DateTime, DateTime, string)>(
                @"SELECT work_date AS WorkDate, punched_at AS PunchedAt, direction AS Direction
                    FROM hr_punches
                   WHERE employee_id = @EmployeeId AND work_date IN (@Prev, @Next)
                   ORDER BY punched_at",
                new { EmployeeId = employeeId, Prev = ieri, Next = domani }).ToList();

        List<RawPunch> Del(DateTime giorno) => vicine
            .Where(v => v.WorkDate.Date == giorno)
            .Select(v => new RawPunch(v.PunchedAt, v.Direction))
            .ToList();

        return new NightContext(Del(ieri), Del(domani));
    }

    /// <summary>
    /// Lo stesso contesto di <see cref="NightContextOf"/>, ma preso da timbrature che si
    /// hanno già in mano invece che dal database: lo usa il cartellino mensile, che ripassa
    /// nel motore un mese intero e non può permettersi due query per giornata.
    ///
    /// <para>Le liste arrivano ordinate per orario dalla query che le ha caricate.</para>
    /// </summary>
    private static NightContext ContestoNotte(
        IReadOnlyDictionary<DateTime, List<PunchRow>> perGiorno, DateTime work_date)
    {
        List<RawPunch> Del(DateTime giorno) =>
            perGiorno.TryGetValue(giorno, out List<PunchRow>? righe)
                ? righe.Select(t => new RawPunch(t.PunchedAt, t.Direction)).ToList()
                : new List<RawPunch>();

        return new NightContext(Del(work_date.Date.AddDays(-1)), Del(work_date.Date.AddDays(1)));
    }

    /// <summary>
    /// Rifà la giornata <b>e le sue vicine</b>. Serve dopo una rettifica a mano: l'uscita
    /// delle 06:03 aggiunta oggi chiude la notte cominciata ieri, e il cartellino di ieri
    /// va rifatto anche se nessuno l'ha toccato.
    /// </summary>
    private void RicalcolaConVicine(MySqlConnection c, int employeeId, DateTime work_date)
    {
        RecalculateDay(c, employeeId, work_date.Date.AddDays(-1));
        RecalculateDay(c, employeeId, work_date.Date);
        RecalculateDay(c, employeeId, work_date.Date.AddDays(1));
    }

    /// <summary>
    /// Segna una giornata da ricalcolare <b>insieme alle sue vicine</b>: una timbratura che
    /// arriva oggi può chiudere la notte cominciata ieri (o aprire quella che finisce
    /// domani), e quelle giornate vanno rifatte anche se non le ha toccate nessuno.
    /// </summary>
    /// <param name="toccate">Le giornate cambiate davvero: è questo il numero da mostrare.</param>
    /// <param name="daRifare">Quelle da ricalcolare, vicine comprese.</param>
    private static void SegnaConVicine(
        HashSet<(int EmployeeId, DateTime WorkDate)> toccate,
        HashSet<(int EmployeeId, DateTime WorkDate)> daRifare,
        int employeeId,
        DateTime work_date)
    {
        toccate.Add((employeeId, work_date.Date));
        daRifare.Add((employeeId, work_date.Date));
        daRifare.Add((employeeId, work_date.Date.AddDays(-1)));
        daRifare.Add((employeeId, work_date.Date.AddDays(1)));
    }
}
