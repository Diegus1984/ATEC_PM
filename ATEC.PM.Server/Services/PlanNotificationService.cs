using ATEC.PM.Shared.DTOs;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Confronta un'istantanea del piano risorse ("snapshot") con lo stato attuale per calcolare
/// le variazioni (nuove/modificate/cancellate) e le invia via email: al dipendente coinvolto,
/// al responsabile del suo reparto (escluse le modifiche fatte da lui stesso) e ai PM/ADMIN
/// (digest globale, escluse le proprie). Port di PlanNotificationService da ATEC.Risorse.Server.
/// </summary>
public class PlanNotificationService
{
    private readonly ResourcesDbService _rdb;
    private readonly EmailService _email;
    private readonly ILogger<PlanNotificationService> _logger;

    public PlanNotificationService(ResourcesDbService rdb, EmailService email, ILogger<PlanNotificationService> logger)
    {
        _rdb = rdb;
        _email = email;
        _logger = logger;
    }

    // ── Righe grezze (interno) ───────────────────────────────────

    private class Row
    {
        public int AssignmentId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? EmployeeEmail { get; set; }
        public string Tipo { get; set; } = "OP";
        public DateTime DataInizio { get; set; }
        public DateTime DataFine { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectCode { get; set; }
        public string? ProjectTitle { get; set; }
        public int? ServiceId { get; set; }
        public string? ServiceCod { get; set; }
        public int? OtherActivityId { get; set; }
        public string? OtherActivityDesc { get; set; }
        public string? Descrizione { get; set; }
        public int? AuthorId { get; set; }
        public string? AuthorName { get; set; }
    }

    private const string CurrentSql = @"
        SELECT a.id AS AssignmentId, a.employee_id AS EmployeeId,
               CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName, e.email AS EmployeeEmail,
               a.tipo AS Tipo, a.data_inizio AS DataInizio, a.data_fine AS DataFine,
               a.project_id AS ProjectId, p.code AS ProjectCode, p.title AS ProjectTitle,
               a.service_id AS ServiceId, s.cod AS ServiceCod,
               a.other_activity_id AS OtherActivityId, o.descrizione AS OtherActivityDesc,
               a.descrizione AS Descrizione,
               a.updated_by AS AuthorId, CONCAT_WS(' ', ub.first_name, ub.last_name) AS AuthorName
        FROM res_assignments a
        JOIN employees e ON e.id = a.employee_id
        LEFT JOIN projects p ON p.id = a.project_id
        LEFT JOIN res_services s ON s.id = a.service_id
        LEFT JOIN res_other_activities o ON o.id = a.other_activity_id
        LEFT JOIN employees ub ON ub.id = a.updated_by";

    private const string SnapshotSql = @"
        SELECT sn.assignment_id AS AssignmentId, sn.employee_id AS EmployeeId,
               CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName, e.email AS EmployeeEmail,
               sn.tipo AS Tipo, sn.data_inizio AS DataInizio, sn.data_fine AS DataFine,
               sn.project_id AS ProjectId, p.code AS ProjectCode, p.title AS ProjectTitle,
               sn.service_id AS ServiceId, s.cod AS ServiceCod,
               sn.other_activity_id AS OtherActivityId, o.descrizione AS OtherActivityDesc,
               sn.descrizione AS Descrizione,
               np.made_by AS AuthorId, CONCAT_WS(' ', ub.first_name, ub.last_name) AS AuthorName
        FROM res_plan_snapshots sn
        JOIN employees e ON e.id = sn.employee_id
        LEFT JOIN projects p ON p.id = sn.project_id
        LEFT JOIN res_services s ON s.id = sn.service_id
        LEFT JOIN res_other_activities o ON o.id = sn.other_activity_id
        LEFT JOIN res_notify_pending np ON np.assignment_id = sn.assignment_id
        LEFT JOIN employees ub ON ub.id = np.made_by
        WHERE sn.batch_id = @BatchId";

    private static string Label(Row r)
    {
        if (r.Tipo == "FERIE")
            return string.IsNullOrWhiteSpace(r.Descrizione) ? "Ferie" : r.Descrizione!;
        if (!string.IsNullOrWhiteSpace(r.ProjectCode) && !string.IsNullOrWhiteSpace(r.ProjectTitle))
            return $"{r.ProjectCode} — {r.ProjectTitle}";
        if (!string.IsNullOrWhiteSpace(r.ProjectCode)) return r.ProjectCode!;
        if (!string.IsNullOrWhiteSpace(r.ServiceCod)) return r.ServiceCod!;
        if (!string.IsNullOrWhiteSpace(r.OtherActivityDesc)) return r.OtherActivityDesc!;
        if (!string.IsNullOrWhiteSpace(r.Descrizione)) return r.Descrizione!;
        return r.Tipo == "FLEX" ? "Flessibile" : "Operativo";
    }

