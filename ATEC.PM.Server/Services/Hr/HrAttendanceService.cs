using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il servizio del modulo HR (PIANO-HR-PRESENZE.md): importa timbrature e richieste assenza da Ecos,
/// calcola <c>hr_days</c> col <see cref="TimesheetEngine"/>, gestisce il workflow delle richieste ferie/permessi,
/// le rettifiche, la mappatura e la matrice mensile presenze.
/// </summary>
public class HrAttendanceService
{
    private const string CursoreKey = "hr_sync_punches_from";

    /// <summary>
    /// L'ultima lettura riuscita dell'anagrafica badge (voce 11 del port): sta in
    /// <c>app_config</c> perché è un'informazione che deve sopravvivere al riavvio.
    /// 🪤 Nell'originale il pulsante «Solo Badge» NON la scriveva, e il ciclo dei 7 giorni
    /// non se ne accorgeva: qui si scrive nell'unico punto in cui i badge si leggono.
    /// </summary>
    private const string BadgeKey = "hr_last_badge_read";

    private static readonly TimeSpan MargineCursore = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MargineCursoreOrologioNostro = TimeSpan.FromHours(1);
    private const int MaxDaysToRepair = 5000;

    private readonly DbService _db;
    private readonly EcosClient _ecos;
    private readonly NotificationService _notif;
    private readonly ILogger<HrAttendanceService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HrAttendanceService(
        DbService db, EcosClient ecos, NotificationService notif, ILogger<HrAttendanceService> logger)
    {
        _db = db;
        _ecos = ecos;
        _notif = notif;
        _logger = logger;
    }

    public HrAttendanceService(
        DbService db, EcosClient ecos, ILogger<HrAttendanceService> logger)
        : this(db, ecos, new NotificationService(db, new AnagraficheCache(Microsoft.Extensions.Logging.Abstractions.NullLogger<AnagraficheCache>.Instance)), logger)
    {
    }

    // Stato dell'ultimo import, per la pagina (il servizio è singleton).
    public bool ImportInProgress { get; private set; }
    public DateTime? LastImport { get; private set; }
    public string LastResult { get; private set; } = "";

    // ── AVANZAMENTO A VIDEO (PIANO-HR-PORT-ORIGINALE.md, B3) ──────────────────
    //
    // Port della barra + txtLog di SyncEcosPage. Il servizio è singleton, quindi lo stato
    // sta qui in memoria e la pagina lo legge da GET /api/hr/status mentre l'import gira.
    // 🪤 In memoria vuol dire che un riavvio del servizio a metà import lo azzera: la
    // pagina lo riconosce (StartedAt nullo) e lo dice, invece di restare a girare.

    private readonly object _progressoLock = new();
    private HrImportProgressDto _progresso = new();

    /// <summary>Copia dello stato dell'import: si legge da un altro thread mentre l'import scrive.</summary>
    public HrImportProgressDto SnapshotProgresso()
    {
        lock (_progressoLock)
        {
            return new HrImportProgressDto
            {
                Running = _progresso.Running,
                Title = _progresso.Title,
                Phase = _progresso.Phase,
                Percent = _progresso.Percent,
                Downloaded = _progresso.Downloaded,
                Added = _progresso.Added,
                Updated = _progresso.Updated,
                Removed = _progresso.Removed,
                DaysRecalculated = _progresso.DaysRecalculated,
                StartedAt = _progresso.StartedAt,
                EndedAt = _progresso.EndedAt,
                Log = new List<string>(_progresso.Log),
            };
        }
    }

    private void ProgressoInizio(string titolo)
    {
        lock (_progressoLock)
        {
            _progresso = new HrImportProgressDto
            {
                Running = true,
                Title = titolo,
                StartedAt = DateTime.Now,
            };
            _progresso.Log.Add($"=== {titolo.ToUpperInvariant()} ===");
        }
    }

    private void ProgressoFase(string fase, int percento)
    {
        lock (_progressoLock)
        {
            _progresso.Phase = fase;
            _progresso.Percent = percento;
            AggiungiRiga(fase);
        }
    }

    private void ProgressoLog(string riga)
    {
        lock (_progressoLock) AggiungiRiga(riga);
    }

    private void ProgressoFine(string riga, HrImportResultDto? esito)
    {
        lock (_progressoLock)
        {
            _progresso.Running = false;
            _progresso.Percent = 100;
            _progresso.Phase = riga;
            _progresso.EndedAt = DateTime.Now;
            if (esito != null)
            {
                _progresso.Added = esito.PunchesAdded;
                _progresso.Updated = esito.PunchesUpdated;
                _progresso.DaysRecalculated = esito.DaysRecalculated;
            }
            AggiungiRiga(riga);
        }
    }

    /// <summary>Da chiamare già dentro il lock: tiene le ultime 200 righe, come il txtLog.</summary>
    private void AggiungiRiga(string riga)
    {
        _progresso.Log.Add(riga);
        if (_progresso.Log.Count > 200) _progresso.Log.RemoveAt(0);
    }

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

    // ── CARTELLINO MENSILE ────────────────────────────────────────────────────

    public HrMonthlyTimesheetDto GetMonthlyTimesheet(int employeeId, int year, int month)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var dipendente = c.QueryFirstOrDefault<(string Name, string? EcosCode, bool MustPunch, decimal DailyHours)>(
            @"SELECT CONCAT_WS(' ', first_name, last_name) AS Name, ecos_empl_code AS EcosCode,
                     hr_must_punch AS MustPunch, hr_daily_hours AS DailyHours
              FROM employees WHERE id = @Id", new { Id = employeeId });

        // 🪤 Ogni colonna vuole il suo alias: Dapper NON abbina `work_date` a `WorkDate`
        // (`MatchNamesWithUnderscores` qui non è attivo). Senza alias la data resta a
        // DateTime.MinValue su OGNI riga, e il `ToDictionary` qui sotto muore al secondo
        // giorno con «An item with the same key has already been added: 01/01/0001» —
        // cioè il cartellino risponde 500 appena una persona ha due giornate.
        var giornate = c.Query<DayRow>(
                @"SELECT work_date AS WorkDate,
                         clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1,
                         clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                         regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                         break_minutes AS BreakMinutes, bands_json AS BandsJson,
                         note AS Note, has_anomaly AS HasAnomaly
                  FROM hr_days
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(g => g.WorkDate.Date);

        var timbrature = c.Query<PunchRow>(
                @"SELECT t.id AS Id, t.work_date AS WorkDate, t.punched_at AS PunchedAt,
                         t.direction AS Direction, t.source AS Source, t.reason AS Reason,
                         CONCAT_WS(' ', e.first_name, e.last_name) AS CreatedBy
                  FROM hr_punches t
                  LEFT JOIN employees e ON e.id = t.created_by
                  WHERE t.employee_id = @Id AND t.work_date BETWEEN @Da AND @A
                  ORDER BY t.punched_at",
                // 🪤 Un giorno in più da ogni parte: serve a riconoscere il turno di notte
                // a cavallo del 1° e dell'ultimo del mese. Il ciclo resta sulle sole
                // giornate del mese, quindi i due giorni in più non finiscono a video.
                new { Id = employeeId, Da = primo.AddDays(-1), A = ultimo.AddDays(1) })
            .GroupBy(t => t.WorkDate.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Assenze approvate del mese
        var assenze = c.Query<HrAbsenceDto>(
                @"SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                         a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType, a.status AS Status
                  FROM hr_absences a
                  WHERE a.employee_id = @Id AND a.status = 'APPROVED'
                    AND a.date_from <= @A AND a.date_to >= @Da",
                new { Id = employeeId, Da = primo, A = ultimo }).ToList();

        var assenzeGiorno = new Dictionary<DateTime, HrAbsenceDto>();
        foreach (var a in assenze)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                assenzeGiorno[dt.Date] = a;
        }

        // Solleciti già chiesti nel mese: il tooltip del pulsante 📧 dice QUANDO (come
        // GetLastMailSent nell'originale), non solo che è già stato mandato.
        var solleciti = c.Query<(DateTime WorkDate, DateTime SentAt)>(
                @"SELECT work_date AS WorkDate, sent_at AS SentAt
                  FROM hr_reminders
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(x => x.WorkDate.Date, x => x.SentAt);

        DateTime oggi = DateTime.Today;

        var dto = new HrMonthlyTimesheetDto
        {
            EmployeeId = employeeId,
            EmployeeName = dipendente.Name ?? "",
            Year = year,
            Month = month,
            EcosLinked = !string.IsNullOrWhiteSpace(dipendente.EcosCode),
        };

        for (DateTime work_date = primo; work_date <= ultimo; work_date = work_date.AddDays(1))
        {
            bool isHoliday = TimesheetRules.IsHoliday(work_date);
            var riga = new HrDayDto
            {
                WorkDate = work_date,
                IsHoliday = isHoliday,
            };

            if (giornate.TryGetValue(work_date, out DayRow? g))
            {
                bool nonCalcolabile = g.Note.StartsWith("⚠ ERR");
                riga.HasData = true;
                riga.ClockIn1 = g.ClockIn1 ?? "";
                riga.ClockOut1 = g.ClockOut1 ?? "";
                riga.ClockIn2 = g.ClockIn2 ?? "";
                riga.ClockOut2 = g.ClockOut2 ?? "";
                riga.RegularHours = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.RegularMinutes);
                riga.Overtime = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.OvertimeMinutes);
                riga.BreakTime = TimesheetRules.FormatDuration(g.BreakMinutes);
                riga.Bands = LeggiFasce(g.BandsJson);
                riga.Note = g.Note;
                riga.HasAnomaly = g.HasAnomaly;
            }
            else if (assenzeGiorno.TryGetValue(work_date, out HrAbsenceDto? abs))
            {
                riga.HasData = true;
                riga.RegularHours = "0h 0m";
                riga.Overtime = "0h 0m";
                riga.BreakTime = "0h 0m";
                riga.Note = abs.IsFullDay ? abs.AbsenceType : $"{abs.AbsenceType} ({abs.Hours}h)";
            }
            else if (!dipendente.MustPunch && !isHoliday && work_date < DateTime.Today)
            {
                // Forfait su giorno passato
                riga.HasData = true;
                int minutiForfait = (int)(dipendente.DailyHours * 60m);
                riga.RegularHours = TimesheetRules.FormatDuration(minutiForfait);
                riga.Overtime = "0h 0m";
                riga.BreakTime = "0h 0m";
                riga.Note = "FORFAIT";
            }

            if (timbrature.TryGetValue(work_date, out List<PunchRow>? grezze))
            {
                riga.Punches = grezze.Select(t => new HrPunchDto
                {
                    Id = t.Id,
                    PunchedAt = t.PunchedAt,
                    Direction = t.Direction,
                    Source = t.Source,
                    Reason = t.Reason,
                    CreatedBy = t.CreatedBy,
                }).ToList();

                // Grezzo e normalizzato non stanno su hr_days — là c'è il risultato — ma
                // si ottengono ripassando le timbrature nel motore, che è puro: nessuna
                // scrittura, e per un mese sono trentun giornate.
                // La configurazione della persona qui non serve: incide sullo straordinario,
                // e i due stadi sono solo orari e somme.
                TimesheetDay stadi = TimesheetEngine.Calcola(
                    work_date,
                    grezze.Select(t => new RawPunch(t.PunchedAt, t.Direction, null)),
                    DateTime.Today,
                    null,
                    ContestoNotte(timbrature, work_date));

                riga.Raw = new HrDayStageDto
                {
                    ClockIn1 = stadi.RawEntrata1,
                    ClockOut1 = stadi.RawUscita1,
                    ClockIn2 = stadi.RawEntrata2,
                    ClockOut2 = stadi.RawUscita2,
                    BreakTime = stadi.RawBreak,
                    TotalHours = stadi.RawTotal,
                };
                riga.Normalized = new HrDayStageDto
                {
                    ClockIn1 = stadi.NormEntrata1,
                    ClockOut1 = stadi.NormUscita1,
                    ClockIn2 = stadi.NormEntrata2,
                    ClockOut2 = stadi.NormUscita2,
                    BreakTime = stadi.NormBreak,
                    TotalHours = stadi.NormTotal,
                };
            }

            // La regola sta in un posto solo (HrDayReminder): la usano il pulsante 📧 sulla
            // riga e il filtro «📧 Da segnalare», che così non possono divergere.
            riga.CanRemind = HrDayReminder.Serve(riga.Note, work_date, oggi);
            if (solleciti.TryGetValue(work_date.Date, out DateTime quando))
                riga.LastReminderAt = quando;

            dto.Days.Add(riga);
        }

        return dto;
    }

