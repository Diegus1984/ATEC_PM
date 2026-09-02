using System.Net;
using System.Text;
using System.Text.Json;
using ATEC.PM.Server.Migrations;
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
/// Il contratto JSON con il VPS (PIANO-SYNC-RISORSE.md §4.4), letto e scritto con le stesse
/// opzioni del client. Le cose da difendere: <c>UpdatedAt</c> è UTC e deve <b>restare</b> UTC
/// dopo un giro completo (Kind e valore) — il confronto «vince l'ultima modifica» di §4.3 si
/// regge su quello, e un DateTime che torna Unspecified verrebbe riletto nel fuso del server.
/// Niente database: sono test puri.
/// </summary>
public class RisorseSyncContrattoTests
{
    private static T Giro<T>(T oggetto)
    {
        string json = JsonSerializer.Serialize(oggetto, RisorseSyncClient.JsonOptions);
        return JsonSerializer.Deserialize<T>(json, RisorseSyncClient.JsonOptions)!;
    }

    [Fact]
    public void Allocazione_UpdatedAt_UTC_resta_UTC_dopo_il_giro()
    {
        var quando = new DateTime(2026, 9, 2, 14, 30, 15, DateTimeKind.Utc);
        var originale = new SyncAssignmentDto
        {
            Id = 182, EmployeeId = 36, Tipo = "FLEX",
            DataInizio = new DateOnly(2026, 9, 7), DataFine = new DateOnly(2026, 9, 11),
            Descrizione = "C260402_202 -OSVA UPGRADE", UpdatedBy = 12, UpdatedAt = quando, CreatedAt = quando,
        };

        string json = JsonSerializer.Serialize(originale, RisorseSyncClient.JsonOptions);
        // camelCase e la Z dell'UTC: è così che il VPS li legge e li scrive.
        Assert.Contains("\"updatedAt\":\"2026-09-02T14:30:15Z\"", json);
        Assert.Contains("\"employeeId\":36", json);
        // Le date-solo sono DateOnly: nel JSON solo 'yyyy-MM-dd', niente ora né fuso.
        Assert.Contains("\"dataInizio\":\"2026-09-07\"", json);
        Assert.Contains("\"dataFine\":\"2026-09-11\"", json);

        SyncAssignmentDto riletto = JsonSerializer.Deserialize<SyncAssignmentDto>(json, RisorseSyncClient.JsonOptions)!;
        Assert.Equal(DateTimeKind.Utc, riletto.UpdatedAt!.Value.Kind);
        Assert.Equal(quando, riletto.UpdatedAt);
        Assert.Equal(quando, riletto.CreatedAt);
        Assert.Equal(originale.DataInizio, riletto.DataInizio);
        Assert.Equal(originale.DataFine, riletto.DataFine);
        Assert.Equal("FLEX", riletto.Tipo);
        Assert.Equal(originale.Descrizione, riletto.Descrizione);
        Assert.Equal(12, riletto.UpdatedBy);
    }

    [Fact]
    public void Upsert_con_Id_nullo_e_UpdatedAt_nullo_viaggiano_come_null()
    {
        var riga = new SyncAssignmentUpsertDto
        {
            Id = null, EmployeeId = 5, Tipo = "FERIE",
            DataInizio = new DateOnly(2026, 8, 10), DataFine = new DateOnly(2026, 8, 21),
            UpdatedBy = null, UpdatedAt = null,
        };

        string json = JsonSerializer.Serialize(riga, RisorseSyncClient.JsonOptions);
        Assert.Contains("\"dataInizio\":\"2026-08-10\"", json);
        Assert.Contains("\"dataFine\":\"2026-08-21\"", json);

        SyncAssignmentUpsertDto riletta = Giro(riga);
        Assert.Null(riletta.Id);
        Assert.Equal(new DateOnly(2026, 8, 10), riletta.DataInizio);
        Assert.Equal(new DateOnly(2026, 8, 21), riletta.DataFine);
        Assert.Null(riletta.UpdatedAt);
        Assert.Null(riletta.UpdatedBy);
        Assert.Equal(5, riletta.EmployeeId);
        Assert.Equal("FERIE", riletta.Tipo);
    }

