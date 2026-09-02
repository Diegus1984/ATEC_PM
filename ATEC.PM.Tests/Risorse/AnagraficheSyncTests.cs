using System.Net;
using System.Text;
using System.Text.Json;
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
/// Il seme della mappa dipendenti (PIANO-SYNC-RISORSE.md §5 punto 3): le tre regole di
/// <see cref="AnagraficheSync.Abbina"/> in ordine di fiducia — username, nome + cognome,
/// cognome + token del nome — e le loro guardie: un VPS si abbina una volta sola, i già
/// mappati stanno fuori, un candidato ambiguo non si abbina. Niente database: test puri.
/// </summary>
public class AbbinamentoDipendentiTests
{
    private static DipendentePm Pm(int id, string nome, string cognome, string? username = null, string empType = "INTERNAL") =>
        new() { Id = id, FirstName = nome, LastName = cognome, Username = username, EmpType = empType };

    private static SyncEmployeeDto Vps(int id, string nome, string cognome, string? username = null, string status = "ACTIVE") =>
        new() { Id = id, FirstName = nome, LastName = cognome, Username = username, Status = status };

    private static readonly Dictionary<int, RisorseSyncMap.Voce> MappaVuota = new();

    [Theory]
    [InlineData("  Larganà ", "largana")]
    [InlineData("VASILE   Ovidiu", "vasile ovidiu")]
    [InlineData("Émile-Ñoño", "emile-nono")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizza_minuscolo_trim_spazi_collassati_senza_accenti(string? dentro, string atteso) =>
        Assert.Equal(atteso, AnagraficheSync.Normalizza(dentro));

    [Fact]
    public void Lo_username_vince_sul_nome()
    {
        // Sul VPS c'è un omonimo con un altro login e un «Marco Bianchi» col login di Mario: comanda il login.
        var pm = new[] { Pm(1, "Mario", "Rossi", "m.rossi") };
        var vps = new[] { Vps(5, "Mario", "Rossi", "altro"), Vps(6, "Marco", "Bianchi", " M.ROSSI ") };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, MappaVuota);

        Abbinamento a = Assert.Single(e.Abbinamenti);
        Assert.Equal((1, 6, AnagraficheSync.CriterioUsername), (a.LocalId, a.RemoteId, a.Criterio));
        Assert.Empty(e.NonAbbinatiPm);
        Assert.Equal(5, Assert.Single(e.SoloVps).Id);
    }

    [Fact]
    public void Nome_e_cognome_si_abbinano_anche_con_accenti_diversi()
    {
        var pm = new[] { Pm(2, "Anna", "Larganà") };
        var vps = new[] { Vps(10, "anna", "Largana", "a.largana") };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, MappaVuota);