    // ── SOLLECITO DELLA SINGOLA GIORNATA (voce 1 del port) ────────────────────

    /// <summary>
    /// Il sollecito pronto per una giornata: destinatario, oggetto e corpo integrale, più lo
    /// stato («già chiesto il …»). Il testo lo compone il server, come per quello mensile: la
    /// pagina lo mostra e basta.
    /// </summary>
    /// <param name="firma">
    /// Nome del mittente da mettere in fondo (vuoto = solo la riga dell'ufficio).
    /// </param>
    public HrDayReminderDto GetDayReminder(int employeeId, DateTime date, string firma)
    {
        DateTime giorno = date.Date;
        HrMonthlyTimesheetDto mese = GetMonthlyTimesheet(employeeId, giorno.Year, giorno.Month);
        HrDayDto? giornata = mese.Days.FirstOrDefault(g => g.WorkDate.Date == giorno);

        var dto = new HrDayReminderDto
        {
            EmployeeId = employeeId,
            EmployeeName = mese.EmployeeName,
            Date = giorno,
        };

        if (giornata == null)
        {
            dto.Blocco = "Giornata fuori dal mese richiesto.";
            return dto;
        }

        using MySqlConnection c = _db.Open();

        // Il saluto usa il nome di battesimo dalla colonna, come fa il sollecito mensile:
        // ricavarlo tagliando il nome completo al primo spazio sbaglierebbe su «Maria Grazia».
        var recapito = c.QueryFirstOrDefault<(string? Email, string FirstName)>(
            "SELECT email AS Email, COALESCE(first_name, '') AS FirstName FROM employees WHERE id = @Id",
            new { Id = employeeId });
        dto.Email = recapito.Email;
        string saluto = string.IsNullOrWhiteSpace(recapito.FirstName)
            ? mese.EmployeeName
            : recapito.FirstName;

        dto.CanRemind = giornata.CanRemind;
        dto.LastReminderAt = giornata.LastReminderAt;
        dto.Subject = HrDayReminder.Oggetto(giorno);
        dto.Body = HrDayReminder.Corpo(saluto, giorno, giornata, firma);

        if (!giornata.CanRemind)
        {
            dto.Blocco = giorno == DateTime.Today
                ? "La giornata di oggi non si sollecita: è ancora aperta."
                : "Questa giornata non ha anomalie da segnalare.";
        }
        else if (string.IsNullOrWhiteSpace(dto.Email))
        {
            // Stesso messaggio dell'originale («Nessuna email configurata per …»).
            dto.Blocco = $"Nessuna email configurata per {mese.EmployeeName}.";
        }

        return dto;
    }