    [Fact]
    public void Lettura_case_insensitive_accetta_PascalCase_dal_VPS()
    {
        // Se un giorno il VPS serializzasse in PascalCase, il client non deve restare a mani vuote.
        const string json = "{\"Success\":true,\"Data\":{\"ServerUtc\":\"2026-09-02T10:00:00Z\",\"Employees\":38,\"Projects\":15,\"Departments\":13,\"Assignments\":182,\"Version\":\"1.4.0\"},\"Message\":\"\"}";

        ApiResponse<SyncStatusDto> api = JsonSerializer.Deserialize<ApiResponse<SyncStatusDto>>(json, RisorseSyncClient.JsonOptions)!;
        Assert.True(api.Success);
        Assert.Equal(182, api.Data!.Assignments);
        Assert.Equal(38, api.Data.Employees);
        Assert.Equal(DateTimeKind.Utc, api.Data.ServerUtc.Kind);
    }

    [Fact]
    public void Anagrafiche_fanno_il_giro_senza_perdere_niente()
    {
        var reparti = new SyncDepartmentsRequest
        {
            Departments = { new SyncDepartmentDto { Code = "MEC", Name = "Meccanico", SortOrder = 3, IsActive = true } },
            Links = { new SyncEmployeeDepartmentDto { EmployeeId = 7, DepartmentCode = "MEC", IsResponsible = true, IsPrimary = true } },
        };
        SyncDepartmentsRequest r = Giro(reparti);
        Assert.Single(r.Departments);
        Assert.Equal("MEC", r.Departments[0].Code);
        Assert.Single(r.Links);
        Assert.True(r.Links[0].IsResponsible);

        var dip = new SyncEmployeeDto { Id = null, FirstName = "Mario", LastName = "Rossi", Email = null, EmpType = "INTERNAL", Status = "ACTIVE", UserRole = "TECH", Username = "m.rossi", PasswordHash = "$2a$…" };
        SyncEmployeeDto d = Giro(dip);
        Assert.Null(d.Id);
        Assert.Equal("m.rossi", d.Username);
        Assert.Equal("$2a$…", d.PasswordHash);

        var esiti = Giro(new List<SyncUpsertResultDto>
        {
            new() { Index = 0, Id = 41, Action = "created" },
            new() { Index = 1, Id = null, Action = "skipped", Error = "EmployeeId 99 inesistente" },
        });
        Assert.Equal(2, esiti.Count);
        Assert.Equal("skipped", esiti[1].Action);
        Assert.Equal("EmployeeId 99 inesistente", esiti[1].Error);

        SyncDeleteRequest del = Giro(new SyncDeleteRequest { Ids = { 1, 2, 3 }, MadeBy = null });
        Assert.Equal(new[] { 1, 2, 3 }, del.Ids);
        Assert.Null(del.MadeBy);

        SyncProjectDto p = Giro(new SyncProjectDto { Id = 9, Code = "C20260402.202", Title = "OSVA upgrade", Status = "ACTIVE" });
        Assert.Equal("C20260402.202", p.Code);
    }

    [Fact]
    public void I_default_delle_anagrafiche_sono_quelli_del_VPS()
    {
        // Un dipendente o una commessa mandati senza specificare lo stato nascono ATTIVI, come di là.
        Assert.Equal("INTERNAL", new SyncEmployeeDto().EmpType);
        Assert.Equal("ACTIVE", new SyncEmployeeDto().Status);
        Assert.Equal("ACTIVE", new SyncProjectDto().Status);
    }
}

/// <summary>
/// Il client HTTP davanti a un VPS che NON risponde come dovrebbe: indirizzo sbagliato (una
/// pagina HTML), proxy giù (502), account senza ruolo (403), password sbagliata (401). Ogni
/// caso deve diventare una frase per il pannello, e dopo un 401 al login il client non deve
/// più bussare al VPS (limite di login per IP). Niente rete: l'HttpClient ha un handler finto.
/// </summary>
public class RisorseSyncClientTests
{
    private static readonly RisorseSyncSettings Impostazioni =
        new(true, "https://vps.esempio/", "sync.pm", "segreta", null, null, null);