        Abbinamento a = Assert.Single(e.Abbinamenti);
        Assert.Equal((2, 10, AnagraficheSync.CriterioNome), (a.LocalId, a.RemoteId, a.Criterio));
    }

    [Fact]
    public void I_token_del_nome_abbinano_solo_se_il_candidato_e_unico()
    {
        // «Vasile Ovidiu Obreja» in PM, «Ovidiu Obreja» sul VPS: i token del nome VPS stanno dentro quelli PM.
        EsitoAbbinamento unico = AnagraficheSync.Abbina(
            new[] { Pm(3, "Vasile Ovidiu", "Obreja") },
            new[] { Vps(20, "Ovidiu", "Obreja") },
            MappaVuota);
        Abbinamento a = Assert.Single(unico.Abbinamenti);
        Assert.Equal((3, 20, AnagraficheSync.CriterioToken), (a.LocalId, a.RemoteId, a.Criterio));

        // Due Obreja compatibili sul VPS: ambiguo, nessuno dei due.
        EsitoAbbinamento dueVps = AnagraficheSync.Abbina(
            new[] { Pm(3, "Vasile Ovidiu", "Obreja") },
            new[] { Vps(20, "Ovidiu", "Obreja"), Vps(21, "Vasile", "Obreja") },
            MappaVuota);
        Assert.Empty(dueVps.Abbinamenti);
        Assert.Single(dueVps.NonAbbinatiPm);
        Assert.Equal(2, dueVps.SoloVps.Count);

        // Due Obreja compatibili in PM per lo stesso VPS: ambiguo anche da questa parte.
        EsitoAbbinamento duePm = AnagraficheSync.Abbina(
            new[] { Pm(3, "Vasile Ovidiu", "Obreja"), Pm(4, "Vasile", "Obreja") },
            new[] { Vps(20, "Ovidiu Vasile", "Obreja") },
            MappaVuota);
        Assert.Empty(duePm.Abbinamenti);
        Assert.Equal(2, duePm.NonAbbinatiPm.Count);
    }

    [Fact]
    public void Scenario_reale_36_38_Abatangelo_si_sistema_e_Monticone_e_zamputo_restano_fuori()
    {
        // §2: in PM 36 = Monticone (esterno, senza utente) e 38 = Abatangelo; sul VPS 36 = Abatangelo e 38 = zamputo.
        var pm = new[]
        {
            Pm(36, "Christian", "Monticone", null, "EXTERNAL"),
            Pm(38, "Alessandra", "Abatangelo", "a.abatangelo"),
            Pm(37, "Edoardo", "Carretta", "e.carretta"),
        };
        var vps = new[]
        {
            Vps(36, "Alessandra", "Abatangelo", "a.abatangelo"),
            Vps(37, "Edoardo", "Carretta", "e.carretta"),
            Vps(38, "pasquale", "zamputo", null, "TERMINATED"),
        };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, MappaVuota);

        Assert.Equal(2, e.Abbinamenti.Count);
        Assert.Contains(e.Abbinamenti, a => a.LocalId == 38 && a.RemoteId == 36);
        Assert.Contains(e.Abbinamenti, a => a.LocalId == 37 && a.RemoteId == 37);
        Assert.Equal(36, Assert.Single(e.NonAbbinatiPm).Id);   // Monticone
        Assert.Equal(38, Assert.Single(e.SoloVps).Id);         // zamputo
    }

    [Fact]
    public void Due_omonimi_in_PM_e_uno_sul_VPS_non_si_abbina_nessuno()
    {
        // Due «Mario Rossi» in PM e uno solo sul VPS: scegliere il primo sarebbe scegliere a
        // caso (e la mappa poi non si corregge da sola) → nessuno dei due, il VPS resta libero.
        var pm = new[] { Pm(1, "Mario", "Rossi"), Pm(2, "Mario", "Rossi") };
        var vps = new[] { Vps(9, "Mario", "Rossi") };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, MappaVuota);

        Assert.Empty(e.Abbinamenti);
        Assert.Equal(2, e.NonAbbinatiPm.Count);
        Assert.Equal(9, Assert.Single(e.SoloVps).Id);

        // Stessa guardia sullo username: due PM con lo stesso login (non dovrebbe succedere, ma
        // la colonna non è UNIQUE) e un VPS solo → nessuno.
        EsitoAbbinamento login = AnagraficheSync.Abbina(
            new[] { Pm(1, "Mario", "Rossi", "m.rossi"), Pm(2, "Marco", "Rossini", "M.ROSSI") },
            new[] { Vps(9, "Mario", "Rossi", "m.rossi") },
            MappaVuota);
        // La regola 1 si ferma, ma la 2 (nome + cognome, unico su entrambi i lati) prende Mario.
        Abbinamento a = Assert.Single(login.Abbinamenti);
        Assert.Equal((1, 9, AnagraficheSync.CriterioNome), (a.LocalId, a.RemoteId, a.Criterio));
    }

    [Fact]
    public void Gli_account_di_sistema_del_VPS_stanno_fuori_dal_seme()
    {
        // GET employees torna anche admin (ADMIN) e «[SYNC] ATEC PM» (SYNC): un PM omonimo
        // dell'admin non deve mapparsi a lui, e nessuno dei due deve finire fra i «solo VPS».
        var pm = new[] { new DipendentePm { Id = 1, FirstName = "Diego", LastName = "Frattini", Username = "d.frattini", UserRole = "PM" } };
        var vps = new[]
        {
            new SyncEmployeeDto { Id = 1, FirstName = "Diego", LastName = "Frattini", UserRole = "ADMIN", Username = "admin" },
            new SyncEmployeeDto { Id = 2, FirstName = "[SYNC]", LastName = "ATEC PM", UserRole = "SYNC", Username = "sync.pm" },
        };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, MappaVuota);

        Assert.Empty(e.Abbinamenti);
        Assert.Empty(e.SoloVps);
        Assert.Equal(1, Assert.Single(e.NonAbbinatiPm).Id);
    }

    [Fact]
    public void I_gia_mappati_stanno_fuori_da_entrambi_i_lati()
    {
        var mappa = new Dictionary<int, RisorseSyncMap.Voce> { [1] = new(9, "abc") };
        var pm = new[] { Pm(1, "Mario", "Rossi", "m.rossi"), Pm(2, "Mario", "Rossi") };
        // VPS 9 è già di PM 1 (anche se il login combacerebbe con PM 1 di nuovo); resta VPS 10.
        var vps = new[] { Vps(9, "Mario", "Rossi", "m.rossi"), Vps(10, "Mario", "Rossi") };

        EsitoAbbinamento e = AnagraficheSync.Abbina(pm, vps, mappa);

        Abbinamento a = Assert.Single(e.Abbinamenti);
        Assert.Equal((2, 10), (a.LocalId, a.RemoteId));
        Assert.Empty(e.NonAbbinatiPm);
        Assert.Empty(e.SoloVps);
    }
}

