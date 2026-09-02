using System.Globalization;
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
/// La logica pura della Fase 2 (<see cref="AllocazioniSync"/>): normalizzazione e impronta,
/// la tabella di merge di PIANO-SYNC-RISORSE.md §4.3 riga per riga, le conversioni con le
/// mappe, i fusi orari (PM ora locale, VPS UTC) e l'abbinamento per contenuto. Niente database.
/// </summary>
public class AllocazioniRegoleTests
{
    private static readonly DateOnly I = new(2026, 9, 3);
    private static readonly DateOnly F = new(2026, 9, 5);

    private static RigaAlloc Riga(string tipo = "OP", int? project = 7, string? descrizione = "Manutenzione",
        int? updatedBy = 1, DateTime? updatedAt = null, int employee = 12, DateOnly? fine = null) =>
        new(employee, tipo, I, fine ?? F, project, descrizione, updatedBy, updatedAt);

    private static DateTime Utc(int giorno, int ora) => new(2026, 9, giorno, ora, 0, 0, DateTimeKind.Utc);

    // ── Impronta e normalizzazione ───────────────────────────────

    [Fact]
    public void L_impronta_ignora_autore_e_ora_e_sente_i_campi_del_piano()
    {
        string base_ = AllocazioniSync.Impronta(Riga());
        Assert.Equal(64, base_.Length);
        Assert.Equal(base_, AllocazioniSync.Impronta(Riga(updatedBy: 99, updatedAt: Utc(1, 10))));   // chi e quando non contano
        Assert.NotEqual(base_, AllocazioniSync.Impronta(Riga(fine: new DateOnly(2026, 9, 6))));
        Assert.NotEqual(base_, AllocazioniSync.Impronta(Riga(project: 8)));
        Assert.NotEqual(base_, AllocazioniSync.Impronta(Riga(descrizione: "Altro")));
        Assert.NotEqual(base_, AllocazioniSync.Impronta(Riga(tipo: "FLEX")));
        Assert.NotEqual(base_, AllocazioniSync.Impronta(Riga(employee: 13)));
    }

    [Fact]
    public void Il_tipo_si_normalizza_e_le_ferie_azzerano_la_commessa()
    {
        Assert.Equal(AllocazioniSync.Impronta(Riga(tipo: "OP")), AllocazioniSync.Impronta(Riga(tipo: " op ")));
        Assert.Equal("FLEX", Riga(tipo: "flex").Tipo);
        Assert.Equal("OP", Riga(tipo: "boh").Tipo);            // fuori da OP/FLEX/FERIE → OP, come il controller
        Assert.Equal("OP", Riga(tipo: null!).Tipo);

        RigaAlloc ferie = Riga(tipo: "ferie", project: 7);
        Assert.Equal("FERIE", ferie.Tipo);
        Assert.Null(ferie.ProjectId);
        Assert.Equal(AllocazioniSync.Impronta(ferie), AllocazioniSync.Impronta(Riga(tipo: "FERIE", project: null)));
    }

    [Fact]
    public void La_descrizione_vuota_e_null_sono_la_stessa_cosa()
    {
        Assert.Null(Riga(descrizione: "").Descrizione);
        Assert.Null(Riga(descrizione: "   ").Descrizione);
        Assert.Equal("Manutenzione", Riga(descrizione: "  Manutenzione ").Descrizione);
        Assert.Equal(AllocazioniSync.Impronta(Riga(descrizione: null)), AllocazioniSync.Impronta(Riga(descrizione: "  ")));
        Assert.Equal(AllocazioniSync.Impronta(Riga()), AllocazioniSync.Impronta(Riga(descrizione: " Manutenzione\t")));
    }

    [Fact]
    public void La_descrizione_oltre_500_caratteri_si_taglia_prima_dell_impronta()
    {
        // Sul VPS è TEXT senza limite, in PM VARCHAR(500): il testo lungo e quello che ci sta hanno la stessa impronta.
        string lunga = new string('x', 600);
        RigaAlloc r = Riga(descrizione: lunga);
        Assert.Equal(AllocazioniSync.LunghezzaDescrizione, r.Descrizione!.Length);
        Assert.Equal(AllocazioniSync.Impronta(Riga(descrizione: lunga[..500])), AllocazioniSync.Impronta(r));
        Assert.Equal(AllocazioniSync.Impronta(r), AllocazioniSync.Impronta(Riga(descrizione: lunga + " coda diversa")));
        Assert.Equal(500, Riga(descrizione: new string('y', 500)).Descrizione!.Length);   // esattamente al limite: intatta
        Assert.Null(AllocazioniSync.NormalizzaDescrizione(new string(' ', 600)));
    }

    [Fact]
    public void L_impronta_non_dipende_dalla_cultura_della_macchina()
    {
        string invariante = AllocazioniSync.Impronta(Riga());
        CultureInfo prima = CultureInfo.CurrentCulture;
        try
        {
            // Calendario buddista: «yyyy» darebbe 2569 con la cultura corrente — l'impronta persistita non deve muoversi.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            Assert.Equal(invariante, AllocazioniSync.Impronta(Riga()));
            Assert.Equal("03/09-05/09", AllocazioniSync.Periodo(Riga()));
        }
        finally
        {
            CultureInfo.CurrentCulture = prima;
        }
    }

    // ── Merge (§4.3) ─────────────────────────────────────────────

    [Fact]
    public void Uguali_niente_o_solo_la_mappa()
    {
        RigaAlloc pm = Riga(updatedAt: Utc(1, 10));
        RigaAlloc vps = Riga(updatedBy: 5, updatedAt: Utc(2, 10));
        string impronta = AllocazioniSync.Impronta(pm);

        Assert.Equal((AzioneMerge.Niente, false), AllocazioniSync.Decidi(pm, vps, impronta));
        Assert.Equal((AzioneMerge.AggiornaHash, false), AllocazioniSync.Decidi(pm, vps, "vecchia"));
        Assert.Equal((AzioneMerge.AggiornaHash, false), AllocazioniSync.Decidi(pm, vps, null));
    }

    [Fact]
    public void Cambiata_da_una_parte_sola_si_copia_dall_altra()
    {
        RigaAlloc originale = Riga();
        string sincronizzata = AllocazioniSync.Impronta(originale);
        RigaAlloc cambiata = Riga(descrizione: "Spostata");

        Assert.Equal((AzioneMerge.AggiornaVps, false), AllocazioniSync.Decidi(cambiata, originale, sincronizzata));
        Assert.Equal((AzioneMerge.AggiornaPm, false), AllocazioniSync.Decidi(originale, cambiata, sincronizzata));
    }

    [Fact]
    public void Cambiate_entrambe_vince_l_ultima_modifica_in_UTC_e_si_conta_il_conflitto()
    {
        string sincronizzata = AllocazioniSync.Impronta(Riga());
        RigaAlloc pmNuova = Riga(descrizione: "Da PM", updatedAt: Utc(3, 12));
        RigaAlloc vpsNuova = Riga(descrizione: "Dal VPS", updatedAt: Utc(3, 11));

        Assert.Equal((AzioneMerge.AggiornaVps, true), AllocazioniSync.Decidi(pmNuova, vpsNuova, sincronizzata));
        Assert.Equal((AzioneMerge.AggiornaPm, true), AllocazioniSync.Decidi(Riga(descrizione: "Da PM", updatedAt: Utc(3, 10)), vpsNuova, sincronizzata));
        // A parità d'istante vince il VPS.
        Assert.Equal((AzioneMerge.AggiornaPm, true), AllocazioniSync.Decidi(Riga(descrizione: "Da PM", updatedAt: Utc(3, 11)), vpsNuova, sincronizzata));
        // Chi non ha l'ora perde.
        Assert.Equal((AzioneMerge.AggiornaPm, true), AllocazioniSync.Decidi(Riga(descrizione: "Da PM", updatedAt: null), vpsNuova, sincronizzata));
        Assert.Equal((AzioneMerge.AggiornaVps, true), AllocazioniSync.Decidi(pmNuova, Riga(descrizione: "Dal VPS", updatedAt: null), sincronizzata));
        // Nessuno dei due ce l'ha: vince il VPS.
        Assert.Equal((AzioneMerge.AggiornaPm, true), AllocazioniSync.Decidi(Riga(descrizione: "Da PM", updatedAt: null), Riga(descrizione: "Dal VPS", updatedAt: null), sincronizzata));
    }