    private static string Periodo(Row r) => $"dal {r.DataInizio:dd/MM/yyyy} al {r.DataFine:dd/MM/yyyy}";

    private static bool SameContent(Row a, Row b) =>
        a.Tipo == b.Tipo && a.DataInizio == b.DataInizio && a.DataFine == b.DataFine &&
        a.ProjectId == b.ProjectId && a.ServiceId == b.ServiceId &&
        a.OtherActivityId == b.OtherActivityId && a.Descrizione == b.Descrizione;

    private record Changes(List<Row> New, List<Row> Changed, List<Row> Deleted, bool BaselineCreated);

    /// <summary>Confronta l'ultimo snapshot con lo stato attuale. BaselineCreated=true se non
    /// esisteva ancora nessuno snapshot (primo giro: scatta la prima foto, nessuna variazione).</summary>
    private Changes ComputeChanges(MySqlConnector.MySqlConnection c)
    {
        int? batchId = c.ExecuteScalar<int?>("SELECT MAX(id) FROM res_plan_snapshot_batches");
        List<Row> current = c.Query<Row>(CurrentSql).ToList();

        if (batchId == null)
        {
            TakeSnapshot(c, current);
            return new Changes(new(), new(), new(), true);
        }

        List<Row> snapshot = c.Query<Row>(SnapshotSql, new { BatchId = batchId }).ToList();
        var currentById = current.ToDictionary(r => r.AssignmentId);
        var snapById = snapshot.ToDictionary(r => r.AssignmentId);

        List<Row> added = current.Where(r => !snapById.ContainsKey(r.AssignmentId)).ToList();
        List<Row> changed = current
            .Where(r => snapById.TryGetValue(r.AssignmentId, out Row? s) &&
                        s.EmployeeId == r.EmployeeId && !SameContent(r, s))
            .ToList();
        List<Row> deleted = snapshot.Where(r => !currentById.ContainsKey(r.AssignmentId)).ToList();

        // Riassegnata (stesso id, persona diversa): per il vecchio dipendente è una rimozione
        // (annotata con il nome del nuovo), per il nuovo una nuova attività. L'autore delle due
        // righe è chi ha fatto la riassegnazione (updated_by attuale), non chi risulta sulla foto.
        foreach (Row curr in current)
        {
            if (!snapById.TryGetValue(curr.AssignmentId, out Row? snap) || snap.EmployeeId == curr.EmployeeId)
                continue;
            snap.Descrizione = string.IsNullOrWhiteSpace(snap.Descrizione)
                ? $"riassegnata a {curr.EmployeeName}"
                : $"{snap.Descrizione} — riassegnata a {curr.EmployeeName}";
            snap.AuthorId = curr.AuthorId;
            snap.AuthorName = curr.AuthorName;
            deleted.Add(snap);
            added.Add(curr);
        }

        return new Changes(added, changed, deleted, false);
    }

    private static void TakeSnapshot(MySqlConnector.MySqlConnection c, List<Row> current)
    {
        int batchId = c.ExecuteScalar<int>(
            "INSERT INTO res_plan_snapshot_batches (created_utc) VALUES (@Now); SELECT LAST_INSERT_ID()",
            new { Now = DateTime.UtcNow });

        foreach (Row r in current)
        {
            c.Execute(@"
                INSERT INTO res_plan_snapshots
                    (batch_id, assignment_id, employee_id, tipo, data_inizio, data_fine, project_id, service_id, other_activity_id, descrizione)
                VALUES (@BatchId, @AssignmentId, @EmployeeId, @Tipo, @DataInizio, @DataFine, @ProjectId, @ServiceId, @OtherActivityId, @Descrizione)",
                new { BatchId = batchId, r.AssignmentId, r.EmployeeId, r.Tipo, r.DataInizio, r.DataFine, r.ProjectId, r.ServiceId, r.OtherActivityId, r.Descrizione });
        }
    }

    // ── Conteggio pendenti (badge toolbar, nessun invio) ─────────