    /// <summary>
    /// Segna la giornata come sollecitata, conservando anche il testo (M117): è la riga che
    /// la Cronologia Email rilegge. Un secondo sollecito sulla stessa giornata aggiorna.
    /// </summary>
    public void MarkDayReminder(
        int employeeId, DateTime date, string? email, string subject, string body,
        int sentBy, string channel)
    {
        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel, email, subject, body)
            VALUES (@EmployeeId, @WorkDate, @SentBy, @Channel, @Email, @Subject, @Body)
            ON DUPLICATE KEY UPDATE sent_at = NOW(), sent_by = VALUES(sent_by), channel = VALUES(channel),
                                    email = VALUES(email), subject = VALUES(subject), body = VALUES(body)",
            new
            {
                EmployeeId = employeeId,
                WorkDate = date.Date,
                SentBy = sentBy,
                Channel = channel,
                Email = email,
                Subject = subject,
                Body = body,
            });
    }

    // ── CRONOLOGIA EMAIL (voce 6 del port) ───────────────────────────────────

    /// <summary>
    /// Le mail di sollecito di un mese. 🪤 Il mese è quello del <b>giorno di riferimento</b>
    /// (<c>work_date</c>), non della spedizione: come nell'originale, una mail mandata a
    /// settembre per un buco di agosto si cerca sotto agosto.
    /// </summary>
    public HrReminderLogDto GetReminderLog(int year, int month, int? employeeId)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        string filtro = employeeId.HasValue ? " AND r.employee_id = @EmployeeId" : "";

        using MySqlConnection c = _db.Open();
        List<HrReminderLogRowDto> righe = c.Query<HrReminderLogRowDto>(
            @"SELECT r.id AS Id, r.sent_at AS SentAt, r.employee_id AS EmployeeId,
                     CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                     r.email AS Email, r.work_date AS WorkDate, r.subject AS Subject,
                     r.body AS Body, r.channel AS Channel,
                     CONCAT_WS(' ', a.first_name, a.last_name) AS SentByName
              FROM hr_reminders r
              JOIN employees e ON e.id = r.employee_id
              LEFT JOIN employees a ON a.id = r.sent_by
              WHERE r.work_date BETWEEN @Primo AND @Ultimo" + filtro + @"
              ORDER BY r.sent_at DESC, r.work_date DESC",
            new { Primo = primo, Ultimo = ultimo, EmployeeId = employeeId }).ToList();

        return new HrReminderLogDto { Year = year, Month = month, Rows = righe };
    }

    // ── GESTIONE RICHIESTE ED ASSENZE (FASE 2) ─────────────────────────────────

    public List<HrAbsenceDto> GetAbsences(
        int? employeeId, int? departmentId, int? year, int? month, string? status, int currentUserId, bool isManagerOrAdmin)
    {
        using MySqlConnection c = _db.Open();

        int? targetEmployeeId = employeeId;
        if (!isManagerOrAdmin)
            targetEmployeeId = currentUserId;

        var sql = @"
            SELECT a.id AS Id, a.employee_id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   d.name AS DepartmentName,
                   a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay,
                   a.absence_type AS AbsenceType, a.status AS Status,
                   a.source AS Source, a.ecos_absence_id AS EcosAbsenceId,
                   a.approved_by AS ApprovedBy,
                   CONCAT_WS(' ', app.first_name, app.last_name) AS ApprovedByName,
                   a.approved_at AS ApprovedAt,
                   a.rejection_reason AS RejectionReason,
                   a.notes AS Notes,
                   a.created_by AS CreatedBy,
                   CONCAT_WS(' ', cr.first_name, cr.last_name) AS CreatedByName,
                   a.created_at AS CreatedAt
            FROM hr_absences a
            JOIN employees e ON e.id = a.employee_id
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            LEFT JOIN employees app ON app.id = a.approved_by
            LEFT JOIN employees cr ON cr.id = a.created_by
            WHERE 1=1";

        var p = new DynamicParameters();

        if (targetEmployeeId.HasValue)
        {
            sql += " AND a.employee_id = @EmpId";
            p.Add("EmpId", targetEmployeeId.Value);
        }
        else if (departmentId.HasValue)
        {
            sql += " AND ed.department_id = @DeptId";
            p.Add("DeptId", departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND a.status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }

        if (year.HasValue && month.HasValue)
        {
            var primo = new DateTime(year.Value, month.Value, 1);
            var ultimo = primo.AddMonths(1).AddDays(-1);
            sql += " AND a.date_from <= @Ultimo AND a.date_to >= @Primo";
            p.Add("Primo", primo);
            p.Add("Ultimo", ultimo);
        }
        else if (year.HasValue)
        {
            var primo = new DateTime(year.Value, 1, 1);
            var ultimo = new DateTime(year.Value, 12, 31);
            sql += " AND a.date_from <= @Ultimo AND a.date_to >= @Primo";
            p.Add("Primo", primo);
            p.Add("Ultimo", ultimo);
        }

        sql += " ORDER BY a.date_from DESC, a.created_at DESC";

        return c.Query<HrAbsenceDto>(sql, p).ToList();
    }

    public (int? Id, string? Error) CreateAbsenceRequest(
        HrCreateAbsenceRequest req, int currentUserId, bool isManagerOrAdmin)
    {
        int targetEmployeeId = req.EmployeeId ?? currentUserId;
        if (targetEmployeeId != currentUserId && !isManagerOrAdmin)
            return (null, "Puoi inserire richieste solo per te stesso.");

        if (req.DateFrom.Date > req.DateTo.Date)
            return (null, "La data di inizio non può essere successiva alla data di fine.");

        if (!req.IsFullDay && (!req.Hours.HasValue || req.Hours.Value <= 0 || req.Hours.Value > 24))
            return (null, "Le ore di permesso devono essere maggiori di 0.");

        string type = (req.AbsenceType ?? "VACATION").Trim().ToUpperInvariant();
        if (type is not ("VACATION" or "PERMIT" or "SICKNESS" or "INJURY" or "OTHER"))
            return (null, "Tipologia assenza non valida.");

        using MySqlConnection c = _db.Open();

        var targetEmp = c.QueryFirstOrDefault<(string Name, int? PrimaryDeptId)>(
            @"SELECT CONCAT_WS(' ', e.first_name, e.last_name) AS Name, ed.department_id AS PrimaryDeptId
              FROM employees e
              LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
              WHERE e.id = @Id AND e.status <> 'TERMINATED'",
            new { Id = targetEmployeeId });

        if (targetEmp == default)
            return (null, "Dipendente non trovato o cessato.");

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO hr_absences
                (employee_id, date_from, date_to, hours, is_full_day, absence_type, status, source, notes, created_by)
            VALUES
                (@EmployeeId, @DateFrom, @DateTo, @Hours, @IsFullDay, @AbsenceType, 'PENDING', 'ATEC', @Notes, @CreatedBy);
            SELECT LAST_INSERT_ID();",
            new
            {
                EmployeeId = targetEmployeeId,
                DateFrom = req.DateFrom.Date,
                DateTo = req.DateTo.Date,
                req.Hours,
                req.IsFullDay,
                AbsenceType = type,
                Notes = req.Notes?.Trim(),
                CreatedBy = currentUserId
            });

        // Notifica ai responsabili di reparto
        try
        {
            var managerIds = c.Query<int>(@"
                SELECT DISTINCT ed.employee_id
                FROM employee_departments ed
                WHERE ed.is_responsible = 1
                  AND ed.department_id IN (
                      SELECT department_id FROM employee_departments WHERE employee_id = @EmpId
                  )
                  AND ed.employee_id <> @CreatedBy",
                new { EmpId = targetEmployeeId, CreatedBy = currentUserId }).ToList();

            if (managerIds.Count > 0)
            {
                string descr = req.IsFullDay
                    ? (req.DateFrom.Date == req.DateTo.Date ? $"il {req.DateFrom:dd/MM/yyyy}" : $"dal {req.DateFrom:dd/MM} al {req.DateTo:dd/MM/yyyy}")
                    : $"il {req.DateFrom:dd/MM/yyyy} ({req.Hours}h)";

                _notif.Create(
                    type: "HR_ABSENCE_REQUEST",
                    severity: "INFO",
                    title: $"Richiesta {type} — {targetEmp.Name}",
                    message: $"{targetEmp.Name} ha richiesto {type.ToLower()} {descr}.",
                    refType: "HR_ABSENCE",
                    refId: id,
                    projectId: null,
                    createdBy: currentUserId,
                    recipientIds: managerIds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HR] Errore invio notifica richiesta ferie a responsabili.");
        }

        return (id, null);
    }

    public string? ApproveAbsenceRequest(
        int absenceId, bool approved, string? rejectionReason, int approverId, bool isManagerOrAdmin)
    {
        using MySqlConnection c = _db.Open();

        var absence = c.QueryFirstOrDefault<(int Id, int EmployeeId, string EmployeeName, string Status, string AbsenceType, DateTime DateFrom, DateTime DateTo, decimal? Hours, bool IsFullDay)>(
            @"SELECT a.id AS Id, a.employee_id AS EmployeeId,
                     CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                     a.status AS Status, a.absence_type AS AbsenceType,
                     a.date_from AS DateFrom, a.date_to AS DateTo, a.hours AS Hours, a.is_full_day AS IsFullDay
              FROM hr_absences a
              JOIN employees e ON e.id = a.employee_id
              WHERE a.id = @Id",
            new { Id = absenceId });

        if (absence == default)
            return "Richiesta non trovata.";

        if (absence.Status != "PENDING")
            return $"La richiesta è già in stato {absence.Status}.";

        if (!isManagerOrAdmin)
        {
            bool isResponsible = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM employee_departments ed_resp
                JOIN employee_departments ed_emp ON ed_emp.department_id = ed_resp.department_id
                WHERE ed_resp.employee_id = @ApproverId AND ed_resp.is_responsible = 1
                  AND ed_emp.employee_id = @TargetEmpId",
                new { ApproverId = approverId, TargetEmpId = absence.EmployeeId }) > 0;

            if (!isResponsible)
                return "Non hai i permessi per approvare richieste per questo dipendente (non sei responsabile del suo reparto).";
        }

        string newStatus = approved ? "APPROVED" : "REJECTED";

        c.Execute(@"
            UPDATE hr_absences
            SET status = @Status, approved_by = @ApproverId, approved_at = NOW(),
                rejection_reason = @RejectionReason
            WHERE id = @Id",
            new
            {
                Status = newStatus,
                ApproverId = approverId,
                RejectionReason = approved ? null : rejectionReason?.Trim(),
                Id = absenceId
            });

        // Sincronizza su res_assignments (Planner Risorse Gantt Ferie)
        SyncToResourcePlanner(c, absence.EmployeeId, absence.DateFrom, absence.DateTo, absence.AbsenceType, isApproved: approved);

        // Notifica al dipendente
        try
        {
            string approverName = c.ExecuteScalar<string>(
                "SELECT CONCAT_WS(' ', first_name, last_name) FROM employees WHERE id = @Id",
                new { Id = approverId }) ?? "Responsabile";

            string esitoStr = approved ? "APPROVATA" : "RIFIUTATA";
            string msg = approved
                ? $"La tua richiesta di {absence.AbsenceType.ToLower()} è stata approvata da {approverName}."
                : $"La tua richiesta di {absence.AbsenceType.ToLower()} è stata rifiutata da {approverName}."
                  + (!string.IsNullOrWhiteSpace(rejectionReason) ? $" Motivo: {rejectionReason}" : "");

            _notif.Create(
                type: "HR_ABSENCE_STATUS",
                severity: approved ? "SUCCESS" : "WARNING",
                title: $"Richiesta {absence.AbsenceType} {esitoStr}",
                message: msg,
                refType: "HR_ABSENCE",
                refId: absenceId,
                projectId: null,
                createdBy: approverId,
                recipientIds: new[] { absence.EmployeeId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HR] Errore invio notifica esito richiesta a dipendente.");
        }

        return null;
    }

    public string? CancelAbsenceRequest(int absenceId, int currentUserId, bool isAdmin)
    {
        using MySqlConnection c = _db.Open();

        var absence = c.QueryFirstOrDefault<(int Id, int EmployeeId, int? CreatedBy, string Status, string AbsenceType, DateTime DateFrom, DateTime DateTo)>(
            "SELECT id, employee_id AS EmployeeId, created_by AS CreatedBy, status AS Status, absence_type AS AbsenceType, date_from AS DateFrom, date_to AS DateTo FROM hr_absences WHERE id = @Id",
            new { Id = absenceId });

        if (absence == default)
            return "Richiesta non trovata.";

        if (absence.Status == "CANCELLED")
            return "La richiesta è già annullata.";

        if (!isAdmin && absence.EmployeeId != currentUserId && absence.CreatedBy != currentUserId)
            return "Puoi annullare solo le tue richieste.";

        if (absence.Status == "APPROVED" && !isAdmin)
            return "Le richieste già approvate possono essere annullate solo da un amministratore.";

        c.Execute("UPDATE hr_absences SET status = 'CANCELLED' WHERE id = @Id", new { Id = absenceId });

        // Rimuovi dal Planner Risorse se era approvata
        SyncToResourcePlanner(c, absence.EmployeeId, absence.DateFrom, absence.DateTo, absence.AbsenceType, isApproved: false);

        return null;
    }

    // ── CALENDARIO MENSILE (PORT DELLA VISTA «Calendario Mensile») ────────────
    //
    // Port di CalendarPage.xaml.vb (CaricaDatiMensili) del progetto Timbrature: una riga
    // per VOCE — ore ordinarie, le nove fasce della Circolare 12/2024, presenza, ferie,
    // permessi, malattia, infortunio — e non una riga per dipendente. Le regole di colore
    // sono quelle dell'originale, tarate sul campo: non si ritoccano a intuito. Testo,
    // colore e tooltip li decide QUI il server, così la pagina web e il file Excel
    // disegnano la stessa griglia invece di due interpretazioni della stessa cosa.

    /// <summary>Le nove voci di straordinario, nell'ordine e con le etichette del VB.</summary>
    private static readonly (string VoceType, string Band, string Label)[] VociStraordinario =
    {
        ("STRAORD_A", "A", "STRAORD. 20%"),
        ("STRAORD_C", "C", "STRAORD. FEST. 55%"),
        ("STRAORD_D", "D", "STRAORD. FEST. RIP. 10%"),
        ("STRAORD_E", "E", "STRAORD. FEST. >8h 55%"),
        ("STRAORD_F", "F", "STRAORD. FEST. RIP. >8h 35%"),
        ("STRAORD_G", "G", "STRAORD. NOTT. 50/60%"),
        ("STRAORD_H", "H", "NOTT. FEST. 35%"),
        ("STRAORD_L", "L", "STRAORD. NOTT. FEST. 75%"),
        ("STRAORD_M", "M", "STRAORD. NOTT. FEST. RIP. 55%"),
    };

    /// <summary>Etichette del dettaglio straordinario nel tooltip (BuildStraordDetail).</summary>
    private static readonly Dictionary<string, string> EtichetteFasceTooltip = new()
    {
        ["A"] = "20%", ["C"] = "Fest.55%", ["D"] = "Fest.Rip.10%", ["E"] = "Fest.>8h 55%",
        ["F"] = "Fest.Rip.>8h 35%", ["G"] = "Nott.50/60%", ["H"] = "Nott.Fest.35%",
        ["L"] = "Nott.Fest.75%", ["M"] = "Nott.Fest.Rip.55%",
    };

    public HrMonthlyCalendarDto GetMonthlyCalendar(int year, int month, int? departmentId)
    {
        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        int daysInMonth = ultimo.Day;
        DateTime oggi = DateTime.Today;

        using MySqlConnection c = _db.Open();

        var p = new DynamicParameters();
        p.Add("Primo", primo);
        p.Add("Ultimo", ultimo);

        string empSql = @"
            SELECT DISTINCT e.id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   d.name AS DepartmentName,
                   e.ecos_empl_code AS EmplCode,
                   e.hr_must_punch AS MustPunch,
                   e.hr_daily_hours AS DailyHours,
                   -- 🪤 `SELECT DISTINCT` + `ORDER BY` su colonne che non sono nella SELECT:
                   -- MySQL lo rifiuta in blocco (ONLY_FULL_GROUP_BY). Cognome e nome stanno
                   -- qui solo per poter ordinare come si è sempre ordinato.
                   e.last_name AS LastName, e.first_name AS FirstName
            FROM employees e
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE e.status = 'ACTIVE' AND e.emp_type = 'INTERNAL' AND e.user_role <> 'ADMIN' AND e.first_name NOT LIKE '[%'";

        if (departmentId.HasValue)
        {
            empSql += " AND ed.department_id = @DeptId";
            p.Add("DeptId", departmentId.Value);
        }

        empSql += " ORDER BY d.name, e.last_name, e.first_name";

        var employees = c.Query<CalendarEmployee>(empSql, p).ToList();

        var days = c.Query<DayRow>(@"
            SELECT employee_id AS EmployeeId, work_date AS WorkDate,
                   clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1, clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                   regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                   break_minutes AS BreakMinutes, bands_json AS BandsJson, note AS Note, has_anomaly AS HasAnomaly
            FROM hr_days
            WHERE work_date BETWEEN @Primo AND @Ultimo", p)
            .GroupBy(d => (d.EmployeeId, d.WorkDate.Date))
            .ToDictionary(g => g.Key, g => g.First());

        // `source` serve al colore: un'assenza che arriva da Ecos è già approvata là (TEAL),
        // una nostra è ancora roba interna (BLUE/ORANGE/PURPLE/YELLOW per causale).
        var absences = c.Query<CalendarAbsence>(@"
            SELECT a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType,
                   a.status AS Status, a.source AS Source
            FROM hr_absences a
            WHERE a.status IN ('APPROVED', 'PENDING')
              AND a.date_from <= @Ultimo AND a.date_to >= @Primo", p).ToList();

        var absencesMap = new Dictionary<(int EmployeeId, DateTime WorkDate), CalendarAbsence>();
        foreach (var a in absences)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                absencesMap[(a.EmployeeId, dt.Date)] = a;
        }

        var result = new HrMonthlyCalendarDto
        {
            Year = year,
            Month = month,
            DaysInMonth = daysInMonth,
            Employees = employees
                .Select(e => new HrCalendarEmployeeDto { Id = e.EmployeeId, Name = e.EmployeeName })
                .ToList(),
        };

        for (int giorno = 1; giorno <= daysInMonth; giorno++)
        {
            var dt = new DateTime(year, month, giorno);
            result.DayLabels[giorno] = NomeGiorno(dt);
            result.NonWorkingDays[giorno] = dt.DayOfWeek == DayOfWeek.Saturday || TimesheetRules.IsHoliday(dt);
        }

        foreach (CalendarEmployee emp in employees)
        {
            // Il nome (con la matricola) sta SOLO sulla prima riga: sotto è la stessa persona.
            string etichetta = string.IsNullOrEmpty(emp.EmplCode)
                ? emp.EmployeeName
                : $"{emp.EmployeeName}\nMatr. {emp.EmplCode}";

            HrCalendarRowDto NuovaRiga(string voce, string voceType, string nome = "") => new()
            {
                EmployeeId = emp.EmployeeId,
                Employee = nome,
                EmployeeKey = emp.EmployeeName,
                DepartmentName = emp.DepartmentName,
                Voce = voce,
                VoceType = voceType,
            };

            HrCalendarRowDto rowOrd = NuovaRiga("ORE ORDINARIE", "ORE_ORDINARIE", etichetta);
            var straordRows = VociStraordinario.ToDictionary(
                v => v.Band, v => NuovaRiga(v.Label, v.VoceType));
            HrCalendarRowDto rowPres = NuovaRiga("PRESENZA", "PRESENZA");
            HrCalendarRowDto rowFerie = NuovaRiga("FERIE", "FERIE");
            HrCalendarRowDto rowPerm = NuovaRiga("PERMESSI", "PERMESSI");
            HrCalendarRowDto rowMal = NuovaRiga("MALATTIA", "MALATTIA");
            HrCalendarRowDto rowInf = NuovaRiga("INFORTUNIO", "INFORTUNIO");

            HrCalendarRowDto[] righeFisse = { rowOrd, rowPres, rowFerie, rowPerm, rowMal, rowInf };

            for (int giorno = 1; giorno <= daysInMonth; giorno++)
            {
                var data = new DateTime(year, month, giorno);
                bool isSabato = data.DayOfWeek == DayOfWeek.Saturday;
                bool isFestivo = isSabato || TimesheetRules.IsHoliday(data);

                days.TryGetValue((emp.EmployeeId, data), out DayRow? dayData);
                absencesMap.TryGetValue((emp.EmployeeId, data), out CalendarAbsence? dayAbsence);

                // Chi non timbra non ha righe in hr_days: il VB gli generava i «record
                // forfait» prima di disegnare (GenerateForfaitRecords), altrimenti ogni sua
                // giornata risulterebbe mancante. Qui la giornata piena si finge alla stessa
                // maniera, senza scriverla da nessuna parte.
                bool forfait = dayData == null && dayAbsence == null && !emp.MustPunch
                               && !isFestivo && data <= oggi;

                if (isFestivo && !isSabato)
                {
                    // ── Domeniche e festivi: grigio su tutto, e lo straordinario se ha lavorato
                    Colora(righeFisse, straordRows, giorno, "GRAY");
                    if (dayData != null)
                    {
                        PopolaStraordinario(straordRows, dayData, giorno);
                        string tip = Tooltip(emp.EmployeeName, data, dayData, "Festivo");
                        foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;
                        Scrivi(rowPres, giorno, "P", "GREEN", tip);
                    }
                }
                else if (isSabato)
                {
                    // ── Sabato: grigio, ma è tutto straordinario se ha lavorato
                    Colora(righeFisse, straordRows, giorno, "GRAY");
                    if (dayData != null)
                    {
                        PopolaStraordinario(straordRows, dayData, giorno);
                        string tip = Tooltip(emp.EmployeeName, data, dayData, "Sabato");
                        foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;
                        Scrivi(rowPres, giorno, "P", "GREEN", tip);
                    }
                }
                else if (dayData != null || forfait)
                {
                    // ── Giorno feriale lavorato
                    int minutiOrd = forfait ? (int)(emp.DailyHours * 60m) : dayData!.RegularMinutes;
                    int minutiStraord = forfait ? 0 : dayData!.OvertimeMinutes;

                    Scrivi(rowOrd, giorno, OreTesto(minutiOrd), "GREEN");
                    if (dayData != null) PopolaStraordinario(straordRows, dayData, giorno);

                    string tip = forfait
                        ? $"{emp.EmployeeName} — {data:dd/MM/yyyy}\nForfait: {emp.DailyHours:0.#}h"
                        : Tooltip(emp.EmployeeName, data, dayData!, null);
                    Cella(rowOrd, giorno).Tooltip = tip;
                    foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;

                    Scrivi(rowPres, giorno, "P", "GREEN", tip);

                    // Ore mancanti: se la giornata non è piena, o la copre un permesso o è rossa.
                    decimal oreLavorate = (decimal)(minutiOrd + minutiStraord) / 60m;
                    decimal oreMancanti = emp.DailyHours - oreLavorate;
                    bool anomalia = dayData?.HasAnomaly == true;

                    // 🪤 Mezzo turno di notte non è mezza giornata mancante: le ore stanno
                    // tutte lì, spartite fra i due giorni che la mezzanotte separa. Senza
                    // questo un turno di notte regolare tinge di rosso DUE caselle.
                    // Vale SOLO per il rosso: se quel giorno c'è anche un permesso, il ramo
                    // qui sotto deve continuare a scriverlo sulla sua riga. E vale solo per
                    // la giornata che ha CEDUTO le ore: quella che se le è prese ce le ha
                    // tutte, e se le mancano il rosso ci va come sempre.
                    bool mezzaNotte = NightShift.HasHandedHours(dayData?.Note);

                    if (oreMancanti >= 0.25m && dayAbsence != null)
                    {
                        decimal ore = dayAbsence.Hours ?? emp.DailyHours;
                        switch (dayAbsence.AbsenceType)
                        {
                            case "PERMIT":
                                Scrivi(rowPerm, giorno, Ore(ore), "ORANGE");
                                break;
                            case "INJURY":
                                Scrivi(rowInf, giorno, Ore(ore), "YELLOW");
                                break;
                        }

                        // Permesso parziale ma nessuna timbratura vera: va sollecitato.
                        if (oreLavorate < 0.25m && ore < emp.DailyHours)
                        {
                            Scrivi(rowPres, giorno, "?", "RED", TooltipPermessoScoperto(emp.EmployeeName, data, ore));
                            Scrivi(rowOrd, giorno, "", "RED");
                        }
                    }
                    else if ((oreMancanti >= 0.25m && !mezzaNotte) || anomalia)
                    {
                        Cella(rowPres, giorno).Color = "RED";

                        // Il VB non aveva un flag di anomalia: il rosso lo deduceva dalle ore
                        // mancanti. Il nostro motore invece la marca (timbratura dispari,
                        // uscita che manca): una giornata così va guardata, e col «?» si vede.
                        if (anomalia)
                        {
                            Scrivi(rowPres, giorno, "?", "RED", tip);
                            Cella(rowOrd, giorno).Color = "RED";
                        }
                    }
                }
                else if (dayAbsence != null)
                {
                    // ── Giorno di assenza piena
                    decimal ore = dayAbsence.Hours ?? emp.DailyHours;
                    bool daEcos = string.Equals(dayAbsence.Source, "ECOS", StringComparison.OrdinalIgnoreCase);
                    string tipAss = $"{emp.EmployeeName} — {data:dd/MM/yyyy}\n{Causale(dayAbsence.AbsenceType)}"
                                    + $" {Ore(ore)}h ({dayAbsence.Status})";

                    switch (dayAbsence.AbsenceType)
                    {
                        case "VACATION":
                            Scrivi(rowFerie, giorno, Ore(ore), daEcos ? "TEAL" : "BLUE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "BLUE", tipAss);
                            break;
                        case "PERMIT":
                            Scrivi(rowPerm, giorno, Ore(ore), daEcos ? "TEAL" : "ORANGE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "ORANGE", tipAss);
                            break;
                        case "SICKNESS":
                            Scrivi(rowMal, giorno, Ore(ore), daEcos ? "TEAL" : "PURPLE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "PURPLE", tipAss);
                            break;
                        case "INJURY":
                            Scrivi(rowInf, giorno, Ore(ore), daEcos ? "TEAL" : "YELLOW", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "YELLOW", tipAss);
                            break;
                    }

                    // Assenza parziale senza timbrature: mezza giornata scoperta.
                    if (ore < emp.DailyHours)
                    {
                        Scrivi(rowPres, giorno, "?", "RED", TooltipPermessoScoperto(emp.EmployeeName, data, ore));
                        Cella(rowOrd, giorno).Color = "RED";
                    }
                }
                else if (!isFestivo && data < oggi)
                {
                    // ── Giorno feriale passato senza niente: è un buco, e si vede
                    Scrivi(rowPres, giorno, "?", "RED");
                    Colora(righeFisse.Where(r => r != rowPres).ToArray(), straordRows, giorno, "RED");
                }
            }

            rowOrd.Total = Totale(rowOrd);
            rowFerie.Total = Totale(rowFerie);
            rowPerm.Total = Totale(rowPerm);
            rowMal.Total = Totale(rowMal);
            rowInf.Total = Totale(rowInf);

            result.Rows.Add(rowOrd);

            // Le righe di straordinario compaiono solo dove c'è davvero straordinario:
            // nove righe vuote per persona renderebbero la griglia illeggibile.
            foreach (var (_, band, _) in VociStraordinario)
            {
                HrCalendarRowDto riga = straordRows[band];
                riga.Total = Totale(riga);
                if (riga.Days.Values.Any(d => !string.IsNullOrEmpty(d.Text)))
                    result.Rows.Add(riga);
            }

            result.Rows.Add(rowPres);
            result.Rows.Add(rowFerie);
            result.Rows.Add(rowPerm);
            result.Rows.Add(rowMal);
            result.Rows.Add(rowInf);
        }

        return result;
    }

    // ── Aiuti del calendario ──────────────────────────────────────────────────

    private static HrCalendarCellDto Cella(HrCalendarRowDto riga, int giorno)
    {
        if (!riga.Days.TryGetValue(giorno, out HrCalendarCellDto? cella))
        {
            cella = new HrCalendarCellDto();
            riga.Days[giorno] = cella;
        }
        return cella;
    }

    private static void Scrivi(HrCalendarRowDto riga, int giorno, string testo, string colore, string? tooltip = null)
    {
        HrCalendarCellDto cella = Cella(riga, giorno);
        cella.Text = testo;
        cella.Color = colore;
        if (tooltip != null) cella.Tooltip = tooltip;
    }

    private static void Colora(
        IEnumerable<HrCalendarRowDto> righe,
        Dictionary<string, HrCalendarRowDto> straordinario,
        int giorno,
        string colore)
    {
        foreach (HrCalendarRowDto r in righe) Cella(r, giorno).Color = colore;
        foreach (HrCalendarRowDto r in straordinario.Values) Cella(r, giorno).Color = colore;
    }

    private static void PopolaStraordinario(
        Dictionary<string, HrCalendarRowDto> righe, DayRow dayData, int giorno)
    {
        Dictionary<string, string> fasce = LeggiFasce(dayData.BandsJson);
        foreach (var (_, band, _) in VociStraordinario)
        {
            if (!fasce.TryGetValue(band, out string? valore)) continue;
            int minuti = MinutesFrom(valore);
            if (minuti > 0) Scrivi(righe[band], giorno, OreTesto(minuti), "ORANGE");
        }
    }

    /// <summary>Ore come le scrive il VB: «7,5» → «7.5», «8h 0m» → «8», zero → vuoto.</summary>
    private static string OreTesto(int minuti) =>
        minuti <= 0 ? "" : (minuti / 60.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Ore(decimal ore) => ore.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Somma i numeri della riga (salta «P» e «?»), come AppUtils.CalcolaTotale.</summary>
    private static string Totale(HrCalendarRowDto riga)
    {
        double totale = 0;
        foreach (HrCalendarCellDto cella in riga.Days.Values)
        {
            if (string.IsNullOrEmpty(cella.Text) || cella.Text is "P" or "?") continue;
            if (double.TryParse(cella.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                totale += v;
        }
        return totale > 0 ? totale.ToString("0.#", CultureInfo.InvariantCulture) + "h" : "";
    }

    private static string NomeGiorno(DateTime dt) => dt.DayOfWeek switch
    {
        DayOfWeek.Monday => "L",
        DayOfWeek.Tuesday => "Ma",
        DayOfWeek.Wednesday => "Me",
        DayOfWeek.Thursday => "G",
        DayOfWeek.Friday => "V",
        DayOfWeek.Saturday => "S",
        DayOfWeek.Sunday => "D",
        _ => "",
    };

    private static string Causale(string absenceType) => absenceType switch
    {
        "VACATION" => "FERIE",
        "PERMIT" => "PERMESSO",
        "SICKNESS" => "MALATTIA",
        "INJURY" => "INFORTUNIO",
        _ => absenceType,
    };

    private static string TooltipPermessoScoperto(string nome, DateTime data, decimal ore) =>
        $"{nome} — {data:dd/MM/yyyy}\n⚠ Permesso parziale {Ore(ore)}h ma nessuna timbratura\n"
        + "Sollecitare inserimento timbrature o estendere a giornata intera";

    private static string Tooltip(string nome, DateTime data, DayRow g, string? tipoGiorno)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(nome).Append(" — ").Append(data.ToString("dd/MM/yyyy"));
        if (tipoGiorno != null) sb.Append(" (").Append(tipoGiorno).Append(')');
        sb.Append('\n');
        sb.Append("E1: ").Append(Orario(g.ClockIn1)).Append("  U1: ").Append(Orario(g.ClockOut1)).Append('\n');
        if (!string.IsNullOrEmpty(g.ClockIn2) && g.ClockIn2 != "--:--")
            sb.Append("E2: ").Append(Orario(g.ClockIn2)).Append("  U2: ").Append(Orario(g.ClockOut2)).Append('\n');

        if (tipoGiorno == null)
        {
            sb.Append("Pausa: ").Append(TimesheetRules.FormatDuration(g.BreakMinutes)).Append('\n');
            sb.Append("Ore: ").Append(TimesheetRules.FormatDuration(g.RegularMinutes));
        }

        string dettaglio = DettaglioStraordinario(g);
        if (dettaglio.Length > 0) sb.Append(tipoGiorno == null ? "\n" : "").Append("Straord:").Append(dettaglio);

        if (!string.IsNullOrEmpty(g.Note) && g.Note != "OK") sb.Append("\nNote: ").Append(g.Note);
        return sb.ToString();
    }

    private static string Orario(string? valore) => string.IsNullOrEmpty(valore) ? "--:--" : valore;

    private static string DettaglioStraordinario(DayRow g)
    {
        Dictionary<string, string> fasce = LeggiFasce(g.BandsJson);
        var parti = new List<string>();
        foreach (var (_, band, _) in VociStraordinario)
        {
            if (!fasce.TryGetValue(band, out string? valore) || MinutesFrom(valore) <= 0) continue;
            parti.Add($" {EtichetteFasceTooltip[band]}: {valore}");
        }
        return string.Join(" |", parti);
    }

    private sealed class CalendarEmployee
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? DepartmentName { get; set; }
        public string? EmplCode { get; set; }
        public bool MustPunch { get; set; } = true;
        public decimal DailyHours { get; set; } = 8.0m;
    }

    private sealed class CalendarAbsence
    {
        public int EmployeeId { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal? Hours { get; set; }
        public bool IsFullDay { get; set; }
        public string AbsenceType { get; set; } = "";
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
    }

    // ── #132 GIUSTIFICAZIONE DELLE ORE MANCANTI (clic su cella del calendario) ─
    //
    // Port di `dgCalendar_MouseDoubleClick` + `CausaleDialog` del programma «Timbrature»:
    // si apre la giornata scoperta, si vedono le ore che mancano e si sceglie la causale
    // che le copre. Le regole (quali causali, quante ore) le decide QUI il server: la
    // pagina disegna quello che le viene detto, non una seconda interpretazione.
    //
    // 🪤 Nell'originale queste giornate erano righe di `Absences`; qui sono righe di
    // `hr_absences`, che è la stessa tabella delle richieste ferie della Fase 2. Ne segue
    // una regola che nel VB non serviva: **una causale scritta da qui copre un giorno
    // solo**. Se sulla giornata c'è già un'assenza che viene da una richiesta a più giorni
    // (o da Ecos) non la si tocca da qui — spezzarla in silenzio lascerebbe la richiesta
    // approvata diversa da quello che è stato approvato.
    //
    // 🪤 Chi giustifica NON deve essere per forza un'altra persona (la regola del «secondo
    // occhio» vale per le rettifiche, che riscrivono le timbrature). Qui si dichiara una
    // causale su una giornata già passata, ed è quello che l'ufficio fa da anni col
    // programma originale: aggiungere il divieto vorrebbe dire cambiargli il lavoro.

    /// <summary>
    /// Cosa si può fare sulla giornata cliccata: quante ore mancano, quali causali sono
    /// ammesse, cosa c'è già scritto. <see cref="HrGiustificaInfoDto.Blocco"/> valorizzato
    /// = non si giustifica, e dentro c'è il perché.
    /// </summary>
    public HrGiustificaInfoDto GetGiustificaInfo(int employeeId, DateTime data)
    {
        DateTime giorno = data.Date;
        var info = new HrGiustificaInfoDto { EmployeeId = employeeId, Date = giorno };

        using MySqlConnection c = _db.Open();

        var emp = c.QueryFirstOrDefault<CalendarEmployee>(@"
            SELECT id AS EmployeeId,
                   CONCAT_WS(' ', first_name, last_name) AS EmployeeName,
                   hr_must_punch AS MustPunch, hr_daily_hours AS DailyHours
            FROM employees WHERE id = @Id AND status <> 'TERMINATED'",
            new { Id = employeeId });

        if (emp == null)
        {
            info.Blocco = "Dipendente non trovato o cessato.";
            return info;
        }

        info.EmployeeName = emp.EmployeeName;
        info.DailyHours = emp.DailyHours;

        // Le due porte dell'originale: solo giorni già passati, e mai i non lavorativi.
        if (giorno >= DateTime.Today)
        {
            info.Blocco = "Si giustificano solo le giornate già passate.";
            return info;
        }
        if (giorno.DayOfWeek == DayOfWeek.Saturday || TimesheetRules.IsHoliday(giorno))
        {
            info.Blocco = "Giornata non lavorativa: non c'è niente da giustificare.";
            return info;
        }

        // Quello che risulta già scritto sulla giornata. Se ci fossero due assenze
        // sovrapposte vince quella di un giorno solo: è la nostra, quella modificabile.
        var assenza = c.QueryFirstOrDefault<GiustificaAssenza>(@"
            SELECT id AS Id, date_from AS DateFrom, date_to AS DateTo, hours AS Hours,
                   absence_type AS AbsenceType, source AS Source, status AS Status
            FROM hr_absences
            WHERE employee_id = @Id AND status IN ('APPROVED', 'PENDING')
              AND date_from <= @G AND date_to >= @G
            ORDER BY (date_from = date_to) DESC, id DESC
            LIMIT 1",
            new { Id = employeeId, G = giorno });

        var day = c.QueryFirstOrDefault<GiustificaGiornata>(@"
            SELECT regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes
            FROM hr_days WHERE employee_id = @Id AND work_date = @G",
            new { Id = employeeId, G = giorno });

        if (assenza != null)
        {
            info.CausaleCorrente = HrCausali.Codice(assenza.AbsenceType);
            info.OreCorrenti = assenza.Hours ?? emp.DailyHours;

            if (string.Equals(assenza.Source, "ECOS", StringComparison.OrdinalIgnoreCase))
            {
                // Ecos è il padrone del suo dato: qui si guarda e basta.
                info.Blocco = "L'assenza arriva da Ecos: si corregge là, non da qui.";
                return info;
            }
            if (assenza.DateFrom.Date != assenza.DateTo.Date)
            {
                info.Blocco =
                    $"Coperta da una richiesta dal {assenza.DateFrom:dd/MM/yyyy} al {assenza.DateTo:dd/MM/yyyy}: "
                    + "si modifica dalle Richieste.";
                return info;
            }

            info.PuoRimuovere = true;
        }

        // Timbrature vere = giornata parziale: si può solo completarla (PE o IN). Senza
        // timbrature — assenza piena o forfettario — vale l'elenco intero, come nel VB.
        info.Causali = day != null
            ? new List<string> { HrCausali.Permesso, HrCausali.Infortunio }
            : new List<string> { HrCausali.Ferie, HrCausali.Permesso, HrCausali.Malattia, HrCausali.Infortunio };

        info.OreLavorate = day == null
            ? 0m
            : Math.Round((decimal)(day.RegularMinutes + day.OvertimeMinutes) / 60m, 2);
        info.OreMancanti = Math.Max(0m, emp.DailyHours - info.OreLavorate);

        // Niente da coprire e niente da togliere: è la stessa informazione che dava il
        // messaggio «Nessuna ora da giustificare per questo giorno» dell'originale.
        if (info.OreMancanti <= 0m && !info.PuoRimuovere)
            info.Blocco = "Nessuna ora da giustificare per questo giorno.";

        return info;
    }

    /// <summary>
    /// Scrive (o toglie) la causale della giornata. Torna null se è andata, altrimenti il
    /// motivo — le stesse guardie di <see cref="GetGiustificaInfo"/>, rifatte qui perché
    /// fra l'apertura del dialogo e il salvataggio può essere cambiato tutto.
    /// </summary>
    public string? SaveGiustifica(HrGiustificaRequest req, int autoreId)
    {
        DateTime giorno = req.Date.Date;
        HrGiustificaInfoDto info = GetGiustificaInfo(req.EmployeeId, giorno);
        if (!string.IsNullOrEmpty(info.Blocco)) return info.Blocco;

        string causale = (req.Causale ?? "").Trim().ToUpperInvariant();

        using MySqlConnection c = _db.Open();

        if (causale.Length == 0)
        {
            if (!info.PuoRimuovere) return "Su questa giornata non c'è nessuna causale da togliere.";

            c.Execute(@"DELETE FROM hr_absences
                        WHERE employee_id = @Id AND date_from = @G AND date_to = @G
                          AND source <> 'ECOS'",
                new { Id = req.EmployeeId, G = giorno });
            return null;
        }

        if (!info.Causali.Contains(causale))
        {
            return info.Causali.Count == 2
                ? "La giornata ha timbrature: si può solo completarla con PE (permesso) o IN (infortunio)."
                : "Causale non valida: ammesse FE, PE, MA, IN.";
        }

        string? tipo = HrCausali.TipoAssenza(causale);
        if (tipo == null) return "Causale non valida: ammesse FE, PE, MA, IN.";

        // Ore: quelle chieste se stanno dentro il buco, altrimenti il buco intero — come il
        // dialogo originale, che proponeva sempre e solo le ore mancanti.
        decimal ore = req.Hours is > 0m && req.Hours.Value <= info.OreMancanti
            ? req.Hours.Value
            : info.OreMancanti;
        if (ore <= 0m) return "Nessuna ora da giustificare per questo giorno.";

        bool giornataPiena = ore >= info.DailyHours;

        // 🪤 `created_by`/`approved_by` hanno la chiave esterna su `employees`: un id 0
        // (token senza dipendente collegato) farebbe fallire l'INSERT con un 500 invece che
        // con un messaggio. Null vuol dire «non lo sappiamo», ed è quello che la colonna ammette.
        int? autore = autoreId > 0 ? autoreId : null;

        // Una riga per giornata: se ce n'è già una nostra la si riscrive, non se ne aggiunge
        // una seconda (il calendario ne mostrerebbe una sola e l'altra resterebbe invisibile).
        int aggiornate = c.Execute(@"
            UPDATE hr_absences
               SET absence_type = @Tipo, hours = @Ore, is_full_day = @Piena,
                   status = 'APPROVED', source = 'MANUAL', created_by = @Autore,
                   approved_by = @Autore, approved_at = CURRENT_TIMESTAMP
             WHERE employee_id = @Id AND date_from = @G AND date_to = @G AND source <> 'ECOS'",
            new { Tipo = tipo, Ore = ore, Piena = giornataPiena, Autore = autore, Id = req.EmployeeId, G = giorno });

        if (aggiornate == 0)
        {
            c.Execute(@"
                INSERT INTO hr_absences
                    (employee_id, date_from, date_to, hours, is_full_day, absence_type,
                     status, source, notes, created_by, approved_by, approved_at)
                VALUES
                    (@Id, @G, @G, @Ore, @Piena, @Tipo, 'APPROVED', 'MANUAL',
                     'Giustificazione ore mancanti da Calendario mensile', @Autore, @Autore, CURRENT_TIMESTAMP)",
                new { Id = req.EmployeeId, G = giorno, Ore = ore, Piena = giornataPiena, Tipo = tipo, Autore = autore });
        }

        return null;
    }

    private sealed class GiustificaAssenza
    {
        public int Id { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal? Hours { get; set; }
        public string AbsenceType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private sealed class GiustificaGiornata
    {
        public int RegularMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
    }

    // ── SOLLECITI DELLE TIMBRATURE MANCANTI ───────────────────────────────────
    //
    // Port dei due pulsanti del calendario originale. Il «?» rosso del calendario è la
    // fonte: si sollecita quello che la griglia mostra, non un secondo conteggio fatto
    // per conto suo — altrimenti la mail direbbe giorni diversi da quelli che la persona
    // vede sullo schermo.

    private static readonly string[] NomiMesi =
    {
        "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno",
        "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre",
    };

    /// <summary>
    /// Chi ha giornate col «?» nel mese, con il testo del sollecito già pronto.
    /// <paramref name="employeeId"/> valorizzato = solo quella persona, come il filtro
    /// della pagina (nel VB il sollecito rispetta il filtro dipendente attivo).
    /// </summary>
    public HrRemindersDto GetReminders(int year, int month, int? departmentId, int? employeeId)
    {
        HrMonthlyCalendarDto calendario = GetMonthlyCalendar(year, month, departmentId);

        var risultato = new HrRemindersDto { Year = year, Month = month };

        // I buchi stanno sulla riga PRESENZA: sono le celle col «?».
        var buchiPerDipendente = calendario.Rows
            .Where(r => r.VoceType == "PRESENZA")
            .Where(r => employeeId == null || r.EmployeeId == employeeId.Value)
            .Select(r => (
                r.EmployeeId,
                r.EmployeeKey,
                Giorni: r.Days.Where(d => d.Value.Text == "?").Select(d => d.Key).OrderBy(g => g).ToList()))
            .Where(x => x.Giorni.Count > 0)
            .ToList();

        if (buchiPerDipendente.Count == 0) return risultato;

        int[] ids = buchiPerDipendente.Select(x => x.EmployeeId).ToArray();

        using MySqlConnection c = _db.Open();

        var recapiti = c.Query<(int Id, string? Email, string FirstName)>(@"
            SELECT id AS Id, email AS Email, COALESCE(first_name, '') AS FirstName
            FROM employees WHERE id IN @Ids", new { Ids = ids })
            .ToDictionary(x => x.Id, x => (x.Email, x.FirstName));

        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        var ultimoSollecito = c.Query<(int EmployeeId, DateTime SentAt)>(@"
            SELECT employee_id AS EmployeeId, MAX(sent_at) AS SentAt
            FROM hr_reminders
            WHERE employee_id IN @Ids AND work_date BETWEEN @Primo AND @Ultimo
            GROUP BY employee_id", new { Ids = ids, Primo = primo, Ultimo = ultimo })
            .ToDictionary(x => x.EmployeeId, x => x.SentAt);

        string mese = NomiMesi[month - 1];

        foreach (var (idDipendente, nomeCompleto, giorni) in buchiPerDipendente)
        {
            recapiti.TryGetValue(idDipendente, out (string? Email, string FirstName) recapito);
            string nome = string.IsNullOrWhiteSpace(recapito.FirstName) ? nomeCompleto : recapito.FirstName;
            string elenco = string.Join(", ", giorni.Select(g => $"{g} {mese}"));

            risultato.Targets.Add(new HrReminderTargetDto
            {
                EmployeeId = idDipendente,
                EmployeeName = nomeCompleto,
                Email = recapito.Email,
                MissingDays = giorni,
                LastReminderAt = ultimoSollecito.TryGetValue(idDipendente, out DateTime quando) ? quando : null,
                Subject = $"Timbrature mancanti - {mese} {year}",
                // I due testi dell'originale: dal client di posta si può anche inserire la
                // causale su eTime, dall'invio automatico si chiede solo di comunicarla.
                MailtoBody = TestoSollecito(nome, elenco, "Si prega di comunicare e/o inserire su eTime le relative causali di assenza."),
                Body = TestoSollecito(nome, elenco, "Si prega di comunicare le relative causali di assenza."),
            });
        }

        return risultato;
    }

    /// <summary>Il testo del sollecito, parola per parola come nel programma originale.</summary>
    private static string TestoSollecito(string nome, string giorni, string richiesta) =>
        $"""
        Gentile {nome},

        risultano mancanti le timbrature per i seguenti giorni:
        {giorni}

        {richiesta}

        Cordiali saluti,
        Ufficio Risorse Umane
        """;

    /// <summary>
    /// Segna come sollecitate le giornate indicate. Una riga per giornata (non per email):
    /// il secondo sollecito sullo stesso giorno aggiorna la data.
    ///
    /// <para>Dalla M117 si conserva anche il testo (destinatario, oggetto, corpo): è quello
    /// che la Cronologia Email rilegge. Le N giornate di uno stesso invio portano la stessa
    /// mail, come il <c>MailLog</c> dell'originale.</para>
    /// </summary>
    public void MarkReminders(
        int year, int month,
        IEnumerable<(int EmployeeId, List<int> Days, string? Email, string? Subject, string? Body)> solleciti,
        int sentBy, string channel)
    {
        var righe = solleciti
            .SelectMany(x => x.Days.Select(g => new
            {
                x.EmployeeId,
                WorkDate = new DateTime(year, month, g),
                SentBy = sentBy,
                Channel = channel,
                x.Email,
                x.Subject,
                x.Body,
            }))
            .ToList();

        if (righe.Count == 0) return;

        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel, email, subject, body)
            VALUES (@EmployeeId, @WorkDate, @SentBy, @Channel, @Email, @Subject, @Body)
            ON DUPLICATE KEY UPDATE sent_at = NOW(), sent_by = VALUES(sent_by), channel = VALUES(channel),
                                    email = VALUES(email), subject = VALUES(subject), body = VALUES(body)",
            righe);
    }

    // ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

    public HrQuadraturaMonthDto GetQuadratura(int year, int month, int? departmentId)
    {
        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var p = new DynamicParameters();
        p.Add("Primo", primo);
        p.Add("Ultimo", ultimo);

        string empSql = @"
            SELECT DISTINCT e.id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   COALESCE(ed.department_id, 0) AS DepartmentId,
                   COALESCE(d.name, 'Senza reparto') AS DepartmentName,
                   e.hr_must_punch AS MustPunch,
                   e.hr_daily_hours AS DailyHours,
                   -- 🪤 `SELECT DISTINCT` + `ORDER BY` su colonne che non sono nella SELECT:
                   -- MySQL lo rifiuta in blocco (ONLY_FULL_GROUP_BY). Cognome e nome stanno
                   -- qui solo per poter ordinare come si è sempre ordinato.
                   e.last_name AS LastName, e.first_name AS FirstName
            FROM employees e
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE e.status = 'ACTIVE' AND e.emp_type = 'INTERNAL' AND e.user_role <> 'ADMIN' AND e.first_name NOT LIKE '[%'";

        if (departmentId.HasValue)
        {
            empSql += " AND ed.department_id = @DeptId";
            p.Add("DeptId", departmentId.Value);
        }

        empSql += " ORDER BY d.name, e.last_name, e.first_name";

        var employees = c.Query<(int EmployeeId, string EmployeeName, int DepartmentId, string DepartmentName, bool MustPunch, decimal DailyHours)>(empSql, p).ToList();

        // 1. Ore presenze da hr_days nel mese
        var presenze = c.Query<(int EmployeeId, int RegularMin, int OvertimeMin)>(@"
            SELECT employee_id AS EmployeeId,
                   SUM(regular_minutes) AS RegularMin,
                   SUM(overtime_minutes) AS OvertimeMin
            FROM hr_days
            WHERE work_date BETWEEN @Primo AND @Ultimo
            GROUP BY employee_id", p)
            .ToDictionary(x => x.EmployeeId, x => Math.Round((decimal)(x.RegularMin + x.OvertimeMin) / 60m, 1));

        // 2. Ore assenze approvate da hr_absences nel mese
        var assenze = c.Query<HrAbsenceDto>(@"
            SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType
            FROM hr_absences a
            WHERE a.status = 'APPROVED'
              AND a.date_from <= @Ultimo AND a.date_to >= @Primo", p).ToList();

        var assenzePerEmp = new Dictionary<int, decimal>();
        foreach (var a in assenze)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            decimal h = 0;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
            {
                if (!TimesheetRules.IsHoliday(dt))
                {
                    h += a.Hours ?? 8.0m;
                }
            }
            assenzePerEmp[a.EmployeeId] = assenzePerEmp.GetValueOrDefault(a.EmployeeId) + h;
        }

        // 3. Ore consuntivate su commesse da timesheet_entries
        var timesheet = c.Query<(int EmployeeId, decimal DirectHours, decimal InternalHours)>(@"
            SELECT te.employee_id AS EmployeeId,
                   SUM(CASE WHEN COALESCE(p.is_internal, 0) = 0 THEN te.hours ELSE 0 END) AS DirectHours,
                   SUM(CASE WHEN COALESCE(p.is_internal, 0) = 1 THEN te.hours ELSE 0 END) AS InternalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.work_date BETWEEN @Primo AND @Ultimo
            GROUP BY te.employee_id", p)
            .ToDictionary(x => x.EmployeeId);

        var rows = new List<HrQuadraturaRowDto>();
        var deptDict = new Dictionary<int, HrQuadraturaDepartmentDto>();

        decimal totPres = 0, totDir = 0, totInt = 0, totAbs = 0;

        foreach (var emp in employees)
        {
            decimal presHours = presenze.GetValueOrDefault(emp.EmployeeId, 0);

            if (!emp.MustPunch && presHours == 0)
            {
                int ggLavorativi = 0;
                for (DateTime dt = primo; dt <= ultimo; dt = dt.AddDays(1))
                    if (!TimesheetRules.IsHoliday(dt) && dt < DateTime.Today) ggLavorativi++;
                presHours = ggLavorativi * emp.DailyHours;
            }

            timesheet.TryGetValue(emp.EmployeeId, out var tsData);
            decimal dirHours = Math.Round(tsData.DirectHours, 1);
            decimal intHours = Math.Round(tsData.InternalHours, 1);
            decimal absHours = Math.Round(assenzePerEmp.GetValueOrDefault(emp.EmployeeId, 0), 1);
            decimal totTs = dirHours + intHours;
            decimal diff = Math.Round(totTs - presHours, 1);
            decimal cov = presHours > 0 ? Math.Round((totTs / presHours) * 100m, 1) : 100m;

            var riga = new HrQuadraturaRowDto
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = emp.EmployeeName,
                DepartmentName = emp.DepartmentName,
                PresenzeHours = presHours,
                DirectTimesheetHours = dirHours,
                InternalTimesheetHours = intHours,
                AbsenceHours = absHours,
                TotalTimesheetHours = totTs,
                DifferenceHours = diff,
                CoveragePercent = cov
            };
            rows.Add(riga);

            totPres += presHours;
            totDir += dirHours;
            totInt += intHours;
            totAbs += absHours;

            if (!deptDict.TryGetValue(emp.DepartmentId, out var dept))
            {
                dept = new HrQuadraturaDepartmentDto
                {
                    DepartmentId = emp.DepartmentId,
                    DepartmentName = emp.DepartmentName,
                };
                deptDict[emp.DepartmentId] = dept;
            }
            dept.TotalPresenzeHours += presHours;
            dept.TotalDirectHours += dirHours;
            dept.TotalInternalHours += intHours;
            dept.TotalAbsenceHours += absHours;
            dept.TotalTimesheetHours += totTs;
            dept.DifferenceHours += diff;
        }

        foreach (var d in deptDict.Values)
        {
            d.CoveragePercent = d.TotalPresenzeHours > 0
                ? Math.Round((d.TotalTimesheetHours / d.TotalPresenzeHours) * 100m, 1)
                : 100m;
        }

        decimal totalTimesheet = totDir + totInt;
        decimal overallCov = totPres > 0 ? Math.Round((totalTimesheet / totPres) * 100m, 1) : 100m;

        return new HrQuadraturaMonthDto
        {
            Year = year,
            Month = month,
            Rows = rows,
            Departments = deptDict.Values.OrderBy(d => d.DepartmentName).ToList(),
            TotalPresenzeHours = totPres,
            TotalDirectHours = totDir,
            TotalInternalHours = totInt,
            TotalAbsenceHours = totAbs,
            TotalTimesheetHours = totalTimesheet,
            OverallCoveragePercent = overallCov
        };
    }

    private static void SyncToResourcePlanner(
        MySqlConnection c, int employeeId, DateTime dateFrom, DateTime dateTo, string absenceType, bool isApproved)
    {
        if (!string.Equals(absenceType, "VACATION", StringComparison.OrdinalIgnoreCase)) return;

        int tableExists = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'res_assignments'");
        if (tableExists == 0) return;

        if (isApproved)
        {
            int exists = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM res_assignments
                WHERE employee_id = @EmployeeId AND tipo = 'FERIE'
                  AND data_inizio = @DateFrom AND data_fine = @DateTo",
                new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });

            if (exists == 0)
            {
                c.Execute(@"
                    INSERT INTO res_assignments
                        (employee_id, tipo, data_inizio, data_fine, descrizione, created_at)
                    VALUES
                        (@EmployeeId, 'FERIE', @DateFrom, @DateTo, 'Ferie approvate (HR)', NOW())",
                    new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });
            }
        }
        else
        {
            c.Execute(@"
                DELETE FROM res_assignments
                WHERE employee_id = @EmployeeId AND tipo = 'FERIE'
                  AND data_inizio = @DateFrom AND data_fine = @DateTo",
                new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });
        }
    }

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