/// <summary>Le impronte (<c>synced_hash</c>) e i payload: stabili, e le credenziali non c'entrano.</summary>
public class ImprontaAnagraficheTests
{
    [Fact]
    public void L_impronta_del_dipendente_ignora_le_credenziali_e_sente_i_campi_anagrafici()
    {
        var a = new SyncEmployeeDto { Id = 5, FirstName = "Mario", LastName = "Rossi", Email = "m@atec.it", EmpType = "INTERNAL", Status = "ACTIVE", UserRole = "TECH", Username = "m.rossi", PasswordHash = "$2a$uno" };
        var b = new SyncEmployeeDto { Id = null, FirstName = "Mario", LastName = "Rossi", Email = "m@atec.it", EmpType = "INTERNAL", Status = "ACTIVE", UserRole = "PM", Username = "altro", PasswordHash = "$2a$due" };
        var c = new SyncEmployeeDto { Id = 5, FirstName = "Mario", LastName = "Rossini", Email = "m@atec.it", EmpType = "INTERNAL", Status = "ACTIVE" };

        string ia = AnagraficheSync.ImprontaDipendente(a);
        Assert.Equal(64, ia.Length);
        Assert.Equal(ia, AnagraficheSync.ImprontaDipendente(a));            // stabile
        Assert.Equal(ia, AnagraficheSync.ImprontaDipendente(b));            // credenziali e id non contano
        Assert.NotEqual(ia, AnagraficheSync.ImprontaDipendente(c));         // il cognome sì
        Assert.NotEqual(ia, AnagraficheSync.ImprontaDipendente(new SyncEmployeeDto { FirstName = "Mario", LastName = "Rossi", Email = "m@atec.it", EmpType = "INTERNAL", Status = "TERMINATED" }));
    }

    [Fact]
    public void L_impronta_della_commessa_sente_codice_titolo_e_stato()
    {
        var p = new SyncProjectDto { Id = 1, Code = "C20260901.001", Title = "OSVA", Status = "ACTIVE" };
        string i = AnagraficheSync.ImprontaCommessa(p);
        Assert.Equal(i, AnagraficheSync.ImprontaCommessa(new SyncProjectDto { Id = 99, Code = "C20260901.001", Title = "OSVA", Status = "ACTIVE" }));
        Assert.NotEqual(i, AnagraficheSync.ImprontaCommessa(new SyncProjectDto { Code = "C20260901.001", Title = "OSVA", Status = "ON_HOLD" }));
    }

    [Fact]
    public void L_impronta_dei_reparti_non_dipende_dall_ordine()
    {
        var uno = new SyncDepartmentsRequest
        {
            Departments = { new() { Code = "MEC", Name = "Meccanico", SortOrder = 2 }, new() { Code = "ELE", Name = "Elettrico", SortOrder = 1 } },
            Links = { new() { EmployeeId = 7, DepartmentCode = "MEC" }, new() { EmployeeId = 3, DepartmentCode = "ELE", IsPrimary = true } },
        };
        var due = new SyncDepartmentsRequest
        {
            Departments = { new() { Code = "ELE", Name = "Elettrico", SortOrder = 1 }, new() { Code = "MEC", Name = "Meccanico", SortOrder = 2 } },
            Links = { new() { EmployeeId = 3, DepartmentCode = "ELE", IsPrimary = true }, new() { EmployeeId = 7, DepartmentCode = "MEC" } },
        };
        Assert.Equal(AnagraficheSync.ImprontaReparti(uno), AnagraficheSync.ImprontaReparti(due));

        due.Links[0].IsPrimary = false;
        Assert.NotEqual(AnagraficheSync.ImprontaReparti(uno), AnagraficheSync.ImprontaReparti(due));
    }

    [Fact]
    public void I_dipendenti_da_inviare_credenziali_solo_ai_nuovi_e_mai_ADMIN()
    {
        var dipendenti = new[]
        {
            new DipendentePm { Id = 1, FirstName = "Mario", LastName = "Rossi", EmpType = "INTERNAL", UserRole = "ADMIN", Username = "m.rossi", PasswordHash = "$2a$x" },
            new DipendentePm { Id = 2, FirstName = "Anna", LastName = "Verdi", EmpType = "INTERNAL", UserRole = "TECH", Username = "a.verdi", PasswordHash = "$2a$y" },
            new DipendentePm { Id = 3, FirstName = "Luca", LastName = "Bianchi", EmpType = "EXTERNAL" },
            new DipendentePm { Id = 4, FirstName = "Gino", LastName = "Neri", EmpType = "EXTERNAL" },
        };
        string improntaAnna = AnagraficheSync.ImprontaDipendente(AnagraficheSync.DtoDipendente(dipendenti[1], 20));
        var mappa = new Dictionary<int, RisorseSyncMap.Voce> { [2] = new(20, improntaAnna), [4] = new(40, "vecchia") };

        List<RigaDaInviare<SyncEmployeeDto>> righe = AnagraficheSync.DipendentiDaInviare(dipendenti, mappa, invioCompleto: false);

        // Rossi: nuovo (Id null) con credenziali, ruolo degradato a PM. Verdi: invariata, non parte.
        // Bianchi: esterno non mappato, non è una risorsa. Neri: esterno ma mappato e cambiato, parte senza credenziali.
        Assert.Equal(new[] { 1, 4 }, righe.Select(r => r.LocalId));
        RigaDaInviare<SyncEmployeeDto> rossi = righe[0];
        Assert.Null(rossi.Dto.Id);
        Assert.False(rossi.Mappata);
        Assert.Equal("PM", rossi.Dto.UserRole);
        Assert.Equal("m.rossi", rossi.Dto.Username);
        Assert.Equal("$2a$x", rossi.Dto.PasswordHash);
        RigaDaInviare<SyncEmployeeDto> neri = righe[1];
        Assert.Equal(40, neri.Dto.Id);
        Assert.Null(neri.Dto.UserRole);
        Assert.Null(neri.Dto.Username);
        Assert.Null(neri.Dto.PasswordHash);

        // Invio completo: partono tutti i candidati, anche Verdi.
        Assert.Equal(new[] { 1, 2, 4 }, AnagraficheSync.DipendentiDaInviare(dipendenti, mappa, invioCompleto: true).Select(r => r.LocalId));
    }

