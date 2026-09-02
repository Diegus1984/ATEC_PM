using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.RisorseSync;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Risorse;

/// <summary>
/// Il badge «modifiche da notificare» con la sincronizzazione col VPS accesa. A notificare i
/// dipendenti è il VPS (PIANO-SYNC-RISORSE.md §7, decisione 6): la foto del piano di PM
/// (<c>res_plan_snapshots</c>) deve restare uguale al piano, così il confronto di
/// <see cref="PlanNotificationService.ComputePending"/> dà zero e il digest di PM, se un giorno
/// la sincronizzazione si spegne, riparte pulito. In produzione la foto era rimasta a 7 righe di
/// prova mentre il motore ne aveva scritte 182: badge a «189 modifiche da notificare».
/// <para>Le cose da difendere: <see cref="PlanNotificationService.AllineaFotoAlPiano"/> da sola
/// (foto vecchia parziale → uguale al piano, <c>res_notify_pending</c> vuota, nessun batch nuovo)
/// e il motore da cima a fondo con <see cref="VpsFinto"/>: dopo un giro che scrive in PM il badge
/// è a zero, un giro senza scritture non tocca la foto.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class NotificheConSyncTests
{
    private readonly SchemaCondiviso _schema;
    private int _rossi, _verdi;
    private const int RossiVps = 10, VerdiVps = 11;
    private static readonly DateOnly Inizio = new(2026, 9, 7);
    private static readonly DateOnly Fine = new(2026, 9, 11);

    public NotificheConSyncTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
        using MySqlConnection c = _schema.Apri();
        c.Execute("DELETE FROM res_settings WHERE `key` LIKE 'sync.%'");
        c.Execute("DELETE FROM res_sync_map");
        c.Execute("DELETE FROM res_sync_log");
        c.Execute("DELETE FROM res_notify_pending");
        c.Execute("DELETE FROM res_plan_snapshot_batches");
        _rossi = Dipendente(c, "Mario", "Rossi", "m.rossi");
        _verdi = Dipendente(c, "Anna", "Verdi", "a.verdi");
        // La mappa dipendenti come la lascia la Fase 1.
        RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, _rossi, RossiVps, null);
        RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, _verdi, VerdiVps, null);
    }

    // ── Dati di prova ────────────────────────────────────────────

    private static int Dipendente(MySqlConnection c, string nome, string cognome, string username) =>
        Inserisci(c, @"INSERT INTO employees (first_name, last_name, email, emp_type, status, user_role, username, password_hash)
                       VALUES (@N, @C, @E, 'INTERNAL', 'ACTIVE', 'TECH', @U, '')",
            new { N = nome, C = cognome, E = $"{nome}.{cognome}@atec.it".ToLowerInvariant(), U = username });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    /// <summary>Una riga in PM come la scrive il controller.</summary>
    private int RigaPm(int employeeId, string tipo = "OP", string? descrizione = "Manutenzione")
    {
        using MySqlConnection c = _schema.Apri();
        return Inserisci(c, @"
            INSERT INTO res_assignments (employee_id, tipo, data_inizio, data_fine, descrizione, updated_by, updated_at)
            VALUES (@E, @T, @I, @F, @D, @E, NOW())",
            new { E = employeeId, T = tipo, I = Inizio.ToDateTime(TimeOnly.MinValue), F = Fine.ToDateTime(TimeOnly.MinValue), D = descrizione });
    }

    /// <summary>Una riga nella foto del batch dato, con il contenuto che si vuole (anche diverso dal piano).</summary>
    private static void RigaFoto(MySqlConnection c, int batchId, int assignmentId, int employeeId, string descrizione) =>
        c.Execute(@"
            INSERT INTO res_plan_snapshots (batch_id, assignment_id, employee_id, tipo, data_inizio, data_fine, descrizione)
            VALUES (@B, @A, @E, 'OP', @I, @F, @D)",
            new { B = batchId, A = assignmentId, E = employeeId, I = Inizio.ToDateTime(TimeOnly.MinValue), F = Fine.ToDateTime(TimeOnly.MinValue), D = descrizione });

    private PlanNotificationService Notifiche()
    {
        var rdb = new ResourcesDbService(_schema.Servizio());
        var email = new EmailService(rdb, new ConfigurationBuilder().Build(), NullLogger<EmailService>.Instance);
        return new PlanNotificationService(rdb, email, NullLogger<PlanNotificationService>.Instance);
    }

    private RisorseSyncService Servizio(VpsFinto vps, PlanNotificationService notify)
    {
        var svc = new RisorseSyncService(
            new ResourcesDbService(_schema.Servizio()),
            new ConfigurationBuilder().Build(),
            NullLogger<RisorseSyncService>.Instance,
            new HttpClient(vps),
            notify: notify);
        svc.SaveSettings(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "segreta" });
        return svc;
    }

    private static VpsFinto Vps()
    {
        var vps = new VpsFinto();
        vps.Dipendenti.Add(new SyncEmployeeDto { Id = RossiVps, FirstName = "Mario", LastName = "Rossi", Username = "m.rossi" });
        vps.Dipendenti.Add(new SyncEmployeeDto { Id = VerdiVps, FirstName = "Anna", LastName = "Verdi", Username = "a.verdi" });
        return vps;
    }

    private int Batch()
    {
        using MySqlConnection c = _schema.Apri();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_plan_snapshot_batches");
    }

    private int PendentiDaCancellare()
    {
        using MySqlConnection c = _schema.Apri();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_notify_pending");
    }

    /// <summary>Gli assignment_id nella foto dell'ultimo batch, in ordine.</summary>
    private List<int> IdNellaFoto()
    {
        using MySqlConnection c = _schema.Apri();
        return c.Query<int>(@"
            SELECT assignment_id FROM res_plan_snapshots
            WHERE batch_id = (SELECT MAX(id) FROM res_plan_snapshot_batches) ORDER BY assignment_id").ToList();
    }

    private List<int> IdNelPiano()
    {
        using MySqlConnection c = _schema.Apri();
        return c.Query<int>("SELECT id FROM res_assignments ORDER BY id").ToList();
    }

    // ── AllineaFotoAlPiano da sola ───────────────────────────────

    [FactRichiedeMySql]
    public void Una_foto_vecchia_e_parziale_diventa_uguale_al_piano_e_il_badge_va_a_zero()
    {
        int a = RigaPm(_rossi, descrizione: "Uguale nella foto");
        int b = RigaPm(_verdi, descrizione: "Cambiata dopo la foto");
        int nuova = RigaPm(_rossi, "FLEX", "Nata dopo la foto");
        const int sparita = 999_999;
        using (MySqlConnection c = _schema.Apri())
        {
            int batch = Inserisci(c, "INSERT INTO res_plan_snapshot_batches (created_utc) VALUES (UTC_TIMESTAMP())");
            RigaFoto(c, batch, a, _rossi, "Uguale nella foto");
            RigaFoto(c, batch, b, _verdi, "Com'era nella foto");
            RigaFoto(c, batch, sparita, _verdi, "Cancellata dal VPS");
            c.Execute("INSERT INTO res_notify_pending (assignment_id, made_by, action, orig_employee_id) VALUES (@A, NULL, 'delete', @E)",
                new { A = sparita, E = _verdi });
        }
        PlanNotificationService notifiche = Notifiche();
        Assert.Equal(3, notifiche.ComputePending().TotalChanges);   // 1 nuova + 1 modificata + 1 cancellata

        int righe = notifiche.AllineaFotoAlPiano();

        Assert.Equal(3, righe);
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
        Assert.Equal(new[] { a, b, nuova }, IdNellaFoto());
        Assert.Equal(IdNelPiano(), IdNellaFoto());
        Assert.Equal(0, PendentiDaCancellare());
        Assert.Equal(1, Batch());                                     // aggiornato il batch che c'era, non uno nuovo

        // Il piano cambia ancora: una via, una nuova → il badge le vede, l'allineamento le assorbe.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("DELETE FROM res_assignments WHERE id = @Id", new { Id = nuova });
        int altra = RigaPm(_verdi, "FLEX", "Supporto");
        Assert.Equal(2, notifiche.ComputePending().TotalChanges);

        Assert.Equal(3, notifiche.AllineaFotoAlPiano());

        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
        Assert.Equal(new[] { a, b, altra }, IdNellaFoto());
        Assert.Equal(1, Batch());
    }

    [FactRichiedeMySql]
    public void Senza_nessuna_foto_ne_scatta_una_e_il_badge_e_a_zero()
    {
        int a = RigaPm(_rossi);
        int b = RigaPm(_verdi, "FERIE", null);
        PlanNotificationService notifiche = Notifiche();

        Assert.Equal(2, notifiche.AllineaFotoAlPiano());

        Assert.Equal(1, Batch());
        Assert.Equal(new[] { a, b }, IdNellaFoto());
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
    }

    [FactRichiedeMySql]
    public void Una_cancellazione_fatta_in_PM_non_ancora_consegnata_al_VPS_resta_pendente_con_l_autore()
    {
        int consegnata = RigaPm(_rossi);
        int inAttesa = RigaPm(_verdi);
        using (MySqlConnection c = _schema.Apri())
        {
            int batch = Inserisci(c, "INSERT INTO res_plan_snapshot_batches (created_utc) VALUES (UTC_TIMESTAMP())");
            RigaFoto(c, batch, consegnata, _rossi, "Manutenzione");
            RigaFoto(c, batch, inAttesa, _verdi, "Manutenzione");
            // Tutte e due sono sul VPS (mappa) e vengono cancellate in PM come fa DeleteAssignment
            // (prima chi cancella in res_notify_pending, poi la DELETE)...
            RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, consegnata, 501, null);
            RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, inAttesa, 502, null);
            c.Execute("INSERT INTO res_notify_pending (assignment_id, made_by, action) VALUES (@Id, @U, 'delete')", new { Id = consegnata, U = _rossi });
            c.Execute("INSERT INTO res_notify_pending (assignment_id, made_by, action) VALUES (@Id, @U, 'delete')", new { Id = inAttesa, U = _verdi });
            c.Execute("DELETE FROM res_assignments WHERE id IN (@A, @B)", new { A = consegnata, B = inAttesa });
            // ...ma solo la prima è già arrivata al VPS (la mappa se ne va con la DELETE di là).
            RisorseSyncMap.Rimuovi(c, RisorseSyncMap.Assignment, consegnata);
        }
        PlanNotificationService notifiche = Notifiche();

        Assert.Equal(0, notifiche.AllineaFotoAlPiano());

        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
        Assert.Empty(IdNellaFoto());
        using MySqlConnection v = _schema.Apri();
        List<(int AssignmentId, int? MadeBy)> rimaste = v.Query<(int, int?)>("SELECT assignment_id, made_by FROM res_notify_pending").ToList();
        Assert.Equal((inAttesa, (int?)_verdi), Assert.Single(rimaste));   // l'autore per il VPS c'è ancora
    }

    [FactRichiedeMySql]
    public void Notifica_subito_due_volte_sulla_stessa_riga_modificata_non_lascia_doppioni_nella_foto()
    {
        int a = RigaPm(_rossi, descrizione: "Prima");
        PlanNotificationService notifiche = Notifiche();
        Assert.Equal(1, notifiche.AllineaFotoAlPiano());                  // la prima foto
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Seconda' WHERE id = @Id", new { Id = a });
        Assert.Equal(1, notifiche.ComputePending().TotalChanges);

        notifiche.SendSelected(new List<int> { a }, "manuale");
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);          // prima lanciava: due righe con lo stesso assignment_id

        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Terza' WHERE id = @Id", new { Id = a });
        notifiche.SendSelected(new List<int> { a }, "manuale");

        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
        Assert.Equal(new[] { a }, IdNellaFoto());
        Assert.Equal(1, Batch());
    }

    // ── Il motore da cima a fondo ────────────────────────────────

    [FactRichiedeMySql]
    public void Accesa_ma_senza_credenziali_la_sincronizzazione_non_e_attiva()
    {
        var svc = new RisorseSyncService(
            new ResourcesDbService(_schema.Servizio()),
            new ConfigurationBuilder().Build(),
            NullLogger<RisorseSyncService>.Instance,
            new HttpClient(new VpsFinto()));

        svc.SaveSettings(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "" });
        Assert.False(svc.IsAttiva);                                        // motore a riposo: il badge di PM deve parlare

        svc.SaveSettings(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "segreta" });
        Assert.True(svc.IsAttiva);

        svc.SaveSettings(new RisorseSyncSettingsDto { Enabled = false, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "" });
        Assert.False(svc.IsAttiva);
    }

    [FactRichiedeMySql]
    public async Task Una_DELETE_in_PM_durante_un_giro_arriva_al_VPS_con_l_autore_anche_se_la_foto_si_allinea_nel_frattempo()
    {
        VpsFinto vps = Vps();
        SyncAssignmentDto op = vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Manutenzione Minebea");
        PlanNotificationService notifiche = Notifiche();
        RisorseSyncService svc = Servizio(vps, notifiche);
        await svc.RunNowAsync("manuale");
        int idPm = Assert.Single(IdNelPiano());
        // Come DeleteAssignment del controller: prima chi cancella in res_notify_pending, poi la DELETE.
        using (MySqlConnection c = _schema.Apri())
        {
            c.Execute("INSERT INTO res_notify_pending (assignment_id, made_by, action) VALUES (@Id, @U, 'delete')", new { Id = idPm, U = _verdi });
            c.Execute("DELETE FROM res_assignments WHERE id = @Id", new { Id = idPm });
        }
        // Il giro che era in corso finisce e allinea la foto: la cancellazione non è ancora partita
        // per il VPS (mappa ancora lì), quindi il suo autore deve restare.
        Assert.Equal(0, notifiche.AllineaFotoAlPiano());
        Assert.Equal(1, PendentiDaCancellare());
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("VPS +0 / ~0 / −1", voce.Dettaglio);
        SyncDeleteRequest richiesta = vps.Corpo<SyncDeleteRequest>("/api/sync/assignments/delete");
        Assert.Equal(new[] { op.Id }, richiesta.Ids);
        Assert.Equal(VerdiVps, richiesta.MadeBy);                          // l'autore, tradotto
        Assert.Empty(vps.Allocazioni);
        Assert.Equal(0, PendentiDaCancellare());                           // consegnata: adesso si consuma
        Assert.Empty(IdNellaFoto());
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
    }

    [FactRichiedeMySql]
    public async Task Dopo_un_giro_che_scrive_in_PM_il_badge_e_a_zero_e_un_giro_senza_scritture_non_tocca_la_foto()
    {
        VpsFinto vps = Vps();
        vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Manutenzione Minebea");
        vps.Allocazione(VerdiVps, "FERIE", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 21));
        PlanNotificationService notifiche = Notifiche();
        RisorseSyncService svc = Servizio(vps, notifiche);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +2", voce.Dettaglio);
        Assert.Equal(2, IdNelPiano().Count);
        Assert.Equal(1, Batch());
        Assert.Equal(IdNelPiano(), IdNellaFoto());
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);

        // Si sporca la foto a mano: un giro che non scrive niente NON la deve toccare.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("DELETE FROM res_plan_snapshots WHERE assignment_id = @Id", new { Id = IdNelPiano()[0] });
        Assert.Equal(1, notifiche.ComputePending().TotalChanges);

        voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −0, VPS +0 / ~0 / −0", voce.Dettaglio);
        Assert.Equal(1, Batch());
        Assert.Equal(1, notifiche.ComputePending().TotalChanges);   // la foto è rimasta com'era

        // Una riga nuova sul VPS: il giro scrive in PM e la foto torna uguale al piano.
        vps.Allocazione(VerdiVps, "FLEX", Inizio, Fine, "Supporto");

        voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +1", voce.Dettaglio);
        Assert.Equal(3, IdNelPiano().Count);
        Assert.Equal(1, Batch());
        Assert.Equal(IdNelPiano(), IdNellaFoto());
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
    }

    [FactRichiedeMySql]
    public async Task Una_riga_sparita_dal_VPS_esce_dalla_foto_e_svuota_le_cancellazioni_pendenti()
    {
        VpsFinto vps = Vps();
        SyncAssignmentDto op = vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Manutenzione Minebea");
        vps.Allocazione(VerdiVps, "FERIE", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 21));
        PlanNotificationService notifiche = Notifiche();
        RisorseSyncService svc = Servizio(vps, notifiche);
        await svc.RunNowAsync("manuale");
        Assert.Equal(2, IdNellaFoto().Count);
        vps.Allocazioni.Remove(op);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −1", voce.Dettaglio);
        Assert.Single(IdNelPiano());
        Assert.Equal(IdNelPiano(), IdNellaFoto());
        Assert.Equal(0, PendentiDaCancellare());                    // la cancellazione è già del VPS: niente da notificare
        Assert.Equal(0, notifiche.ComputePending().TotalChanges);
    }
}