    [Fact]
    public void La_cancellazione_vince_sempre_e_se_l_altro_lato_e_cambiato_e_un_conflitto()
    {
        RigaAlloc riga = Riga();
        string sincronizzata = AllocazioniSync.Impronta(riga);

        Assert.Equal((AzioneMerge.CancellaVps, false), AllocazioniSync.Decidi(null, riga, sincronizzata));
        Assert.Equal((AzioneMerge.CancellaPm, false), AllocazioniSync.Decidi(riga, null, sincronizzata));
        // L'altro lato nel frattempo è cambiato: si cancella lo stesso, ma è un conflitto (va nel registro).
        Assert.Equal((AzioneMerge.CancellaVps, true), AllocazioniSync.Decidi(null, Riga(descrizione: "Cambiata", updatedAt: Utc(3, 12)), sincronizzata));
        Assert.Equal((AzioneMerge.CancellaPm, true), AllocazioniSync.Decidi(Riga(descrizione: "Cambiata", updatedAt: Utc(3, 12)), null, sincronizzata));
        // Sparite entrambe: resta solo la mappa da togliere.
        Assert.Equal((AzioneMerge.AggiornaHash, false), AllocazioniSync.Decidi(null, null, sincronizzata));
    }

    // ── Conversioni ──────────────────────────────────────────────

    [Fact]
    public void Dal_VPS_dipendente_o_commessa_non_mappati_fermano_la_riga_l_autore_no()
    {
        var dipVpsPm = new Dictionary<int, int> { [36] = 38, [37] = 37 };
        var comVpsPm = new Dictionary<int, int> { [100] = 5 };
        var dto = new SyncAssignmentDto
        {
            Id = 1, EmployeeId = 36, Tipo = "OP", DataInizio = I, DataFine = F, ProjectId = 100, Descrizione = " OSVA ",
            UpdatedBy = 37, UpdatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        };

        RigaAlloc? r = AllocazioniSync.DaVps(dto, dipVpsPm, comVpsPm);
        Assert.NotNull(r);
        Assert.Equal(38, r!.EmployeeId);
        Assert.Equal(5, r.ProjectId);
        Assert.Equal(37, r.UpdatedBy);
        Assert.Equal("OSVA", r.Descrizione);
        Assert.Equal(DateTimeKind.Utc, r.UpdatedAtUtc!.Value.Kind);

        // Autore sconosciuto → null, la riga passa.
        dto.UpdatedBy = 999;
        RigaAlloc? r2 = AllocazioniSync.DaVps(dto, dipVpsPm, comVpsPm);
        Assert.NotNull(r2);
        Assert.Equal(5, r2!.ProjectId);
        Assert.Null(r2.UpdatedBy);

        // Commessa sconosciuta → null: la riga si salta (mai azzerata: al giro dopo la copia «senza
        // commessa» tornerebbe sul VPS, che perderebbe il legame — lo speculare di VersoVps).
        dto.ProjectId = 999;
        Assert.Null(AllocazioniSync.DaVps(dto, dipVpsPm, comVpsPm));
        // Una FERIE su una commessa sconosciuta passa: la forma comune le toglie comunque la commessa.
        dto.Tipo = "FERIE";
        RigaAlloc? ferie = AllocazioniSync.DaVps(dto, dipVpsPm, comVpsPm);
        Assert.NotNull(ferie);
        Assert.Null(ferie!.ProjectId);

        // Dipendente sconosciuto → null: la riga si salta.
        dto.Tipo = "OP";
        dto.ProjectId = 100;
        dto.EmployeeId = 38;
        Assert.Null(AllocazioniSync.DaVps(dto, dipVpsPm, comVpsPm));
    }