    public NotifyPendingDto ComputePending()
    {
        using var c = _rdb.Open();
        Changes ch = ComputeChanges(c);

        var byEmp = new Dictionary<int, NotifyPendingEmployee>();
        void Bump(Row r, Action<NotifyPendingEmployee> inc)
        {
            if (!byEmp.TryGetValue(r.EmployeeId, out NotifyPendingEmployee? e))
            {
                e = new NotifyPendingEmployee
                {
                    EmployeeId = r.EmployeeId,
                    EmployeeName = r.EmployeeName,
                    HasEmail = !string.IsNullOrWhiteSpace(r.EmployeeEmail),
                };
                byEmp[r.EmployeeId] = e;
            }
            inc(e);
        }
        foreach (Row r in ch.New) Bump(r, e => e.Nuove++);
        foreach (Row r in ch.Changed) Bump(r, e => e.Modificate++);
        foreach (Row r in ch.Deleted) Bump(r, e => e.Cancellate++);

        return new NotifyPendingDto
        {
            TotalChanges = ch.New.Count + ch.Changed.Count + ch.Deleted.Count,
            EmailConfigurata = _email.Enabled,
            Employees = byEmp.Values.OrderBy(e => e.EmployeeName).ToList(),
        };
    }

    // ── Anteprima (nessun invio, nessuna nuova foto) ─────────────

    public DigestPreviewDto PreviewDigest()
    {
        using var c = _rdb.Open();
        Changes ch = ComputeChanges(c);
        return new DigestPreviewDto { Dipendenti = GroupByEmployee(ch) };
    }

    public SelectivePreviewDto BuildSelectivePreview()
    {
        using var c = _rdb.Open();
        Changes ch = ComputeChanges(c);
        List<DigestPreviewPerson> people = GroupByEmployee(ch);
        return new SelectivePreviewDto
        {
            Dipendenti = people.Select(p => new SelectivePerson
            {
                EmployeeId = p.EmployeeId,
                EmployeeName = p.EmployeeName,
                HasEmail = p.HasEmail,
                Righe = p.Righe,
            }).ToList(),
        };
    }

    private static List<DigestPreviewPerson> GroupByEmployee(Changes ch)
    {
        var byEmp = new Dictionary<int, DigestPreviewPerson>();
        DigestPreviewPerson Get(Row r)
        {
            if (!byEmp.TryGetValue(r.EmployeeId, out DigestPreviewPerson? p))
            {
                p = new DigestPreviewPerson
                {
                    EmployeeId = r.EmployeeId,
                    EmployeeName = r.EmployeeName,
                    HasEmail = !string.IsNullOrWhiteSpace(r.EmployeeEmail),
                };
                byEmp[r.EmployeeId] = p;
            }
            return p;
        }
        foreach (Row r in ch.New)
            Get(r).Righe.Add(new PlanChangeLine { AssignmentId = r.AssignmentId, Kind = "new", Attivita = Label(r), Periodo = Periodo(r), Note = r.Descrizione, AutoreNome = r.AuthorName });
        foreach (Row r in ch.Changed)
            Get(r).Righe.Add(new PlanChangeLine { AssignmentId = r.AssignmentId, Kind = "changed", Attivita = Label(r), Periodo = Periodo(r), Note = r.Descrizione, AutoreNome = r.AuthorName });
        foreach (Row r in ch.Deleted)
            Get(r).Righe.Add(new PlanChangeLine { AssignmentId = r.AssignmentId, Kind = "deleted", Attivita = Label(r), Periodo = Periodo(r), Note = r.Descrizione, AutoreNome = r.AuthorName });
        return byEmp.Values.OrderBy(p => p.EmployeeName).ToList();
    }

    // ── Digest completo (automatico o "Esegui ora") ──────────────

    public NotifySendResultDto SendDigest(string trigger)
    {
        using var c = _rdb.Open();
        Changes ch = ComputeChanges(c);

        if (ch.BaselineCreated)
        {
            LogRun(c, trigger, 0, 0, "Prima istantanea creata: nessuna variazione da notificare.");
            return new NotifySendResultDto { BaselineCreated = true, Message = "Prima istantanea del piano creata." };
        }

        NotifySendResultDto result = SendForChanges(c, ch);

        // Foto nuova = baseline per il prossimo confronto; le cancellazioni processate sono "consumate".
        List<Row> current = c.Query<Row>(CurrentSql).ToList();
        TakeSnapshot(c, current);
        if (ch.Deleted.Count > 0)
        {
            var ids = ch.Deleted.Select(r => r.AssignmentId).ToList();
            c.Execute("DELETE FROM res_notify_pending WHERE assignment_id IN @Ids", new { Ids = ids });
        }

        LogRun(c, trigger, result.EmailInviate, result.DipendentiSenzaEmail, result.Message);
        return result;
    }