    [Fact]
    public void L_invio_completo_scatta_senza_segnalibro_o_dopo_24_ore()
    {
        var adesso = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(AnagraficheSync.ServeInvioCompleto(null, adesso));
        Assert.True(AnagraficheSync.ServeInvioCompleto(adesso.AddHours(-24), adesso));
        Assert.False(AnagraficheSync.ServeInvioCompleto(adesso.AddHours(-23), adesso));
    }
}

/// <summary>
/// Il giro delle anagrafiche da cima a fondo (Fase 1): MySQL di prova, VPS finto che risponde
/// come il vero (login, GET employees, PUT con esiti riga per riga). Le cose da difendere:
/// al primo giro mappa e impronte scritte e payload giusti (Id null e credenziali SOLO per i
/// nuovi); un secondo giro senza cambiamenti NON fa nessuna PUT; una modifica manda una riga
/// sola; uno <c>skipped</c> si conta e non fa fallire il giro.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class GiroAnagraficheTests
{
    private readonly SchemaCondiviso _schema;
    private int _rossi, _anna, _luca, _giulia, _repA, _repB, _commessaAttiva, _commessaChiusa;
    /// <summary>Le commesse ACTIVE nel database di prova (la nostra più quelle che lo schema semina da solo).</summary>
    private int _attive;

    public GiroAnagraficheTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
        using MySqlConnection c = _schema.Apri();
        c.Execute("DELETE FROM res_settings WHERE `key` LIKE 'sync.%'");
        c.Execute("DELETE FROM res_sync_map");
        c.Execute("DELETE FROM res_sync_log");
    }

    // ── Dati di prova ────────────────────────────────────────────

    private void SeminaPm()
    {
        using MySqlConnection c = _schema.Apri();
        _rossi = Dipendente(c, "Mario", "Rossi", "INTERNAL", "TECH", "m.rossi", "$2a$rossi");
        _anna = Dipendente(c, "Anna", "Largana", "INTERNAL", "RESP_REPARTO", "a.largana", "$2a$anna");
        _luca = Dipendente(c, "Luca", "Bianchi", "EXTERNAL", "TECH", null, null);
        _giulia = Dipendente(c, "Giulia", "Verdi", "INTERNAL", "PM", "g.verdi", "$2a$giulia");

        _repA = Inserisci(c, "INSERT INTO departments (code, name, sort_order, is_active) VALUES ('sya', 'Sync A', 1, 1)");
        _repB = Inserisci(c, "INSERT INTO departments (code, name, sort_order, is_active) VALUES ('SYB', 'Sync B', 2, 0)");
        c.Execute("INSERT INTO employee_departments (employee_id, department_id, is_responsible, is_primary) VALUES (@E, @D, 1, 1)", new { E = _rossi, D = _repA });
        c.Execute("INSERT INTO employee_departments (employee_id, department_id, is_responsible, is_primary) VALUES (@E, @D, 0, 1)", new { E = _anna, D = _repB });
        c.Execute("INSERT INTO employee_departments (employee_id, department_id, is_responsible, is_primary) VALUES (@E, @D, 0, 0)", new { E = _luca, D = _repA });

        int cliente = c.ExecuteScalar<int?>("SELECT id FROM customers ORDER BY id LIMIT 1")
            ?? Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = c.ExecuteScalar<int>("SELECT id FROM employees WHERE username = 'admin'");
        _commessaAttiva = Inserisci(c,
            "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20260901.001', 'OSVA upgrade', @C, @P, 'ACTIVE')",
            new { C = cliente, P = pm });
        _commessaChiusa = Inserisci(c,
            "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20250101.001', 'Vecchia', @C, @P, 'COMPLETED')",
            new { C = cliente, P = pm });
        _attive = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE status = 'ACTIVE'");
    }

    private static int Dipendente(MySqlConnection c, string nome, string cognome, string tipo, string ruolo, string? username, string? hash) =>
        Inserisci(c, @"INSERT INTO employees (first_name, last_name, email, emp_type, status, user_role, username, password_hash)
                       VALUES (@N, @C, @E, @T, 'ACTIVE', @R, @U, @H)",
            new { N = nome, C = cognome, E = $"{nome}.{cognome}@atec.it".ToLowerInvariant(), T = tipo, R = ruolo, U = username, H = hash ?? "" });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    /// <summary>
    /// Il VPS di §2 visto dal seme: Anna c'è già (con l'accento), zamputo è solo di là, più gli
    /// account di sistema (admin, [SYNC]) che il GET torna e che il seme deve ignorare. Le
    /// wildcard di reparto («[PM] Generico», …) che lo schema semina in PM NON ci sono: il motore
    /// non le deve mandare.
    /// </summary>
    private static VpsFinto Vps() => new()
    {
        Dipendenti =
        {
            new SyncEmployeeDto { Id = 1, FirstName = "Diego", LastName = "Frattini", Username = "admin", UserRole = "ADMIN", EmpType = "INTERNAL", Status = "ACTIVE" },
            new SyncEmployeeDto { Id = 2, FirstName = "[SYNC]", LastName = "ATEC PM", Username = "sync.pm", UserRole = "SYNC", EmpType = "INTERNAL", Status = "ACTIVE" },
            new SyncEmployeeDto { Id = 10, FirstName = "Anna", LastName = "Larganà", Username = "a.largana", EmpType = "INTERNAL", Status = "ACTIVE" },
            new SyncEmployeeDto { Id = 11, FirstName = "pasquale", LastName = "zamputo", EmpType = "INTERNAL", Status = "TERMINATED" },
        },
    };

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

    private Dictionary<int, RisorseSyncMap.Voce> Mappa(string kind)
    {
        using MySqlConnection c = _schema.Apri();
        return RisorseSyncMap.Carica(c, kind);
    }

    private string? Impostazione(string chiave)
    {
        using MySqlConnection c = _schema.Apri();
        return c.ExecuteScalar<string>("SELECT `value` FROM res_settings WHERE `key` = @K", new { K = chiave });
    }

    // ── I test ───────────────────────────────────────────────────

    [FactRichiedeMySql]
    public async Task Primo_giro_seme_mappa_e_payload_giusti()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("dipendenti 2 creati / 1 aggiornati", voce.Dettaglio);
        Assert.Contains("reparti inviati", voce.Dettaglio);
        Assert.Contains($"commesse {_attive} create", voce.Dettaglio);
        // Fra i «non abbinati PM» solo gli esterni: Rossi e Verdi sono stati creati, non sono «non abbinati».
        Assert.Contains("non abbinati PM: Luca Bianchi (esterno)", voce.Dettaglio);
        string nonAbbinati = voce.Dettaglio[voce.Dettaglio.IndexOf("non abbinati PM:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Mario Rossi", nonAbbinati);
        Assert.DoesNotContain("Giulia Verdi", nonAbbinati);
        // Fra i «solo VPS» solo le persone: né admin né [SYNC].
        Assert.Contains("solo VPS: pasquale zamputo", voce.Dettaglio);
        Assert.DoesNotContain("Frattini", voce.Dettaglio);
        Assert.DoesNotContain("[SYNC]", voce.Dettaglio);

        // Una PUT per tipo, nell'ordine del giro.
        Assert.Equal(new[] { "/api/sync/employees", "/api/sync/departments", "/api/sync/projects" },
            vps.Chiamate.Where(x => x.Metodo == "PUT").Select(x => x.Percorso));

        // Dipendenti: Anna con l'Id del VPS e SENZA credenziali; Rossi e Verdi nuovi (Id null) CON
        // credenziali; Luca (esterno) assente; le wildcard «[PM] Generico»… assenti.
        List<SyncEmployeeDto> inviati = vps.Corpo<List<SyncEmployeeDto>>("/api/sync/employees");
        Assert.Equal(3, inviati.Count);
        Assert.DoesNotContain(inviati, d => d.FirstName.StartsWith('['));
        SyncEmployeeDto rossi = Assert.Single(inviati, d => d.LastName == "Rossi");
        Assert.Null(rossi.Id);
        Assert.Equal("m.rossi", rossi.Username);
        Assert.Equal("$2a$rossi", rossi.PasswordHash);
        Assert.Equal("TECH", rossi.UserRole);
        SyncEmployeeDto anna = Assert.Single(inviati, d => d.LastName == "Largana");
        Assert.Equal(10, anna.Id);
        Assert.Null(anna.Username);
        Assert.Null(anna.PasswordHash);
        Assert.Null(anna.UserRole);
        Assert.DoesNotContain(inviati, d => d.LastName == "Bianchi");

        // Mappa dipendenti: seme (Anna → 10) e creati (Id dal VPS), tutti con l'impronta scritta.
        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa(RisorseSyncMap.Employee);
        Assert.Equal(3, mappa.Count);
        Assert.Equal(10, mappa[_anna].RemoteId);
        Assert.Equal(AnagraficheSync.ImprontaDipendente(anna), mappa[_anna].SyncedHash);
        Assert.Equal(AnagraficheSync.ImprontaDipendente(rossi), mappa[_rossi].SyncedHash);
        Assert.NotEqual(mappa[_rossi].RemoteId, mappa[_giulia].RemoteId);
        Assert.False(mappa.ContainsKey(_luca));

        // Reparti: tutti e due, codice in maiuscolo; legami dei soli mappati (Luca e le wildcard no), EmployeeId = id VPS.
        SyncDepartmentsRequest reparti = vps.Corpo<SyncDepartmentsRequest>("/api/sync/departments");
        Assert.Equal(new[] { "SYA", "SYB" }, reparti.Departments.Select(d => d.Code));
        Assert.False(reparti.Departments[1].IsActive);
        Assert.Equal(2, reparti.Links.Count);
        Assert.Contains(reparti.Links, l => l.EmployeeId == 10 && l.DepartmentCode == "SYB" && l.IsPrimary && !l.IsResponsible);
        Assert.Contains(reparti.Links, l => l.EmployeeId == mappa[_rossi].RemoteId && l.DepartmentCode == "SYA" && l.IsResponsible);
        Assert.Equal(AnagraficheSync.ImprontaReparti(reparti), Impostazione("sync.hash.reparti"));

        // Commesse: solo le ACTIVE (la COMPLETED no), Id null, mappate con l'Id ricevuto.
        List<SyncProjectDto> commesse = vps.Corpo<List<SyncProjectDto>>("/api/sync/projects");
        Assert.Equal(_attive, commesse.Count);
        Assert.All(commesse, p => Assert.Null(p.Id));
        SyncProjectDto attiva = Assert.Single(commesse, p => p.Code == "C20260901.001");
        Assert.DoesNotContain(commesse, p => p.Code == "C20250101.001");
        Dictionary<int, RisorseSyncMap.Voce> mappaCom = Mappa(RisorseSyncMap.Project);
        Assert.Equal(_attive, mappaCom.Count);
        Assert.Equal(AnagraficheSync.ImprontaCommessa(attiva), mappaCom[_commessaAttiva].SyncedHash);
        Assert.False(mappaCom.ContainsKey(_commessaChiusa));

        // Segnalibro dell'invio completo e riga di registro con i contatori.
        Assert.NotNull(RisorseSyncService.ParseLastRun(Impostazione("sync.anagrafiche_full_at")));
        using MySqlConnection c = _schema.Apri();
        var log = c.QuerySingle<(int CreateVps, int AggiornateVps, int Saltate)>(
            "SELECT create_vps, aggiornate_vps, saltate FROM res_sync_log ORDER BY id DESC LIMIT 1");
        Assert.Equal((2 + 2 + _attive, 1, 0), log);   // 2 dipendenti + 2 reparti + commesse creati; 1 dipendente aggiornato
    }

    [FactRichiedeMySql]
    public async Task Secondo_giro_senza_cambiamenti_non_fa_nessuna_PUT()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int putPrima = vps.Put;
        int chiamatePrima = vps.Chiamate.Count;

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Equal(putPrima, vps.Put);
        Assert.Contains("dipendenti 3 invariati", voce.Dettaglio);
        Assert.Contains("reparti invariati", voce.Dettaglio);
        Assert.Contains($"commesse {_attive} invariate", voce.Dettaglio);
        // Luca (esterno, non mappato) non tiene acceso il seme: nessuna GET employees e niente «solo VPS».
        Assert.DoesNotContain(vps.Chiamate.Skip(chiamatePrima), x => x.Metodo == "GET" && x.Percorso.EndsWith("/api/sync/employees"));
        Assert.DoesNotContain("solo VPS", voce.Dettaglio);
        // Giro del timer senza scritture: niente riga nel registro (resta quella del primo giro).
        using MySqlConnection c = _schema.Apri();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_sync_log"));
    }

    [FactRichiedeMySql]
    public async Task Un_legame_cambiato_manda_i_reparti_e_il_timer_lo_registra()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        // Rossi non è più responsabile: cambiano solo i legami, il VPS risponde 0 creati / 0 aggiornati.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE employee_departments SET is_responsible = 0 WHERE employee_id = @E", new { E = _rossi });
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        (string Metodo, string Percorso, string Corpo) put = Assert.Single(vps.Chiamate, x => x.Metodo == "PUT");
        Assert.Equal("/api/sync/departments", put.Percorso);
        Assert.Contains("reparti inviati", voce.Dettaglio);
        SyncDepartmentsRequest reparti = vps.Corpo<SyncDepartmentsRequest>("/api/sync/departments");
        Assert.Contains(reparti.Links, l => l.DepartmentCode == "SYA" && !l.IsResponsible);
        // Il giro HA scritto sul VPS anche se i contatori del VPS dicono 0/0: la riga di registro c'è.
        using MySqlConnection c2 = _schema.Apri();
        Assert.Equal(2, c2.ExecuteScalar<int>("SELECT COUNT(*) FROM res_sync_log"));
    }

    [FactRichiedeMySql]
    public async Task Il_cambio_di_un_cognome_manda_una_riga_sola()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int remotoRossi = Mappa(RisorseSyncMap.Employee)[_rossi].RemoteId;
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE employees SET last_name = 'Rossini' WHERE id = @Id", new { Id = _rossi });
        vps.Chiamate.Clear();

        RisorseSyncLogEntry voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        (string Metodo, string Percorso, string Corpo) put = Assert.Single(vps.Chiamate, x => x.Metodo == "PUT");
        Assert.Equal("/api/sync/employees", put.Percorso);
        SyncEmployeeDto riga = Assert.Single(vps.Corpo<List<SyncEmployeeDto>>("/api/sync/employees"));
        Assert.Equal(remotoRossi, riga.Id);
        Assert.Equal("Rossini", riga.LastName);
        Assert.Null(riga.PasswordHash);   // già sul VPS: le credenziali non viaggiano più
        Assert.Contains("dipendenti 1 aggiornati / 2 invariati", voce.Dettaglio);
        Assert.Equal(AnagraficheSync.ImprontaDipendente(riga), Mappa(RisorseSyncMap.Employee)[_rossi].SyncedHash);
    }

    [FactRichiedeMySql]
    public async Task Uno_skipped_si_conta_e_non_fa_fallire_il_giro()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        vps.DaSaltare.Add("Verdi");
        RisorseSyncService svc = Servizio(vps);

        RisorseSyncLogEntry voce = await svc.RunNowAsync("manuale");

        Assert.Equal("ok", voce.Esito);
        Assert.Contains("1 saltati", voce.Dettaglio);
        Assert.Contains("dipendente Giulia Verdi: EmployeeId inesistente", voce.Dettaglio);
        Dictionary<int, RisorseSyncMap.Voce> mappa = Mappa(RisorseSyncMap.Employee);
        Assert.False(mappa.ContainsKey(_giulia));
        Assert.True(mappa.ContainsKey(_rossi));
        using MySqlConnection c = _schema.Apri();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT saltate FROM res_sync_log ORDER BY id DESC LIMIT 1"));
        // Il pannello continua a vederla anche a giro «ok».
        Assert.Contains("Giulia Verdi", svc.LastError);
        Assert.Contains("Giulia Verdi", Impostazione("sync.last_error"));
    }

    [FactRichiedeMySql]
    public async Task Una_riga_gia_saltata_non_riparte_a_ogni_giro_del_timer()
    {
        SeminaPm();
        VpsFinto vps = Vps();
        vps.DaSaltare.Add("Verdi");
        RisorseSyncService svc = Servizio(vps);
        await svc.RunNowAsync("manuale");
        int putPrima = vps.Put;

        // Secondo giro (timer), stesso dato: Giulia NON si rimanda, niente PUT, niente riga nuova nel registro,
        // e nemmeno la GET del seme: un interno rifiutato stabilmente non tiene acceso l'abbinamento.
        int chiamatePrima = vps.Chiamate.Count;
        RisorseSyncLogEntry voce = await svc.RunNowAsync("timer");

        Assert.Equal("ok", voce.Esito);
        Assert.Equal(putPrima, vps.Put);
        Assert.DoesNotContain(vps.Chiamate.Skip(chiamatePrima), x => x.Metodo == "GET" && x.Percorso.EndsWith("/api/sync/employees"));
        Assert.Contains("saltate (già segnalate): 1", voce.Dettaglio);
        Assert.DoesNotContain("1 saltati", voce.Dettaglio);
        Assert.Contains("Giulia Verdi", svc.LastError);
        using (MySqlConnection c = _schema.Apri())
            Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM res_sync_log"));

        // «Sincronizza adesso» dal pannello riprova anche le righe già saltate: una PUT, ancora rifiutata.
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("manuale");
        Assert.Single(vps.Chiamate, x => x.Metodo == "PUT" && x.Percorso == "/api/sync/employees");
        Assert.Contains("1 saltati", voce.Dettaglio);

        // Il dato cambia (e stavolta il VPS la accetta): riparte da sola, una riga sola, e finisce in mappa.
        using (MySqlConnection c = _schema.Apri())
            c.Execute("UPDATE employees SET last_name = 'Verdini' WHERE id = @Id", new { Id = _giulia });
        vps.Chiamate.Clear();
        voce = await svc.RunNowAsync("pm");

        Assert.Equal("ok", voce.Esito);
        (string Metodo, string Percorso, string Corpo) put = Assert.Single(vps.Chiamate, x => x.Metodo == "PUT");
        Assert.Equal("/api/sync/employees", put.Percorso);
        SyncEmployeeDto riga = Assert.Single(vps.Corpo<List<SyncEmployeeDto>>("/api/sync/employees"));
        Assert.Equal("Verdini", riga.LastName);
        Assert.True(Mappa(RisorseSyncMap.Employee).ContainsKey(_giulia));
        Assert.DoesNotContain("già segnalate", voce.Dettaglio);
        Assert.Null(svc.LastError);
    }

    /// <summary>
    /// Il VPS finto: login, stato, GET employees dall'elenco dato, PUT con esiti riga per riga
    /// (Id null → <c>created</c> con un id nuovo; Id presente → <c>updated</c>; i cognomi in
    /// <see cref="DaSaltare"/> → <c>skipped</c>). I reparti li ricorda: come il VPS vero risponde
    /// <c>Created</c>/<c>Updated</c> solo per quelli nuovi o cambiati, 0/0 se cambiano solo i
    /// legami. Tiene ogni chiamata col suo corpo.
    /// </summary>
    private sealed class VpsFinto : HttpMessageHandler
    {
        private const string LoginOk = "{\"success\":true,\"data\":{\"token\":\"jwt-finto\",\"employeeId\":1,\"fullName\":\"[SYNC] ATEC PM\",\"userRole\":\"SYNC\"},\"message\":\"\"}";
        private const string StatoOk = "{\"success\":true,\"data\":{\"serverUtc\":\"2026-09-02T10:00:00Z\",\"employees\":2,\"projects\":0,\"departments\":0,\"assignments\":0,\"version\":\"1.4.0\"},\"message\":\"\"}";

        public List<SyncEmployeeDto> Dipendenti { get; } = new();
        public HashSet<string> DaSaltare { get; } = new();
        public List<(string Metodo, string Percorso, string Corpo)> Chiamate { get; } = new();
        private int _prossimoId = 100;
        /// <summary>I reparti già sul VPS: codice → firma dei campi.</summary>
        private readonly Dictionary<string, string> _reparti = new();

        public int Put => Chiamate.Count(x => x.Metodo == "PUT");

        /// <summary>Il corpo dell'ULTIMA PUT su quel percorso, deserializzato come lo legge il VPS.</summary>
        public T Corpo<T>(string percorso) =>
            JsonSerializer.Deserialize<T>(Chiamate.Last(x => x.Metodo == "PUT" && x.Percorso == percorso).Corpo, RisorseSyncClient.JsonOptions)!;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string corpo = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            string percorso = request.RequestUri!.AbsolutePath;
            Chiamate.Add((request.Method.Method, percorso, corpo));

            if (percorso.EndsWith("/api/auth/login")) return Json(LoginOk);
            if (percorso.EndsWith("/api/sync/status")) return Json(StatoOk);
            if (request.Method == HttpMethod.Get && percorso.EndsWith("/api/sync/employees"))
                return Ok(Dipendenti);
            if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/employees"))
                return Ok(Esiti(JsonSerializer.Deserialize<List<SyncEmployeeDto>>(corpo, RisorseSyncClient.JsonOptions)!,
                    d => d.Id, d => DaSaltare.Contains(d.LastName)));
            if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/departments"))
            {
                SyncDepartmentsRequest r = JsonSerializer.Deserialize<SyncDepartmentsRequest>(corpo, RisorseSyncClient.JsonOptions)!;
                int creati = 0, aggiornati = 0, invariati = 0;
                foreach (SyncDepartmentDto d in r.Departments)
                {
                    string firma = $"{d.Name}|{d.SortOrder}|{d.IsActive}";
                    if (!_reparti.TryGetValue(d.Code, out string? prima)) creati++;
                    else if (prima != firma) aggiornati++;
                    else invariati++;
                    _reparti[d.Code] = firma;
                }
                return Ok(new SyncCountsDto { Created = creati, Updated = aggiornati, Unchanged = invariati, Links = r.Links.Count });
            }
            if (request.Method == HttpMethod.Put && percorso.EndsWith("/api/sync/projects"))
                return Ok(Esiti(JsonSerializer.Deserialize<List<SyncProjectDto>>(corpo, RisorseSyncClient.JsonOptions)!,
                    p => p.Id, _ => false));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private List<SyncUpsertResultDto> Esiti<T>(List<T> righe, Func<T, int?> id, Func<T, bool> salta)
        {
            var esiti = new List<SyncUpsertResultDto>();
            for (int i = 0; i < righe.Count; i++)
            {
                if (salta(righe[i]))
                    esiti.Add(new SyncUpsertResultDto { Index = i, Action = "skipped", Error = "EmployeeId inesistente" });
                else if (id(righe[i]) is int esistente)
                    esiti.Add(new SyncUpsertResultDto { Index = i, Id = esistente, Action = "updated" });
                else
                    esiti.Add(new SyncUpsertResultDto { Index = i, Id = _prossimoId++, Action = "created" });
            }
            return esiti;
        }

        private static HttpResponseMessage Ok<T>(T data) =>
            Json(JsonSerializer.Serialize(ApiResponse<T>.Ok(data), RisorseSyncClient.JsonOptions));

        private static HttpResponseMessage Json(string corpo) =>
            new(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
    }
}