    private const string LoginOk = "{\"success\":true,\"data\":{\"token\":\"jwt-finto\",\"employeeId\":1,\"fullName\":\"Sync PM\",\"userRole\":\"SYNC\"},\"message\":\"\"}";

    private static RisorseSyncClient Client(HandlerFinto handler) =>
        new(Impostazioni, NullLogger.Instance, new HttpClient(handler));

    private static HttpResponseMessage Json(HttpStatusCode stato, string corpo) =>
        new(stato) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Html(HttpStatusCode stato, string corpo = "<html><body>Benvenuti</body></html>") =>
        new(stato) { Content = new StringContent(corpo, Encoding.UTF8, "text/html") };

    [Fact]
    public async Task Un_403_a_corpo_vuoto_dice_che_manca_il_ruolo()
    {
        var handler = new HandlerFinto(req => req.RequestUri!.AbsolutePath.EndsWith("/api/auth/login")
            ? Json(HttpStatusCode.OK, LoginOk)
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<RisorseSyncException>(() => Client(handler).GetStatusAsync());
        Assert.Equal("L'account di servizio sul VPS non ha il ruolo richiesto (SYNC o ADMIN)", ex.Message);
        Assert.Equal(2, handler.Chiamate); // login + status
    }

    [Fact]
    public async Task Un_200_html_dice_che_l_indirizzo_non_e_l_API()
    {
        // L'indirizzo punta a un sito qualunque: risponde 200 con una pagina.
        var handler = new HandlerFinto(_ => Html(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<RisorseSyncException>(() => Client(handler).LoginAsync());
        Assert.Equal("L'indirizzo del VPS non risponde con l'API (controllare l'indirizzo)", ex.Message);
    }

    [Fact]
    public async Task Un_502_html_riporta_il_codice_e_il_percorso()
    {
        var handler = new HandlerFinto(req => req.RequestUri!.AbsolutePath.EndsWith("/api/auth/login")
            ? Json(HttpStatusCode.OK, LoginOk)
            : Html(HttpStatusCode.BadGateway, "<html><h1>502 Bad Gateway</h1><hr>nginx</html>"));

        var ex = await Assert.ThrowsAsync<RisorseSyncException>(() => Client(handler).GetStatusAsync());
        Assert.Equal("Il VPS ha risposto HTTP 502 senza JSON su /api/sync/status", ex.Message);
    }

    [Fact]
    public async Task Un_404_dice_di_controllare_l_indirizzo()
    {
        var handler = new HandlerFinto(req => req.RequestUri!.AbsolutePath.EndsWith("/api/auth/login")
            ? Json(HttpStatusCode.OK, LoginOk)
            : Html(HttpStatusCode.NotFound, "Not Found"));

        var ex = await Assert.ThrowsAsync<RisorseSyncException>(() => Client(handler).GetStatusAsync());
        Assert.Equal("Percorso non trovato sul VPS (/api/sync/status): controllare l'indirizzo", ex.Message);
    }

    [Fact]
    public async Task Un_401_al_login_alza_il_freno_e_la_seconda_chiamata_non_tocca_la_rete()
    {
        var handler = new HandlerFinto(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        RisorseSyncClient client = Client(handler);

        var prima = await Assert.ThrowsAsync<RisorseSyncException>(() => client.LoginAsync());
        Assert.Equal("Credenziali rifiutate dal VPS: correggere utente o password nella scheda Sincronizzazione", prima.Message);
        Assert.True(client.CredenzialiRifiutate);
        Assert.Equal(1, handler.Chiamate);

        // Da qui in poi: stesso errore, subito, senza chiamare il VPS (né login né chiamate del contratto).
        var seconda = await Assert.ThrowsAsync<RisorseSyncException>(() => client.GetStatusAsync());
        Assert.Equal(prima.Message, seconda.Message);
        await Assert.ThrowsAsync<RisorseSyncException>(() => client.TokenAsync());
        await Assert.ThrowsAsync<RisorseSyncException>(() => client.LoginAsync());
        Assert.Equal(1, handler.Chiamate);

        // Impostazioni nuove = client nuovo = freno tolto: il servizio lo rifà, non lo riusa.
        Assert.True(client.StesseImpostazioni(Impostazioni));
        Assert.False(client.StesseImpostazioni(Impostazioni with { Password = "corretta" }));
    }

    [Fact]
    public async Task Con_il_JSON_giusto_il_token_si_riusa_e_lo_stato_arriva()
    {
        const string statoOk = "{\"success\":true,\"data\":{\"serverUtc\":\"2026-09-02T10:00:00Z\",\"employees\":38,\"projects\":15,\"departments\":13,\"assignments\":182,\"version\":\"1.4.0\"},\"message\":\"\"}";
        var handler = new HandlerFinto(req => req.RequestUri!.AbsolutePath.EndsWith("/api/auth/login")
            ? Json(HttpStatusCode.OK, LoginOk)
            : Json(HttpStatusCode.OK, statoOk));
        RisorseSyncClient client = Client(handler);

        SyncStatusDto s1 = await client.GetStatusAsync();
        SyncStatusDto s2 = await client.GetStatusAsync();
        Assert.Equal(182, s1.Assignments);
        Assert.Equal(38, s2.Employees);
        Assert.Equal("jwt-finto", client.Token);
        Assert.Equal(3, handler.Chiamate); // un login solo, poi due status
        Assert.All(handler.Bearer.Skip(1), b => Assert.Equal("jwt-finto", b));
    }

    /// <summary>Handler che risponde con la funzione data e conta le chiamate (e i Bearer visti).</summary>
    private sealed class HandlerFinto : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _rispondi;
        public int Chiamate { get; private set; }
        public List<string?> Bearer { get; } = new();

        public HandlerFinto(Func<HttpRequestMessage, HttpResponseMessage> rispondi) => _rispondi = rispondi;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chiamate++;
            Bearer.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(_rispondi(request));
        }
    }
}

/// <summary>Le regole pure del motore: indirizzo del VPS, password mai nei log, ore in UTC verso il pannello.</summary>
public class RisorseSyncRegoleTests
{
    [Theory]
    [InlineData("https://178-32-137-221.sslip.io")]
    [InlineData("https://vps.esempio/")]
    [InlineData("http://localhost:5200")]
    [InlineData("http://127.0.0.1:5200")]
    [InlineData("http://10.8.0.3")]
    [InlineData("http://192.168.2.150:5150")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.254")]
    [InlineData("")]
    [InlineData("   ")]
    public void Indirizzi_buoni(string url) =>
        Assert.Null(RisorseSyncSettings.ErroreIndirizzo(url));

    [Theory]
    [InlineData("http://178-32-137-221.sslip.io", "Verso Internet serve https")]
    [InlineData("http://172.32.0.1", "Verso Internet serve https")]
    [InlineData("http://11.0.0.1", "Verso Internet serve https")]
    [InlineData("http://vps.esempio", "Verso Internet serve https")]
    [InlineData("vps.esempio", "Indirizzo del VPS non valido")]
    [InlineData("ftp://vps.esempio", "Indirizzo del VPS non valido")]
    [InlineData("/api/sync", "Indirizzo del VPS non valido")]
    public void Indirizzi_cattivi(string url, string inizioErrore)
    {
        string? errore = RisorseSyncSettings.ErroreIndirizzo(url);
        Assert.NotNull(errore);
        Assert.StartsWith(inizioErrore, errore);
        // Il messaggio d'esempio non contiene l'indirizzo vero del VPS.
        Assert.DoesNotContain("sslip", errore);
    }

    [Fact]
    public void Il_ToString_delle_impostazioni_non_stampa_la_password()
    {
        var s = new RisorseSyncSettings(true, "https://vps.esempio", "sync.pm", "segretissima", "2026-09-02 12:00:05", "ok", null);
        string testo = s.ToString();
        Assert.DoesNotContain("segretissima", testo);
        Assert.Contains("Password = ***", testo);
        Assert.Contains("sync.pm", testo);
        Assert.Contains("https://vps.esempio", testo);
    }

    [Fact]
    public void Sync_last_run_torna_UTC_e_nel_JSON_porta_la_Z()
    {
        DateTime? lastRun = RisorseSyncService.ParseLastRun("2026-09-02 12:00:05");
        Assert.NotNull(lastRun);
        Assert.Equal(DateTimeKind.Utc, lastRun!.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 2, 12, 0, 5, DateTimeKind.Utc), lastRun);
        Assert.Null(RisorseSyncService.ParseLastRun(null));
        Assert.Null(RisorseSyncService.ParseLastRun("ieri"));

        var stato = new RisorseSyncStatusDto
        {
            LastRun = lastRun,
            UltimiGiri = { new RisorseSyncLogEntry { RunUtc = lastRun.Value, Innesco = "manuale", Esito = "ok" } },
        };
        string json = JsonSerializer.Serialize(stato, RisorseSyncClient.JsonOptions);
        Assert.Contains("\"lastRun\":\"2026-09-02T12:00:05Z\"", json);
        Assert.Contains("\"runUtc\":\"2026-09-02T12:00:05Z\"", json);
    }
}

/// <summary>
/// Le impostazioni del motore in <c>res_settings</c> (chiavi <c>sync.*</c>), come per SMTP ed
/// Ecos: la password non torna mai indietro, salvare a vuoto non la cancella, sul database sta
/// cifrata, e senza niente nel database vale l'<c>appsettings</c>.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class RisorseSyncImpostazioniTests
{
    private readonly SchemaCondiviso _schema;

    public RisorseSyncImpostazioniTests(SchemaCondiviso schema)
    {
        _schema = schema;
        using MySqlConnection c = _schema.Apri();
        c.Execute("DELETE FROM res_settings WHERE `key` LIKE 'sync.%'");
    }

    private RisorseSyncSettingsStore Store(IConfiguration? config = null) =>
        new(new ResourcesDbService(_schema.Servizio()),
            config ?? new ConfigurationBuilder().Build(),
            NullLogger.Instance);

    [FactRichiedeMySql]
    public void Il_dto_non_restituisce_mai_la_password_e_HasPassword_dice_se_ce()
    {
        RisorseSyncSettingsStore store = Store();

        RisorseSyncSettingsDto prima = store.LeggiDto();
        Assert.False(prima.HasPassword);
        Assert.Null(prima.Password);
        Assert.False(prima.Enabled);

        store.Salva(new RisorseSyncSettingsDto
        {
            Enabled = true, BaseUrl = "https://vps.esempio/", Username = "sync.pm", Password = "segreta",
        });

        RisorseSyncSettingsDto dopo = store.LeggiDto();
        Assert.True(dopo.HasPassword);
        Assert.Null(dopo.Password);
        Assert.True(dopo.Enabled);
        Assert.Equal("sync.pm", dopo.Username);
        // Lo slash finale se ne va al salvataggio: i percorsi si attaccano con «/api/...».
        Assert.Equal("https://vps.esempio", dopo.BaseUrl);

        // In memoria invece la password c'è, ed è quella: il client HTTP la usa per il login.
        RisorseSyncSettings s = store.Leggi();
        Assert.Equal("segreta", s.Password);
        Assert.True(s.IsConfigured);
    }

    [FactRichiedeMySql]
    public void Salvare_con_password_vuota_conserva_quella_esistente()
    {
        RisorseSyncSettingsStore store = Store();
        store.Salva(new RisorseSyncSettingsDto { Enabled = false, BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "segreta" });

        // Secondo salvataggio senza toccare la password: cambia solo l'utente.
        store.Salva(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm2", Password = "" });

        RisorseSyncSettings s = store.Leggi();
        Assert.Equal("sync.pm2", s.Username);
        Assert.Equal("segreta", s.Password);
        Assert.True(store.LeggiDto().HasPassword);

        // Anche con null (il client web non manda il campo se non lo tocca).
        store.Salva(new RisorseSyncSettingsDto { Enabled = true, BaseUrl = "https://vps.esempio", Username = "sync.pm2", Password = null });
        Assert.Equal("segreta", store.Leggi().Password);
    }

    [FactRichiedeMySql]
    public void La_password_sul_database_e_cifrata()
    {
        Store().Salva(new RisorseSyncSettingsDto { BaseUrl = "https://vps.esempio", Username = "sync.pm", Password = "segretissima" });

        using MySqlConnection c = _schema.Apri();
        string? salvata = c.ExecuteScalar<string>("SELECT `value` FROM res_settings WHERE `key` = 'sync.password'");

        Assert.False(string.IsNullOrEmpty(salvata));
        Assert.DoesNotContain("segretissima", salvata);
    }

    [FactRichiedeMySql]
    public void Senza_niente_nel_database_vale_appsettings_e_non_e_configurato_da_spento()
    {
        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RisorseSync:Enabled"] = "false",
            ["RisorseSync:BaseUrl"] = "https://dal-file.esempio/",
            ["RisorseSync:Username"] = "sync.pm",
            ["RisorseSync:Password"] = "dal-file",
        }).Build();

        RisorseSyncSettings s = Store(cfg).Leggi();
        Assert.Equal("sync.pm", s.Username);
        Assert.Equal("dal-file", s.Password);
        Assert.Equal("https://dal-file.esempio", s.BaseUrlNormalizzato);
        Assert.True(s.HasCredentials);   // il «Prova» può girare…
        Assert.False(s.IsConfigured);    // …ma il motore no: è spento
        Assert.True(Store(cfg).LeggiDto().HasPassword);
    }

    [FactRichiedeMySql]
    public void L_esito_dell_ultimo_giro_finisce_nelle_impostazioni()
    {
        RisorseSyncSettingsStore store = Store();
        var quando = new DateTime(2026, 9, 2, 12, 0, 5, DateTimeKind.Utc);

        store.ScriviEsito(quando, "errore", "VPS non raggiungibile");

        RisorseSyncSettingsDto dto = store.LeggiDto();
        Assert.Equal("2026-09-02 12:00:05", dto.LastRun);
        Assert.Equal("errore", dto.LastEsito);
        Assert.Equal("VPS non raggiungibile", dto.LastError);
    }
}

/// <summary>
/// La M119: esiste con quel numero, crea le due tabelle e si può rifare (una migrazione
/// fallita viene ritentata al riavvio, su uno schema che magari le ha già).
/// </summary>
public class RisorseSyncMigrazioneTests
{
    [Fact]
    public void La_M119_e_scoperta_dal_runner_con_versione_119()
    {
        IMigrazione? m = new MigrationRunner(NullLogger.Instance).Migrazioni.SingleOrDefault(x => x.Versione == 119);
        Assert.NotNull(m);
        Assert.IsType<M119_SyncRisorse>(m);
        Assert.Contains("res_sync_map", m!.Descrizione);
    }