    /// <summary>Invia (accoda) le mail per l'insieme di variazioni dato, senza toccare gli snapshot.</summary>
    private NotifySendResultDto SendForChanges(MySqlConnector.MySqlConnection c, Changes ch)
    {
        var result = new NotifySendResultDto();
        List<DigestPreviewPerson> people = GroupByEmployee(ch);

        // 1) Dipendenti
        foreach (DigestPreviewPerson p in people)
        {
            if (p.Righe.Count == 0) continue;
            if (!p.HasEmail) { result.DipendentiSenzaEmail++; continue; }

            List<string> autori = p.Righe.Select(r => r.AutoreNome).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList()!;
            string? modificheDi = autori.Count == 1 ? autori[0] : null;

            var employeeRow = c.QueryFirstOrDefault<(string Email, string Name)>(
                "SELECT email AS Email, CONCAT_WS(' ', first_name, last_name) AS Name FROM employees WHERE id=@Id",
                new { Id = p.EmployeeId });

            _email.QueueSummaryMail(new PlanSummaryMailData(
                employeeRow.Email, employeeRow.Name, modificheDi,
                p.Righe.Where(r => r.Kind == "new").ToList(),
                p.Righe.Where(r => r.Kind == "changed").ToList(),
                p.Righe.Where(r => r.Kind == "deleted").ToList()));

            result.EmailInviate++;
            result.NotifiedNames.Add(p.EmployeeName);
        }

        // 2) Responsabili di reparto (escluse le modifiche fatte da loro stessi)
        List<DepartmentSummaryMailData> deptDigests = BuildDepartmentDigests(c, people, global: false);
        foreach (DepartmentSummaryMailData d in deptDigests)
        {
            if (d.PerTecnico.Sum(t => t.Righe.Count) == 0) continue;
            _email.QueueDepartmentMail(d);
            result.ResponsabiliNotificati++;
            result.ResponsabiliNotificatiNomi.Add(d.ToName);
        }

        // 3) PM/ADMIN (digest globale, escluse le modifiche fatte da loro stessi)
        List<DepartmentSummaryMailData> pmDigests = BuildDepartmentDigests(c, people, global: true);
        foreach (DepartmentSummaryMailData d in pmDigests)
        {
            if (d.PerTecnico.Sum(t => t.Righe.Count) == 0) continue;
            _email.QueueDepartmentMail(d);
            result.PmNotificati++;
            result.PmNotificatiNomi.Add(d.ToName);
        }

        // Il conteggio riflette quante persone AVREBBERO ricevuto una mail (calcolo/snapshot già
        // eseguiti); se SMTP non è configurato nessuna email viene realmente spedita (QueueXxxMail
        // scarta silenziosamente) — lo segnaliamo esplicitamente per non confondere l'admin.
        string smtpNote = _email.Enabled ? "" : " (SMTP non configurato: nessuna email realmente inviata)";
        result.Message = $"{result.EmailInviate} dipendenti notificati" +
            (result.DipendentiSenzaEmail > 0 ? $", {result.DipendentiSenzaEmail} senza email" : "") +
            (result.ResponsabiliNotificati > 0 ? $", {result.ResponsabiliNotificati} responsabili" : "") +
            (result.PmNotificati > 0 ? $", {result.PmNotificati} PM" : "") + "." + smtpNote;
        return result;
    }