    [Fact]
    public void Verso_il_VPS_gli_id_si_traducono_e_l_ora_resta_UTC()
    {
        var mappaDip = new Dictionary<int, RisorseSyncMap.Voce> { [38] = new(36, null), [37] = new(37, null) };
        var mappaCom = new Dictionary<int, RisorseSyncMap.Voce> { [5] = new(100, null) };
        RigaAlloc r = new(38, "FLEX", I, F, 5, "OSVA", 37, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        SyncAssignmentUpsertDto dto = AllocazioniSync.VersoVps(r, 182, mappaDip, mappaCom);
        Assert.Equal(182, dto.Id);
        Assert.Equal(36, dto.EmployeeId);
        Assert.Equal(100, dto.ProjectId);
        Assert.Equal(37, dto.UpdatedBy);
        Assert.Equal("FLEX", dto.Tipo);
        Assert.Equal(I, dto.DataInizio);
        Assert.Equal(F, dto.DataFine);
        Assert.Equal(DateTimeKind.Utc, dto.UpdatedAt!.Value.Kind);
        Assert.Null(dto.ServiceId);

        // Autore non mappato → null; dipendente o commessa non mappati → errore (non deve succedere:
        // il motore li salta prima; mandare la riga SENZA commessa farebbe perdere il legame in PM al giro dopo).
        SyncAssignmentUpsertDto nuova = AllocazioniSync.VersoVps(new RigaAlloc(38, "OP", I, F, null, null, 99, null), null, mappaDip, mappaCom);
        Assert.Null(nuova.Id);
        Assert.Null(nuova.ProjectId);
        Assert.Null(nuova.UpdatedBy);
        Assert.Null(nuova.UpdatedAt);
        Assert.Throws<InvalidOperationException>(() => AllocazioniSync.VersoVps(new RigaAlloc(1, "OP", I, F, null, null, null, null), null, mappaDip, mappaCom));
        Assert.Throws<InvalidOperationException>(() => AllocazioniSync.VersoVps(new RigaAlloc(38, "OP", I, F, 6, null, null, null), null, mappaDip, mappaCom));
        // Una FERIE su una commessa non mappata passa: la forma comune le ha già tolto la commessa.
        Assert.Null(AllocazioniSync.VersoVps(new RigaAlloc(38, "FERIE", I, F, 6, null, null, null), null, mappaDip, mappaCom).ProjectId);
    }

    [Fact]
    public void Da_MySQL_l_ora_locale_diventa_UTC()
    {
        var a = new AllocazionePm
        {
            Id = 1, EmployeeId = 3, Tipo = "op", DataInizio = new DateTime(2026, 9, 3), DataFine = new DateTime(2026, 9, 5),
            Descrizione = "", UpdatedBy = 1, UpdatedAt = new DateTime(2026, 7, 15, 12, 0, 0),
        };
        RigaAlloc r = AllocazioniSync.DaPm(a);
        Assert.Equal("OP", r.Tipo);
        Assert.Equal(I, r.Inizio);
        Assert.Equal(F, r.Fine);
        Assert.Null(r.Descrizione);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc), r.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, r.UpdatedAtUtc!.Value.Kind);
        a.UpdatedAt = null;
        Assert.Null(AllocazioniSync.DaPm(a).UpdatedAtUtc);
    }

    [Fact]
    public void Fusi_mezzogiorno_d_estate_sono_le_10_UTC_e_ritorno_d_inverno_le_11()
    {
        DateTime estate = AllocazioniSync.UtcDaLocale(new DateTime(2026, 7, 15, 12, 0, 0));
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0), estate);
        Assert.Equal(DateTimeKind.Utc, estate.Kind);

        DateTime ritorno = AllocazioniSync.LocaleDaUtc(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0), ritorno);
        Assert.NotEqual(DateTimeKind.Utc, ritorno.Kind);

        Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), AllocazioniSync.UtcDaLocale(new DateTime(2026, 1, 15, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), AllocazioniSync.LocaleDaUtc(new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc)));
        // Anche un DateTime che si dichiara Utc o Local si tratta come ora italiana da orologio.
        Assert.Equal(estate, AllocazioniSync.UtcDaLocale(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    // ── Abbinamento per contenuto ────────────────────────────────

    [Fact]
    public void L_abbinamento_per_contenuto_e_uno_a_uno_in_ordine_di_id()
    {
        RigaAlloc a = Riga(descrizione: "A");
        RigaAlloc b = Riga(descrizione: "B");
        var pm = new List<(int Id, RigaAlloc Riga)> { (30, a), (10, a), (20, b), (40, Riga(descrizione: "solo PM")) };
        var vps = new List<(int Id, RigaAlloc Riga)> { (502, b), (501, Riga(descrizione: "A", updatedBy: 9)), (503, Riga(descrizione: "solo VPS")) };

        List<(int IdPm, int IdVps, string Impronta)> coppie = AllocazioniSync.AbbinaPerContenuto(pm, vps);

        // Due «A» in PM e una sola sul VPS: si abbina la prima per id (10), la 30 resta da creare.
        Assert.Equal(2, coppie.Count);
        Assert.Contains(coppie, c => c.IdPm == 10 && c.IdVps == 501 && c.Impronta == AllocazioniSync.Impronta(a));
        Assert.Contains(coppie, c => c.IdPm == 20 && c.IdVps == 502 && c.Impronta == AllocazioniSync.Impronta(b));
        Assert.DoesNotContain(coppie, c => c.IdPm == 30 || c.IdPm == 40 || c.IdVps == 503);
        Assert.Empty(AllocazioniSync.AbbinaPerContenuto(pm, new List<(int, RigaAlloc)>()));
    }
}

/// <summary>
/// Il giro delle allocazioni da cima a fondo (Fase 2): MySQL di prova, <see cref="VpsFinto"/>
/// che tiene le sue allocazioni in memoria. Le mappe EMPLOYEE e PROJECT le lascia la Fase 1
/// (qui seminate a mano per i dipendenti, dal giro per la commessa). Le cose da difendere:
/// gli id tradotti nei due versi, <c>updated_at</c> locale in PM e UTC sul VPS, mappa e
/// impronte scritte, la cancellazione con l'autore, le righe di un dipendente o di una commessa
/// non mappati (da entrambi i lati) mai toccate, niente doppioni se la stessa riga è già su
/// entrambi i lati, e un secondo giro identico che non scrive niente.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class GiroAllocazioniTests
{
    private readonly SchemaCondiviso _schema;
    private int _rossi, _verdi, _monticone, _commessa;
    private const int RossiVps = 10, VerdiVps = 11;
    private static readonly DateOnly Inizio = new(2026, 9, 7);
    private static readonly DateOnly Fine = new(2026, 9, 11);
    private static readonly DateTime Luglio1Utc = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);

    public GiroAllocazioniTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
        using MySqlConnection c = _schema.Apri();
        c.Execute("DELETE FROM res_settings WHERE `key` LIKE 'sync.%'");
        c.Execute("DELETE FROM res_sync_map");
        c.Execute("DELETE FROM res_sync_log");
        c.Execute("DELETE FROM res_notify_pending");
        SeminaPm(c);
    }

    // ── Dati di prova ────────────────────────────────────────────

    private void SeminaPm(MySqlConnection c)
    {
        _rossi = Dipendente(c, "Mario", "Rossi", "INTERNAL", "m.rossi");
        _verdi = Dipendente(c, "Anna", "Verdi", "INTERNAL", "a.verdi");
        _monticone = Dipendente(c, "Christian", "Monticone", "EXTERNAL", null);
        // La mappa dipendenti come la lascia la Fase 1 (Monticone fuori: esterno, non abbinato).
        RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, _rossi, RossiVps, null);
        RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, _verdi, VerdiVps, null);

        int cliente = c.ExecuteScalar<int?>("SELECT id FROM customers ORDER BY id LIMIT 1")
            ?? Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = 'admin'");
        _commessa = Inserisci(c,
            "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20260901.001', 'OSVA upgrade', @C, @P, 'ACTIVE')",
            new { C = cliente, P = pm });
    }

    private static int Dipendente(MySqlConnection c, string nome, string cognome, string tipo, string? username) =>
        Inserisci(c, @"INSERT INTO employees (first_name, last_name, email, emp_type, status, user_role, username, password_hash)
                       VALUES (@N, @C, @E, @T, 'ACTIVE', 'TECH', @U, '')",
            new { N = nome, C = cognome, E = $"{nome}.{cognome}@atec.it".ToLowerInvariant(), T = tipo, U = username });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    /// <summary>Una riga in PM come la scrive il controller (updated_at = NOW()).</summary>
    private int RigaPm(int employeeId, string tipo = "OP", string? descrizione = "Manutenzione", int? projectId = null)
    {
        using MySqlConnection c = _schema.Apri();
        return Inserisci(c, @"
            INSERT INTO res_assignments (employee_id, tipo, data_inizio, data_fine, project_id, descrizione, updated_by, updated_at)
            VALUES (@E, @T, @I, @F, @P, @D, @E, NOW())",
            new { E = employeeId, T = tipo, I = Inizio.ToDateTime(TimeOnly.MinValue), F = Fine.ToDateTime(TimeOnly.MinValue), P = projectId, D = descrizione });
    }

    private static VpsFinto Vps()
    {
        var vps = new VpsFinto();
        vps.Dipendenti.Add(new SyncEmployeeDto { Id = RossiVps, FirstName = "Mario", LastName = "Rossi", Username = "m.rossi" });
        vps.Dipendenti.Add(new SyncEmployeeDto { Id = VerdiVps, FirstName = "Anna", LastName = "Verdi", Username = "a.verdi" });
        return vps;
    }

    /// <summary>Il VPS di partenza: due allocazioni vere, una con l'ora e l'autore, una FERIE senza.</summary>
    private static (VpsFinto Vps, SyncAssignmentDto Op, SyncAssignmentDto Ferie) VpsConDueRighe()
    {
        VpsFinto vps = Vps();
        SyncAssignmentDto op = vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Manutenzione Minebea", updatedBy: VerdiVps, updatedAtUtc: Luglio1Utc);
        SyncAssignmentDto ferie = vps.Allocazione(VerdiVps, "FERIE", new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 21));
        return (vps, op, ferie);
    }

    private RisorseSyncService Servizio(VpsFinto vps)
    {
        var svc = new RisorseSyncService(
            new ResourcesDbService(_schema.Servizio()),
            new ConfigurationBuilder().Build(),
            NullLogger<RisorseSyncService>.Instance,
            new HttpClient(vps));
        svc.SaveSettings(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "segreta" });
        return svc;
    }

    private sealed class RigaLetta
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string Tipo { get; set; } = "";
        public DateTime DataInizio { get; set; }
        public DateTime DataFine { get; set; }
        public int? ProjectId { get; set; }
        public string? Descrizione { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private List<RigaLetta> RighePm()
    {
        using MySqlConnection c = _schema.Apri();
        return c.Query<RigaLetta>(@"
            SELECT id AS Id, employee_id AS EmployeeId, tipo AS Tipo, data_inizio AS DataInizio, data_fine AS DataFine,
                   project_id AS ProjectId, descrizione AS Descrizione, updated_by AS UpdatedBy, updated_at AS UpdatedAt
            FROM res_assignments ORDER BY id").ToList();
    }

    private Dictionary<int, RisorseSyncMap.Voce> Mappa()
    {
        using MySqlConnection c = _schema.Apri();
        return RisorseSyncMap.Carica(c, RisorseSyncMap.Assignment);
    }

    private (int CreatePm, int CreateVps, int AggiornatePm, int AggiornateVps, int CancellatePm, int CancellateVps, int Conflitti, int Saltate) UltimoRegistro()
    {
        using MySqlConnection c = _schema.Apri();
        return c.QuerySingle<(int, int, int, int, int, int, int, int)>(@"
            SELECT create_pm, create_vps, aggiornate_pm, aggiornate_vps, cancellate_pm, cancellate_vps, conflitti, saltate
            FROM res_sync_log ORDER BY id DESC LIMIT 1");
    }

    private int RigheRegistro()
    {
        using MySqlConnection c = _schema.Apri();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_sync_log");
    }

    // ── I test ───────────────────────────────────────────────────

    [FactRichiedeMySql]
    public async Task Primo_giro_le_righe_del_VPS_entrano_in_PM_con_id_tradotti_e_ora_locale()
    {
        (VpsFinto vps, SyncAssignmentDto op, SyncAssignmentDto ferie) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("Allocazioni: PM +2 / ~0 / −0, VPS +0 / ~0 / −0", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);

        List<RigaLetta> righe = RighePm();
        Assert.Equal(2, righe.Count);
        RigaLetta opPm = Assert.Single(righe, r => r.Tipo == "OP");
        Assert.Equal(_rossi, opPm.EmployeeId);                       // VPS 10 → PM Rossi
        Assert.Equal(_verdi, opPm.UpdatedBy);                        // l'autore, tradotto
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0), opPm.UpdatedAt);   // 10:00Z → 12:00 ora italiana d'estate
        Assert.Equal(new DateTime(2026, 9, 7), opPm.DataInizio);
        Assert.Equal(new DateTime(2026, 9, 11), opPm.DataFine);
        Assert.Equal("Manutenzione Minebea", opPm.Descrizione);
        RigaLetta feriePm = Assert.Single(righe, r => r.Tipo == "FERIE");
        Assert.Equal(_verdi, feriePm.EmployeeId);
        Assert.Null(feriePm.UpdatedAt);
        Assert.Null(feriePm.UpdatedBy);

        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa();
        Assert.Equal(2, mappa.Count);
        Assert.Equal(op.Id, mappa[opPm.Id].RemoteId);
        Assert.Equal(ferie.Id, mappa[feriePm.Id].RemoteId);
        Assert.Equal(AllocazioniSync.Impronta(new RigaAlloc(_rossi, "OP", Inizio, Fine, null, "Manutenzione Minebea", null, null)), mappa[opPm.Id].SyncedHash);

        // Le colonne del registro (create_vps ecc. sommano anche le anagrafiche: qui si guarda il lato PM).
        (int createPm, _, int aggiornatePm, _, int cancellatePm, _, int conflitti, int saltate) = UltimoRegistro();
        Assert.Equal((2, 0, 0, 0, 0), (createPm, aggiornatePm, cancellatePm, conflitti, saltate));
    }

    [FactRichiedeMySql]
    public async Task Una_riga_nuova_in_PM_parte_con_Id_null_e_torna_mappata()
    {
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);
        int idPm = RigaPm(_rossi, "OP", "Trasferta OSVA", projectId: _commessa);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("VPS +1 / ~0 / −0", voce.Dettaglio);
        Assert.Equal(1, vps.PostAllocazioni);
        SyncAssignmentUpsertDto inviata = Assert.Single(vps.Corpo<List<SyncAssignmentUpsertDto>>("/api/sync/assignments"));
        Assert.Null(inviata.Id);
        Assert.Equal(RossiVps, inviata.EmployeeId);
        Assert.Equal(RossiVps, inviata.UpdatedBy);
        Assert.Equal("Trasferta OSVA", inviata.Descrizione);
        Assert.Equal(Inizio, inviata.DataInizio);
        Assert.Equal(Fine, inviata.DataFine);
        Assert.NotNull(inviata.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, inviata.UpdatedAt!.Value.Kind);
        // Il NOW() di MySQL (ora italiana) è arrivato in UTC: due ore indietro d'estate, comunque «adesso».
        Assert.InRange((DateTime.UtcNow - inviata.UpdatedAt.Value).TotalMinutes, -1, 5);
        // La commessa creata dalla Fase 1 nello stesso giro: il suo id VPS è già in mappa e viaggia tradotto.
        using (MySqlConnection c = _schema.Apri())
            Assert.Equal(RisorseSyncMap.Carica(c, RisorseSyncMap.Project)[_commessa].RemoteId, inviata.ProjectId);

        SyncAssignmentDto sulVps = Assert.Single(vps.Allocazioni);
        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa();
        Assert.Equal(sulVps.Id, mappa[idPm].RemoteId);
        Assert.Equal(AllocazioniSync.Impronta(new RigaAlloc(_rossi, "OP", Inizio, Fine, _commessa, "Trasferta OSVA", null, null)), mappa[idPm].SyncedHash);
        Assert.True(UltimoRegistro().CreateVps >= 1);   // sommato alle anagrafiche (commessa, reparti)
        Assert.Single(RighePm());   // in PM niente doppioni
    }

    [FactRichiedeMySql]
    public async Task Una_modifica_in_PM_parte_con_l_Id_del_VPS()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Manutenzione Minebea — 2ª settimana', data_fine = '2026-09-12', updated_by = @U, updated_at = NOW() WHERE id = @Id",
                new { Id = idPm, U = _rossi });
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("VPS +0 / ~1 / −0", voce.Dettaglio);
        Assert.Equal(1, vps.PostAllocazioni);
        SyncAssignmentUpsertDto inviata = Assert.Single(vps.Corpo<List<SyncAssignmentUpsertDto>>("/api/sync/assignments"));
        Assert.Equal(op.Id, inviata.Id);
        Assert.Equal(RossiVps, inviata.UpdatedBy);
        Assert.Equal(new DateOnly(2026, 9, 12), inviata.DataFine);
        Assert.Equal("Manutenzione Minebea — 2ª settimana", op.Descrizione);   // il finto ha aggiornato la sua riga
        Assert.Equal(AllocazioniSync.Impronta(new RigaAlloc(_rossi, "OP", Inizio, new DateOnly(2026, 9, 12), null, "Manutenzione Minebea — 2ª settimana", null, null)), Mappa()[idPm].SyncedHash);
        Assert.Equal(1, UltimoRegistro().AggiornateVps);
    }

    [FactRichiedeMySql]
    public async Task Una_modifica_sul_VPS_aggiorna_PM_con_l_ora_del_VPS()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        op.DataFine = new DateOnly(2026, 9, 12);
        op.UpdatedBy = RossiVps;
        op.UpdatedAt = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~1 / −0", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        RigaLetta riga = Assert.Single(RighePm(), r => r.Id == idPm);
        Assert.Equal(new DateTime(2026, 9, 12), riga.DataFine);
        Assert.Equal(_rossi, riga.UpdatedBy);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 30, 0), riga.UpdatedAt);   // 08:30Z → 10:30 locale, NON NOW()
        Assert.Equal(1, UltimoRegistro().AggiornatePm);
        Assert.Equal(AllocazioniSync.Impronta(new RigaAlloc(_rossi, "OP", Inizio, new DateOnly(2026, 9, 12), null, "Manutenzione Minebea", null, null)), Mappa()[idPm].SyncedHash);
    }

    [FactRichiedeMySql]
    public async Task Cambiate_entrambe_vince_la_piu_recente_e_il_registro_conta_il_conflitto()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;

        // PM alle 10:00 locali (= 08:00Z), VPS alle 09:00Z: vince il VPS.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM', updated_at = '2026-08-01 10:00:00' WHERE id = @Id", new { Id = idPm });
        op.Descrizione = "Dal VPS";
        op.UpdatedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 conflitto (vince il VPS: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal("Dal VPS", Assert.Single(RighePm(), r => r.Id == idPm).Descrizione);
        Assert.Equal(new DateTime(2026, 8, 1, 11, 0, 0), Assert.Single(RighePm(), r => r.Id == idPm).UpdatedAt);
        Assert.Equal(1, UltimoRegistro().Conflitti);

        // Il contrario: PM più recente → vince PM, POST con l'Id.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM, dopo', updated_at = '2026-08-02 10:00:00' WHERE id = @Id", new { Id = idPm });
        op.Descrizione = "Dal VPS, prima";
        op.UpdatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        vps.Chiamate.Clear();

        voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 conflitto (vince PM: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        SyncAssignmentUpsertDto inviata = Assert.Single(vps.Corpo<List<SyncAssignmentUpsertDto>>("/api/sync/assignments"));
        Assert.Equal(op.Id, inviata.Id);
        Assert.Equal("Da PM, dopo", op.Descrizione);
        Assert.Equal(new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc), op.UpdatedAt);   // l'ora di PM, in UTC
        Assert.Equal("Da PM, dopo", Assert.Single(RighePm(), r => r.Id == idPm).Descrizione);

        // Dopo il conflitto i due lati sono uguali: il giro dopo non fa niente.
        vps.Chiamate.Clear();
        int registro = RigheRegistro();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.DoesNotContain("conflitto", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());
    }

    [FactRichiedeMySql]
    public async Task Una_DELETE_in_PM_cancella_sul_VPS_con_l_autore_e_toglie_la_mappa()
    {
        (VpsFinto vps, SyncAssignmentDto op, SyncAssignmentDto ferie) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        // Come DeleteAssignment del controller: prima chi cancella in res_notify_pending, poi la DELETE.
        using (MySqlConnection c = _schema.Apri())
        {
            c.Execute("INSERT INTO res_notify_pending (assignment_id, made_by, action) VALUES (@Id, @U, 'delete')", new { Id = idPm, U = _verdi });
            c.Execute("DELETE FROM res_assignments WHERE id = @Id", new { Id = idPm });
        }
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("VPS +0 / ~0 / −1", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(1, vps.PostCancellazioni);
        SyncDeleteRequest richiesta = vps.Corpo<SyncDeleteRequest>("/api/sync/assignments/delete");
        Assert.Equal(new[] { op.Id }, richiesta.Ids);
        Assert.Equal(VerdiVps, richiesta.MadeBy);                       // l'autore, tradotto
        Assert.Equal(ferie.Id, Assert.Single(vps.Allocazioni).Id);       // sul VPS resta solo la ferie
        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa();
        Assert.Single(mappa);
        Assert.False(mappa.ContainsKey(idPm));
        Assert.Equal(1, UltimoRegistro().CancellateVps);
    }

    [FactRichiedeMySql]
    public async Task Una_riga_sparita_dal_VPS_si_cancella_in_PM_e_finisce_in_res_notify_pending()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        vps.Allocazioni.Remove(op);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −1", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(0, vps.PostCancellazioni);
        Assert.DoesNotContain(RighePm(), r => r.Id == idPm);
        Assert.Single(RighePm());
        Assert.False(Mappa().ContainsKey(idPm));
        using MySqlConnection c = _schema.Apri();
        var pendente = c.QuerySingle<(string Action, int? MadeBy, int? Emp, string? Descrizione)>(
            "SELECT action, made_by, orig_employee_id, orig_descrizione FROM res_notify_pending WHERE assignment_id = @Id", new { Id = idPm });
        Assert.Equal("delete", pendente.Action);
        Assert.Null(pendente.MadeBy);                                    // chi ha cancellato sul VPS non si sa
        Assert.Equal(_rossi, pendente.Emp);
        Assert.Equal("Manutenzione Minebea", pendente.Descrizione);
        Assert.Equal(1, UltimoRegistro().CancellatePm);
    }

    [FactRichiedeMySql]
    public async Task Le_righe_di_un_dipendente_non_mappato_si_saltano_e_non_si_toccano_mai()
    {
        (VpsFinto vps, _, _) = VpsConDueRighe();
        // Sul VPS una riga di «zamputo» (id 38, solo di là): nessuno la reclama.
        vps.Allocazione(38, "OP", Inizio, Fine, "Solo VPS");
        RisorseSyncService svc = Servizio(vps);
        int idMonticone = RigaPm(_monticone, "OP", "Cantiere esterno");

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(0, vps.PostCancellazioni);
        Assert.Contains("saltate 2 (dipendente non mappato: Christian Monticone, dipendente VPS 38 non mappato)", voce.Dettaglio);
        Assert.Contains("PM +2 / ~0 / −0", voce.Dettaglio);            // le due righe buone entrano lo stesso
        Assert.Equal(3, RighePm().Count);                                // 2 dal VPS + Monticone, intatta
        RigaLetta monticone = Assert.Single(RighePm(), r => r.Id == idMonticone);
        Assert.Equal("Cantiere esterno", monticone.Descrizione);
        Assert.Equal(2, Mappa().Count);
        Assert.False(Mappa().ContainsKey(idMonticone));
        Assert.Equal(3, vps.Allocazioni.Count);                          // la riga di zamputo è ancora lì
        Assert.Equal(2, UltimoRegistro().Saltate);

        // E un giro dopo, del timer: stesse righe saltate, nessuna scrittura, nessuna riga di registro.
        int registro = RigheRegistro();
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Contains("saltate 2", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());
        Assert.Equal(3, RighePm().Count);
    }

    [FactRichiedeMySql]
    public async Task La_stessa_allocazione_su_entrambi_i_lati_senza_mappa_si_abbina_senza_scrivere()
    {
        VpsFinto vps = Vps();
        SyncAssignmentDto sulVps = vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Manutenzione", updatedBy: RossiVps, updatedAtUtc: Luglio1Utc);
        RisorseSyncService svc = Servizio(vps);
        int idPm = RigaPm(_rossi, "OP", " Manutenzione ");   // stessa riga, spazi diversi: l'impronta è la stessa

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("Allocazioni: PM +0 / ~0 / −0, VPS +0 / ~0 / −0, 1 abbinate per contenuto", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Single(RighePm());
        Assert.Single(vps.Allocazioni);
        RisorseSyncMap.Voce voceMappa = Assert.Single(Mappa()).Value;
        Assert.Equal(sulVps.Id, voceMappa.RemoteId);
        Assert.Equal(AllocazioniSync.Impronta(new RigaAlloc(_rossi, "OP", Inizio, Fine, null, "Manutenzione", null, null)), voceMappa.SyncedHash);
        Assert.True(Mappa().ContainsKey(idPm));
        // La riga di PM non è stata toccata (descrizione con gli spazi com'era, updated_at suo).
        Assert.Equal(" Manutenzione ", Assert.Single(RighePm()).Descrizione);
    }

    [FactRichiedeMySql]
    public async Task Un_secondo_giro_identico_non_scrive_niente_e_il_timer_non_lascia_righe_nel_registro()
    {
        (VpsFinto vps, _, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        int idPm = RigaPm(_verdi, "FLEX", "Supporto");
        await svc.RunNowAsync("manuale");
        int registro = RigheRegistro();
        List<RigaLetta> prima = RighePm();
        Dictionary<int, RisorseSyncMap.Voce> mappaPrima = Mappa();
        Assert.Equal(3, prima.Count);
        Assert.Equal(3, mappaPrima.Count);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("Allocazioni: PM +0 / ~0 / −0, VPS +0 / ~0 / −0, 3 invariate", voce.Dettaglio);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(0, vps.PostCancellazioni);
        Assert.DoesNotContain(vps.Chiamate, x => x.Metodo != "GET" && !x.Percorso.EndsWith("/login"));
        Assert.Equal(registro, RigheRegistro());
        // Righe e mappa identiche a prima, updated_at compresi.
        List<RigaLetta> dopo = RighePm();
        Assert.Equal(prima.Select(r => (r.Id, r.Descrizione, r.UpdatedAt)), dopo.Select(r => (r.Id, r.Descrizione, r.UpdatedAt)));
        Assert.Equal(mappaPrima.OrderBy(kv => kv.Key), Mappa().OrderBy(kv => kv.Key));
        Assert.True(Mappa().ContainsKey(idPm));
        Assert.Null(svc.LastError);
    }

    [FactRichiedeMySql]
    public async Task Uno_skipped_del_VPS_si_conta_e_non_fa_fallire_il_giro_ne_riparte_a_ogni_timer()
    {
        VpsFinto vps = Vps();
        vps.DipendentiDaSaltare.Add(VerdiVps);
        RisorseSyncService svc = Servizio(vps);
        int idVerdi = RigaPm(_verdi, "OP", "Rifiutata");
        int idRossi = RigaPm(_rossi, "OP", "Accettata");

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("VPS +1 / ~0 / −0", voce.Dettaglio);
        Assert.Contains("saltate 1", voce.Dettaglio);
        Assert.Contains($"allocazione Anna Verdi 07/09-11/09: EmployeeId {VerdiVps} inesistente", voce.Dettaglio);
        Assert.True(Mappa().ContainsKey(idRossi));
        Assert.False(Mappa().ContainsKey(idVerdi));
        Assert.Contains("Anna Verdi", svc.LastError);
        Assert.Equal(1, UltimoRegistro().Saltate);

        // Timer: la riga rifiutata NON riparte (stesso dato), nessuna POST, nessuna riga nuova nel registro.
        int registro = RigheRegistro();
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Contains("saltate (già segnalate): 1", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());

        // Il dato cambia (e stavolta il VPS accetta): riparte da sola e finisce in mappa.
        vps.DipendentiDaSaltare.Clear();
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Riprovata', updated_at = NOW() WHERE id = @Id", new { Id = idVerdi });
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("pm");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(1, vps.PostAllocazioni);
        Assert.True(Mappa().ContainsKey(idVerdi));
        Assert.Null(svc.LastError);
    }

    [FactRichiedeMySql]
    public async Task Una_riga_PM_su_una_commessa_non_mappata_si_salta_e_non_perde_la_commessa()
    {
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);
        int inSospeso;
        using (MySqlConnection c = _schema.Apri())
            inSospeso = Inserisci(c,
                "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20260901.002', 'In sospeso', @C, @P, 'DRAFT')",
                new { C = c.ExecuteScalar<int>("SELECT customer_id FROM projects WHERE id = @Id", new { Id = _commessa }), P = c.ExecuteScalar<int>("SELECT pm_id FROM projects WHERE id = @Id", new { Id = _commessa }) });
        // DRAFT: la Fase 1 non la manda, quindi non è in mappa PROJECT. La riga NON deve partire senza commessa.
        int idPm = RigaPm(_rossi, "OP", "Sopralluogo", projectId: inSospeso);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Contains("saltate 1 (commessa non mappata: C20260901.002)", voce.Dettaglio);
        Assert.False(Mappa().ContainsKey(idPm));
        Assert.Empty(vps.Allocazioni);

        // Il giro dopo: ancora saltata, la commessa è ancora lì (era questo il baco: project_id → NULL).
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(inSospeso, Assert.Single(RighePm()).ProjectId);
        Assert.Equal("Sopralluogo", Assert.Single(RighePm()).Descrizione);

        // La commessa passa ad ACTIVE: la Fase 1 la mappa e la riga parte da sola, con la commessa tradotta.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE projects SET status = 'ACTIVE' WHERE id = @Id", new { Id = inSospeso });
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(1, vps.PostAllocazioni);
        SyncAssignmentUpsertDto inviata = Assert.Single(vps.Corpo<List<SyncAssignmentUpsertDto>>("/api/sync/assignments"));
        using (MySqlConnection c = _schema.Apri())
            Assert.Equal(RisorseSyncMap.Carica(c, RisorseSyncMap.Project)[inSospeso].RemoteId, inviata.ProjectId);
        Assert.True(Mappa().ContainsKey(idPm));
        Assert.Equal(inSospeso, Assert.Single(RighePm()).ProjectId);
    }

    [FactRichiedeMySql]
    public async Task Una_descrizione_lunga_dal_VPS_entra_tagliata_a_500_e_non_fa_ping_pong()
    {
        VpsFinto vps = Vps();
        string lunga = new string('a', 499) + " " + new string('b', 200);
        SyncAssignmentDto sulVps = vps.Allocazione(RossiVps, "OP", Inizio, Fine, lunga, updatedAtUtc: Luglio1Utc);
        RisorseSyncService svc = Servizio(vps);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +1 / ~0 / −0", voce.Dettaglio);
        Assert.DoesNotContain("errori", voce.Dettaglio);
        RigaLetta riga = Assert.Single(RighePm());
        Assert.Equal(499, riga.Descrizione!.Length);          // 500 tagliati, poi via lo spazio in coda
        Assert.Equal(sulVps.Id, Mappa()[riga.Id].RemoteId);
        Assert.Equal(lunga, sulVps.Descrizione);              // il VPS tiene il suo testo

        // Il giro dopo: le impronte coincidono (il taglio è nella forma comune), niente POST, niente registro.
        int registro = RigheRegistro();
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Contains("1 invariate", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());
    }

    [FactRichiedeMySql]
    public async Task Un_VPS_che_risponde_zero_allocazioni_con_la_mappa_piena_ferma_il_giro_senza_cancellare()
    {
        // Il freno ha un minimo (SogliaCancellazioniPm = 10 coppie): qui la mappa è piena davvero.
        (VpsFinto vps, _, _) = VpsConDueRighe();
        for (int i = 0; i < 8; i++)
            vps.Allocazione(RossiVps, "OP", Inizio.AddDays(7 * (i + 1)), Fine.AddDays(7 * (i + 1)), $"Settimana {i + 2}");
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        Assert.Equal(10, RighePm().Count);
        vps.Allocazioni.Clear();   // database del VPS ripristinato vuoto
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("errore", voce.Esito);
        Assert.Contains("0 allocazioni con 10 righe mappate", voce.Dettaglio);
        Assert.Equal(10, RighePm().Count);                 // niente cancellato
        Assert.Equal(10, Mappa().Count);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(0, vps.PostCancellazioni);
        using (MySqlConnection c = _schema.Apri())
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_notify_pending"));
        Assert.Contains("0 allocazioni", svc.LastError);

        // Stessa cosa dall'hub; «Sincronizza adesso» dal pannello invece è una scelta dell'operatore: procede.
        voce = await svc.RunNowAsync("hub");
        Assert.Equal("errore", voce.Esito);
        Assert.Equal(10, RighePm().Count);
        voce = await svc.RunNowAsync("manuale");
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −10", voce.Dettaglio);
        Assert.Empty(RighePm());
        Assert.Empty(Mappa());
    }

    [FactRichiedeMySql]
    public async Task Sotto_la_soglia_cancellare_l_ultima_allocazione_sul_VPS_e_una_cancellazione_legittima()
    {
        // Una sola coppia mappata e il VPS che risponde 0 righe: non c'è «massa» da proteggere,
        // la cancellazione riga per riga di §4.3 procede da sola (niente errore a ogni giro del timer).
        VpsFinto vps = Vps();
        vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Unica", updatedAtUtc: Luglio1Utc);
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        Assert.Single(RighePm());
        vps.Allocazioni.Clear();   // l'operatore ha cancellato l'ultima allocazione sul VPS
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −1", voce.Dettaglio);
        Assert.Empty(RighePm());
        Assert.Empty(Mappa());
        Assert.Null(svc.LastError);

        // E il giro dopo il motore è libero: niente errore, niente riga di registro.
        int registro = RigheRegistro();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Equal(registro, RigheRegistro());
    }

    [FactRichiedeMySql]
    public async Task Una_modifica_fatta_in_PM_a_giro_in_corso_non_viene_sovrascritta()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        string improntaPrima = Mappa()[idPm].SyncedHash!;
        // Il VPS è cambiato (ora vecchia, così al giro dopo vince PM)…
        op.Descrizione = "Dal VPS";
        op.UpdatedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        // …e fra la lettura di PM e la scrittura in PM un utente salva dal planner.
        vps.PrimaDelleAllocazioni = () =>
        {
            using MySqlConnection c = _schema.Apri();
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM in corsa', updated_by = @U, updated_at = NOW() WHERE id = @Id", new { Id = idPm, U = _rossi });
        };
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("rimandate 1 (modificata in PM durante il giro, si ridecide al prossimo: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.Contains("PM +0 / ~0 / −0", voce.Dettaglio);
        Assert.Equal("Da PM in corsa", Assert.Single(RighePm(), r => r.Id == idPm).Descrizione);   // NON sovrascritta
        Assert.Equal(improntaPrima, Mappa()[idPm].SyncedHash);                                      // mappa ferma
        Assert.Equal(0, UltimoRegistro().Conflitti);                                                // un rimando non è un conflitto
        Assert.DoesNotContain("conflitto", voce.Dettaglio);

        // Il giro dopo, senza intrusi: cambiate entrambe, PM è più recente → vince PM, POST con l'Id.
        vps.PrimaDelleAllocazioni = null;
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("pm");
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 conflitto (vince PM: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.Equal(op.Id, Assert.Single(vps.Corpo<List<SyncAssignmentUpsertDto>>("/api/sync/assignments")).Id);
        Assert.Equal("Da PM in corsa", op.Descrizione);
    }

    [FactRichiedeMySql]
    public async Task Una_cancellazione_dal_VPS_su_una_riga_appena_toccata_in_PM_si_rimanda_al_giro_dopo()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        vps.Allocazioni.Remove(op);
        vps.PrimaDelleAllocazioni = () =>
        {
            using MySqlConnection c = _schema.Apri();
            c.Execute("UPDATE res_assignments SET descrizione = 'Toccata in corsa', updated_at = NOW() WHERE id = @Id", new { Id = idPm });
        };

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("si ridecide al prossimo", voce.Dettaglio);
        Assert.Equal(0, UltimoRegistro().Conflitti);
        Assert.Contains(RighePm(), r => r.Id == idPm);                 // ancora lì
        Assert.True(Mappa().ContainsKey(idPm));
        using (MySqlConnection c = _schema.Apri())
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_notify_pending WHERE assignment_id = @Id", new { Id = idPm }));   // il SAVEPOINT ha ritirato anche il digest

        // Giro dopo: la cancellazione vince comunque (§4.3), ma finisce nel registro come conflitto.
        vps.PrimaDelleAllocazioni = null;
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 conflitto (cancellata sul VPS e modificata in PM, vince la cancellazione", voce.Dettaglio);
        Assert.Equal(1, UltimoRegistro().Conflitti);
        Assert.DoesNotContain(RighePm(), r => r.Id == idPm);
        Assert.False(Mappa().ContainsKey(idPm));
    }

    [FactRichiedeMySql]
    public async Task Un_conflitto_vinto_da_PM_che_il_VPS_rifiuta_non_si_conta_e_nessuno_vince()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;

        // Cambiate entrambe, PM più recente → vince PM… ma il VPS rifiuta la riga (skipped): nessuno ha vinto.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM, dopo', updated_at = '2026-08-02 10:00:00' WHERE id = @Id", new { Id = idPm });
        op.Descrizione = "Dal VPS, prima";
        op.UpdatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        vps.DipendentiDaSaltare.Add(op.EmployeeId);
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Equal(1, vps.PostAllocazioni);                       // la POST è partita…
        Assert.DoesNotContain("vince", voce.Dettaglio);            // …ma nessuno ha vinto
        Assert.Equal(0, UltimoRegistro().Conflitti);
        Assert.Equal(1, UltimoRegistro().Saltate);
        Assert.Equal("Dal VPS, prima", vps.Allocazioni.Single(a => a.Id == op.Id).Descrizione);   // il VPS non è stato toccato

        // Il VPS torna a prendere la riga: al giro manuale il conflitto si racconta e si conta una volta sola.
        vps.DipendentiDaSaltare.Clear();
        voce = await svc.RunNowAsync("manuale");
        Assert.Contains("1 conflitto (vince PM: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.Equal(1, UltimoRegistro().Conflitti);
        Assert.Equal("Da PM, dopo", vps.Allocazioni.Single(a => a.Id == op.Id).Descrizione);
    }

    [FactRichiedeMySql]
    public async Task Un_conflitto_rimandato_dalla_guardia_di_concorrenza_non_si_conta_e_nessuno_vince()
    {
        (VpsFinto vps, SyncAssignmentDto op, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int idPm = Mappa().Single(kv => kv.Value.RemoteId == op.Id).Key;
        string improntaPrima = Mappa()[idPm].SyncedHash!;
        // Cambiate entrambe: PM alle 10:00 locali (= 08:00Z), VPS alle 09:00Z → al passo C «vince il VPS»…
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM', updated_at = '2026-08-01 10:00:00' WHERE id = @Id", new { Id = idPm });
        op.Descrizione = "Dal VPS";
        op.UpdatedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        // …ma fra la lettura di PM e la scrittura in PM un utente salva dal planner: l'UPDATE tocca 0 righe.
        vps.PrimaDelleAllocazioni = () =>
        {
            using MySqlConnection c = _schema.Apri();
            c.Execute("UPDATE res_assignments SET descrizione = 'Da PM in corsa', updated_by = @U, updated_at = NOW() WHERE id = @Id", new { Id = idPm, U = _rossi });
        };
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        // Una sola voce (il rimando), nessun conflitto contato, nessun «vince»: in PM non è stato scritto niente.
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("rimandate 1 (modificata in PM durante il giro, si ridecide al prossimo: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.DoesNotContain("vince", voce.Dettaglio);
        Assert.DoesNotContain("conflitto", voce.Dettaglio);
        Assert.Equal(0, UltimoRegistro().Conflitti);
        Assert.Equal("Da PM in corsa", Assert.Single(RighePm(), r => r.Id == idPm).Descrizione);
        Assert.Equal(improntaPrima, Mappa()[idPm].SyncedHash);
        Assert.Equal(0, vps.PostAllocazioni);

        // Il giro dopo si ridecide: PM (adesso) è più recente del VPS (09:00Z del 1° agosto) → vince PM, e stavolta si conta.
        vps.PrimaDelleAllocazioni = null;
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("pm");
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 conflitto (vince PM: Mario Rossi 07/09-11/09)", voce.Dettaglio);
        Assert.DoesNotContain("rimandate", voce.Dettaglio);
        Assert.Equal(1, UltimoRegistro().Conflitti);
        Assert.Equal("Da PM in corsa", op.Descrizione);
    }

    [FactRichiedeMySql]
    public async Task L_eco_dell_hub_del_VPS_non_lascia_righe_nel_registro()
    {
        (VpsFinto vps, _, _) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int registro = RigheRegistro();

        // Il VPS rimanda AssignmentsChanged anche per le POST del motore: quel giro non trova niente da fare.
        RisorseSyncLogEntry voce = await svc.RunNowAsync("hub");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("2 invariate", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());
    }

    [FactRichiedeMySql]
    public async Task Una_mappa_che_punta_a_un_oggetto_cancellato_in_PM_non_fa_violare_le_FK()
    {
        VpsFinto vps = Vps();
        // Una commessa e un dipendente mappati un tempo, poi cancellati in PM: la mappa (senza FK) è rimasta.
        using (MySqlConnection c = _schema.Apri())
        {
            RisorseSyncMap.Salva(c, RisorseSyncMap.Project, 999_999, 777, "x");
            RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, 999_998, 12, "x");
        }
        vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Su commessa sparita", projectId: 777);
        vps.Allocazione(12, "OP", Inizio, Fine, "Di un dipendente sparito");
        RisorseSyncService svc = Servizio(vps);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        // Le coppie orfane escono dalla mappa (JOIN): le due righe VPS sono «non mappate» e si saltano,
        // nessuna entra in PM (né con la commessa azzerata: perderebbe il legame sul VPS al giro dopo).
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −0", voce.Dettaglio);
        Assert.Contains("saltate 2 (dipendente VPS 12 non mappato, commessa VPS 777 non mappata)", voce.Dettaglio);
        Assert.Empty(RighePm());
        Assert.Empty(Mappa());
        Assert.Equal(2, vps.Allocazioni.Count);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Equal(0, vps.PostCancellazioni);
    }

    [FactRichiedeMySql]
    public async Task Una_riga_VPS_su_una_commessa_non_mappata_si_salta_e_riparte_quando_la_commessa_entra_in_mappa()
    {
        VpsFinto vps = Vps();
        SyncAssignmentDto sulVps = vps.Allocazione(RossiVps, "OP", Inizio, Fine, "Avviamento", projectId: 777, updatedAtUtc: Luglio1Utc);
        RisorseSyncService svc = Servizio(vps);
        int inArrivo;
        using (MySqlConnection c = _schema.Apri())
            inArrivo = Inserisci(c,
                "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20260901.003', 'In arrivo', @C, @P, 'DRAFT')",
                new { C = c.ExecuteScalar<int>("SELECT customer_id FROM projects WHERE id = @Id", new { Id = _commessa }), P = c.ExecuteScalar<int>("SELECT pm_id FROM projects WHERE id = @Id", new { Id = _commessa }) });

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        // La commessa VPS 777 non è in mappa: la riga si salta, non entra in PM senza commessa.
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +0 / ~0 / −0", voce.Dettaglio);
        Assert.Contains("saltate 1 (commessa VPS 777 non mappata)", voce.Dettaglio);
        Assert.Empty(RighePm());
        Assert.Empty(Mappa());
        Assert.Equal(777, sulVps.ProjectId);

        // Un giro del timer: stessa riga saltata, nessuna scrittura, nessuna riga di registro.
        int registro = RigheRegistro();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Empty(RighePm());
        Assert.Equal(registro, RigheRegistro());

        // La commessa entra in mappa (come la lascerebbe la Fase 1; qui a mano, perché la 777 sul VPS non nasce da PM).
        using (MySqlConnection c = _schema.Apri())
            RisorseSyncMap.Salva(c, RisorseSyncMap.Project, inArrivo, 777, null);
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("timer");

        // La riga riparte da sola e entra in PM con la commessa tradotta.
        Assert.Equal("ok", voce.Esito);
        Assert.Contains("PM +1 / ~0 / −0", voce.Dettaglio);
        Assert.DoesNotContain("commessa VPS 777", voce.Dettaglio);
        RigaLetta riga = Assert.Single(RighePm());
        Assert.Equal(inArrivo, riga.ProjectId);
        Assert.Equal("Avviamento", riga.Descrizione);
        Assert.Equal(sulVps.Id, Mappa()[riga.Id].RemoteId);
        Assert.Equal(777, sulVps.ProjectId);                            // sul VPS la commessa è ancora lì
    }

    [FactRichiedeMySql]
    public async Task Un_dipendente_cancellato_in_PM_lascia_le_sue_allocazioni_sul_VPS_e_libera_la_mappa()
    {
        (VpsFinto vps, SyncAssignmentDto op, SyncAssignmentDto ferie) = VpsConDueRighe();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        Assert.Equal(2, RighePm().Count);
        Assert.Equal(2, Mappa().Count);
        // Hard delete di Rossi in PM (caso raro: di norma si mette TERMINATED): le sue allocazioni vanno in cascata,
        // la sua coppia EMPLOYEE resta in res_sync_map (senza FK) ma il JOIN del passo A la lascia fuori.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("DELETE FROM employees WHERE id = @Id", new { Id = _rossi });
        Assert.Single(RighePm());
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        // REGOLA SCELTA: la riga VPS resta (nominata «non mappato»), la coppia ASSIGNMENT orfana esce dalla mappa.
        Assert.Equal("ok", voce.Esito);
        Assert.Contains($"dipendente VPS {RossiVps} non mappato", voce.Dettaglio);
        Assert.Contains("PM +0 / ~0 / −0, VPS +0 / ~0 / −0", voce.Dettaglio);
        Assert.Equal(0, vps.PostCancellazioni);
        Assert.Equal(0, vps.PostAllocazioni);
        Assert.Contains(vps.Allocazioni, a => a.Id == op.Id);           // ancora sul VPS
        Assert.Equal(2, vps.Allocazioni.Count);
        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa();
        Assert.Equal(ferie.Id, Assert.Single(mappa).Value.RemoteId);   // solo la coppia di Verdi
        Assert.DoesNotContain(mappa.Values, v => v.RemoteId == op.Id);
        Assert.Single(RighePm());

        // Il giro dopo: la riga VPS è ancora saltata, niente da fare, nessuna riga di registro.
        int registro = RigheRegistro();
        voce = await svc.RunNowAsync("timer");
        Assert.Equal("ok", voce.Esito);
        Assert.Contains($"dipendente VPS {RossiVps} non mappato", voce.Dettaglio);
        Assert.Equal(registro, RigheRegistro());
        Assert.Single(Mappa());
    }
}