    [FactRichiedeMySql]
    public void La_M119_crea_le_due_tabelle_e_si_puo_rifare()
    {
        using var db = new DatabaseDiProva("sync119");
        db.CreaSchemaCompleto(); // qui la M119 è già passata
        Assert.Contains(119, db.VersioniApplicate());
        using MySqlConnection c = db.Apri();

        // ── si torna alla forma pre-M119 ──
        c.Execute("DROP TABLE IF EXISTS res_sync_map");
        c.Execute("DROP TABLE IF EXISTS res_sync_log");
        Assert.Equal(0, TabellePresenti(c));

        // ── la migrazione ──
        new M119_SyncRisorse().Applica(c, NullLogger.Instance);

        Assert.Equal(2, TabellePresenti(c));
        // Le due UNIQUE della mappa: un id per lato, mai in due coppie.
        Assert.Equal(2, c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'res_sync_map'
              AND index_name IN ('uq_sync_map_local', 'uq_sync_map_remote')"));
        Assert.Equal(1, c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'res_sync_log'
              AND index_name = 'idx_sync_log_run'"));

        // Le colonne del registro che il motore scrive.
        Assert.Equal(16, c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'res_sync_log'"));

        // ── e la si può rifare: nessuna eccezione, niente di doppio ──
        new M119_SyncRisorse().Applica(c, NullLogger.Instance);
        Assert.Equal(2, TabellePresenti(c));

        // La mappa rifiuta davvero una coppia doppia.
        c.Execute("INSERT INTO res_sync_map (kind, local_id, remote_id) VALUES ('EMPLOYEE', 38, 36)");
        Assert.ThrowsAny<MySqlException>(() =>
            c.Execute("INSERT INTO res_sync_map (kind, local_id, remote_id) VALUES ('EMPLOYEE', 1, 36)"));
    }

    private static int TabellePresenti(MySqlConnection c) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name IN ('res_sync_map', 'res_sync_log')");
}