    /// <summary>Digest per responsabili di reparto (global=false) o PM/ADMIN (global=true, tutti i
    /// dipendenti, non solo il proprio reparto). Esclude le righe il cui autore è il destinatario stesso.</summary>
    private List<DepartmentSummaryMailData> BuildDepartmentDigests(
        MySqlConnector.MySqlConnection c, List<DigestPreviewPerson> people, bool global)
    {
        var result = new List<DepartmentSummaryMailData>();
        if (people.All(p => p.Righe.Count == 0)) return result;

        if (global)
        {
            List<(int Id, string Name, string? Email)> pms = c.Query<(int, string, string?)>(
                "SELECT id, CONCAT_WS(' ', first_name, last_name), email FROM employees WHERE user_role IN ('PM','ADMIN') AND status='ACTIVE'")
                .ToList();

            foreach ((int id, string name, string? email) in pms)
            {
                if (string.IsNullOrWhiteSpace(email)) continue;
                var perTecnico = new List<(string, List<PlanChangeLine>)>();
                foreach (DigestPreviewPerson p in people)
                {
                    List<PlanChangeLine> righe = p.Righe.Where(r => r.AutoreNome != name).ToList();
                    if (righe.Count > 0) perTecnico.Add((p.EmployeeName, righe));
                }
                if (perTecnico.Sum(t => t.Item2.Count) > 0)
                    result.Add(new DepartmentSummaryMailData(email, name, null, true, perTecnico));
            }
            return result;
        }

        List<int> employeeIds = people.Where(p => p.Righe.Count > 0).Select(p => p.EmployeeId).ToList();
        if (employeeIds.Count == 0) return result;

        List<(int TecnicoId, string RespName, string? RespEmail, string DeptName)> deptRows = c.Query<(int, string, string?, string)>(@"
            SELECT ed.employee_id, CONCAT_WS(' ', resp.first_name, resp.last_name), resp.email, d.name
            FROM employee_departments ed
            JOIN departments d ON d.id = ed.department_id
            JOIN employee_departments rd ON rd.department_id = ed.department_id AND rd.is_responsible = 1
            JOIN employees resp ON resp.id = rd.employee_id AND resp.status = 'ACTIVE'
            WHERE ed.employee_id IN @Ids", new { Ids = employeeIds }).ToList();

        foreach (var group in deptRows.GroupBy(r => new { r.RespName, r.RespEmail, r.DeptName }))
        {
            if (string.IsNullOrWhiteSpace(group.Key.RespEmail)) continue;
            var perTecnico = new List<(string, List<PlanChangeLine>)>();
            foreach (int tecnicoId in group.Select(g => g.TecnicoId).Distinct())
            {
                DigestPreviewPerson? p = people.FirstOrDefault(p => p.EmployeeId == tecnicoId);
                if (p == null) continue;
                List<PlanChangeLine> righe = p.Righe.Where(r => r.AutoreNome != group.Key.RespName).ToList();
                if (righe.Count > 0) perTecnico.Add((p.EmployeeName, righe));
            }
            if (perTecnico.Sum(t => t.Item2.Count) > 0)
                result.Add(new DepartmentSummaryMailData(group.Key.RespEmail!, group.Key.RespName, group.Key.DeptName, false, perTecnico));
        }
        return result;
    }

    // ── Invio selettivo (dialog "Notifica subito") ───────────────

    public NotifySendResultDto SendSelected(List<int> assignmentIds, string trigger)
    {
        using var c = _rdb.Open();
        Changes all = ComputeChanges(c);
        if (all.BaselineCreated)
            return new NotifySendResultDto { BaselineCreated = true, Message = "Prima istantanea del piano creata." };

        var idSet = assignmentIds.ToHashSet();
        var filtered = new Changes(
            all.New.Where(r => idSet.Contains(r.AssignmentId)).ToList(),
            all.Changed.Where(r => idSet.Contains(r.AssignmentId)).ToList(),
            all.Deleted.Where(r => idSet.Contains(r.AssignmentId)).ToList(),
            false);

        NotifySendResultDto result = SendForChanges(c, filtered);

        SyncSnapshotForAssignments(c, filtered);

        LogRun(c, trigger, result.EmailInviate, result.DipendentiSenzaEmail, result.Message);
        return result;
    }

    /// <summary>Aggiorna lo snapshot corrente SOLO per gli assignment_id appena notificati (le altre
    /// variazioni restano pendenti per il digest automatico successivo).</summary>
    private static void SyncSnapshotForAssignments(MySqlConnector.MySqlConnection c, Changes filtered)
    {
        int? batchId = c.ExecuteScalar<int?>("SELECT MAX(id) FROM res_plan_snapshot_batches");
        if (batchId == null) return;

        // Prima le cancellazioni, POI gli upsert: una riassegnazione ha lo stesso assignment_id
        // sia tra le Deleted (vecchio dipendente) sia tra le New (nuovo) — cancellando per ultimi
        // si perderebbe la foto appena aggiornata e la modifica verrebbe rinotificata.
        if (filtered.Deleted.Count > 0)
        {
            var ids = filtered.Deleted.Select(r => r.AssignmentId).ToList();
            c.Execute("DELETE FROM res_plan_snapshots WHERE batch_id=@BatchId AND assignment_id IN @Ids", new { BatchId = batchId, Ids = ids });
            c.Execute("DELETE FROM res_notify_pending WHERE assignment_id IN @Ids", new { Ids = ids });
        }

        foreach (Row r in filtered.New.Concat(filtered.Changed))
        {
            c.Execute(@"
                INSERT INTO res_plan_snapshots
                    (batch_id, assignment_id, employee_id, tipo, data_inizio, data_fine, project_id, service_id, other_activity_id, descrizione)
                VALUES (@BatchId, @AssignmentId, @EmployeeId, @Tipo, @DataInizio, @DataFine, @ProjectId, @ServiceId, @OtherActivityId, @Descrizione)
                ON DUPLICATE KEY UPDATE
                    employee_id=VALUES(employee_id), tipo=VALUES(tipo), data_inizio=VALUES(data_inizio), data_fine=VALUES(data_fine),
                    project_id=VALUES(project_id), service_id=VALUES(service_id), other_activity_id=VALUES(other_activity_id), descrizione=VALUES(descrizione)",
                new { BatchId = batchId, r.AssignmentId, r.EmployeeId, r.Tipo, r.DataInizio, r.DataFine, r.ProjectId, r.ServiceId, r.OtherActivityId, r.Descrizione });
        }
    }

    // ── Impostazioni digest + registro esecuzioni ────────────────

    public PlanDigestSettingsDto GetSettings()
    {
        using var c = _rdb.Open();
        var rows = c.Query<(string SettingKey, string SettingValue)>(
            "SELECT `key` AS SettingKey, `value` AS SettingValue FROM res_settings WHERE `key` LIKE 'digest_%'")
            .ToDictionary(r => r.SettingKey, r => r.SettingValue);
        string Get(string k, string fallback) => rows.TryGetValue(k, out string? v) ? v : fallback;

        return new PlanDigestSettingsDto
        {
            DigestEnabled = Get("digest_enabled", "0") == "1",
            DigestTime = Get("digest_time", "07:00"),
            DigestWeekends = Get("digest_weekends", "1") == "1",
            DigestLastRun = Get("digest_last_run", ""),
        };
    }

    public void SaveSettings(PlanDigestSettingsDto dto)
    {
        using var c = _rdb.Open();
        void Set(string key, string value) => c.Execute(
            "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
            new { K = key, V = value });

        Set("digest_enabled", dto.DigestEnabled ? "1" : "0");
        Set("digest_time", string.IsNullOrWhiteSpace(dto.DigestTime) ? "07:00" : dto.DigestTime.Trim());
        Set("digest_weekends", dto.DigestWeekends ? "1" : "0");
    }

    public DigestStatusDto GetStatus()
    {
        using var c = _rdb.Open();
        PlanDigestSettingsDto settings = GetSettings();

        int attivita = c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_assignments");
        int conEmail = c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees WHERE status='ACTIVE' AND email IS NOT NULL AND email <> ''");
        int senzaEmail = c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees WHERE status='ACTIVE' AND (email IS NULL OR email = '')");

        List<DigestLogEntry> log = c.Query<DigestLogEntry>(@"
            SELECT run_utc AS RunUtc, trigger_kind AS `Trigger`, email_inviate AS EmailInviate, senza_email AS SenzaEmail, esito AS Esito
            FROM res_digest_log ORDER BY id DESC LIMIT 20").ToList();

        DateTime nowUtc = DateTime.UtcNow;
        return new DigestStatusDto
        {
            ServerTimeUtc = nowUtc,
            ServerTimeLocal = ToItalyTime(nowUtc),
            Settings = settings,
            EmailConfigurata = _email.Enabled,
            AttivitaNelPiano = attivita,
            DipendentiConEmail = conEmail,
            DipendentiSenzaEmail = senzaEmail,
            UltimeEsecuzioni = log,
        };
    }

    private void LogRun(MySqlConnector.MySqlConnection c, string trigger, int emailInviate, int senzaEmail, string esito)
    {
        c.Execute(@"
            INSERT INTO res_digest_log (run_utc, trigger_kind, email_inviate, senza_email, esito)
            VALUES (@Now, @Trigger, @Email, @Senza, @Esito)",
            new { Now = DateTime.UtcNow, Trigger = trigger, Email = emailInviate, Senza = senzaEmail, Esito = esito });
    }

    public static DateTime ToItalyTime(DateTime utc)
    {
        try
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                return utc.AddHours(1);
            }
        }
    }
}
