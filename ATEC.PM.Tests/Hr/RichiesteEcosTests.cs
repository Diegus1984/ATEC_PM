using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>Le regole pure delle richieste verso Ecos (#151): causali e fascia oraria.</summary>
public class RichiesteEcosRegoleTests
{
    private static readonly DateTime Giorno = new(2026, 2, 5);

    [Theory]
    [InlineData("VACATION", "F")]
    [InlineData("PERMIT", "P")]
    [InlineData("SICKNESS", "M")]
    [InlineData("INJURY", "I1")]
    [InlineData("OTHER", "F_ND")]
    [InlineData("", "F_ND")]
    public void La_causale_di_Ecos_si_prende_dal_nostro_tipo(string tipo, string atteso)
    {
        Assert.Equal(atteso, HrAttendanceService.CategoriaEcos(tipo));
    }

    [Fact]
    public void Senza_timbrature_il_buco_parte_dalle_8()
    {
        var fascia = HrAttendanceService.FasciaDaTimbrature(4m, Array.Empty<(DateTime, string)>());
        Assert.Equal((new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0)), fascia);
    }

    [Fact]
    public void Con_la_mattina_libera_il_buco_sta_prima_della_prima_timbratura()
    {
        // Entrata alle 12:58 (→ 13:00) e uscita alle 17:02 (→ 17:00): le 4 ore mancano al mattino.
        var fascia = HrAttendanceService.FasciaDaTimbrature(4m, new[]
        {
            (Giorno.AddHours(12).AddMinutes(58), "IN"),
            (Giorno.AddHours(17).AddMinutes(2), "OUT"),
        });
        Assert.Equal((new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0)), fascia);
    }

    [Fact]
    public void Altrimenti_il_buco_sta_dopo_l_ultima_timbratura()
    {
        // Entrata 07:58 (→ 08:00), uscita 12:32 (→ 12:30): le 3,5 ore mancano al pomeriggio.
        var fascia = HrAttendanceService.FasciaDaTimbrature(3.5m, new[]
        {
            (Giorno.AddHours(7).AddMinutes(58), "IN"),
            (Giorno.AddHours(12).AddMinutes(32), "OUT"),
        });
        Assert.Equal((new TimeSpan(12, 30, 0), new TimeSpan(16, 0, 0)), fascia);
    }

    [Fact]
    public void Un_buco_che_sfora_la_giornata_non_si_manda()
    {
        // Entrata 07:58 (→ 08:00), uscita 18:00: 8 ore dopo le 18 finirebbero domani.
        var fascia = HrAttendanceService.FasciaDaTimbrature(8m, new[]
        {
            (Giorno.AddHours(7).AddMinutes(58), "IN"),
            (Giorno.AddHours(18), "OUT"),
        });
        Assert.Equal((null, null), fascia);
    }
}

/// <summary>
/// Le richieste verso Ecos sul database (#151): decisioni che vanno prima su Ecos, richieste
/// nate qui che nascono anche là, giustificazioni dal cartellino già accettate.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class RichiesteEcosTests
{
    private readonly SchemaCondiviso _schema;

    public RichiesteEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Giovedì 5 febbraio 2026: feriale e passato, come nel banco di prova delle giustificazioni.
    private static readonly DateTime Giorno = new(2026, 2, 5);

    [FactRichiedeMySql]
    public async Task Approvare_una_richiesta_di_Ecos_scrive_prima_su_Ecos_poi_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        int capo = Dipendente(c, "Anna", "Capo", null, null);
        int richiesta = RichiestaEcos(c, mario, "136478", "PENDING");

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        string? errore = await Servizio(ecos).ApproveAbsenceRequestAsync(richiesta, true, null, capo, isManagerOrAdmin: true);

        Assert.Null(errore);
        Assert.Equal(2, ecos.UrlChiamati.Count);
        Assert.Contains("ApiName=PeopleAbsenceRequestPost&Edit=true", ecos.UrlChiamati[1]);
        Assert.Contains("AbsenceRequestID=136478", ecos.CorpiInviati[1]);
        Assert.Contains("StatusCode=ACCEPTED", ecos.CorpiInviati[1]);
        Assert.Equal("APPROVED", Stato(c, richiesta));
    }

    [FactRichiedeMySql]
    public async Task Rifiutare_manda_il_motivo_a_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        int capo = Dipendente(c, "Anna", "Capo", null, null);
        int richiesta = RichiestaEcos(c, mario, "136478", "PENDING");

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        string? errore = await Servizio(ecos).ApproveAbsenceRequestAsync(richiesta, false, "troppa gente fuori", capo, isManagerOrAdmin: true);

        Assert.Null(errore);
        Assert.Contains("StatusCode=REJECT", ecos.CorpiInviati[1]);
        Assert.Contains("ApproveReply=troppa+gente+fuori", ecos.CorpiInviati[1]);
        Assert.Equal("REJECTED", Stato(c, richiesta));
    }

    [FactRichiedeMySql]
    public async Task Se_Ecos_rifiuta_la_decisione_qui_non_cambia_niente()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        int capo = Dipendente(c, "Anna", "Capo", null, null);
        int richiesta = RichiestaEcos(c, mario, "136478", "PENDING");

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaErrore("-1", "Token expired"));
        string? errore = await Servizio(ecos).ApproveAbsenceRequestAsync(richiesta, true, null, capo, isManagerOrAdmin: true);

        Assert.NotNull(errore);
        Assert.Contains("Token expired", errore);
        Assert.Equal("PENDING", Stato(c, richiesta));

        // E il ramo sincrono da solo la rifiuta: la decisione passa da Ecos.
        Assert.Equal(HrAttendanceService.VaDecisaSuEcos,
            Servizio(ecos).ApproveAbsenceRequest(richiesta, true, null, capo, isManagerOrAdmin: true));
    }

    [FactRichiedeMySql]
    public async Task Annullare_cancella_prima_su_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        int richiesta = RichiestaEcos(c, mario, "136478", "PENDING");

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        string? errore = await Servizio(ecos).CancelAbsenceRequestAsync(richiesta, mario, isAdmin: true);

        Assert.Null(errore);
        Assert.Contains("Edit=true", ecos.UrlChiamati[1]);
        Assert.Contains("AbsenceRequestID=136478", ecos.CorpiInviati[1]);
        Assert.Contains("Delete=1", ecos.CorpiInviati[1]);
        Assert.Equal("CANCELLED", Stato(c, richiesta));
    }

    [FactRichiedeMySql]
    public async Task Una_richiesta_nuova_nasce_anche_su_Ecos_in_attesa()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), RispostaCausali(), RispostaInsert("136500", "5374", "REQUEST"));
        var req = new HrCreateAbsenceRequest
        {
            EmployeeId = mario, DateFrom = Giorno, DateTo = Giorno.AddDays(1), IsFullDay = true,
            AbsenceType = "VACATION", Notes = "ponte",
        };
        (int? id, string? errore, string? avviso) = await Servizio(ecos).CreateAbsenceRequestAsync(req, mario, isManagerOrAdmin: false);

        Assert.Null(errore);
        Assert.Null(avviso);
        Assert.NotNull(id);
        // Token, causali, inserimento.
        Assert.Equal(3, ecos.UrlChiamati.Count);
        Assert.Contains("ApiName=AnagTSCategoryGetAll", ecos.UrlChiamati[1]);
        Assert.Contains("ApiName=PeopleAbsenceRequestPost&ReturnAllPostedRecord=1", ecos.UrlChiamati[2]);
        Assert.DoesNotContain("Edit=true", ecos.UrlChiamati[2]);
        string corpo = ecos.CorpiInviati[2];
        Assert.Contains("EmplID=5374", corpo);
        Assert.Contains("DateBegin=2026-02-05+00%3A00%3A00", corpo);
        Assert.Contains("DateEnd=2026-02-06+00%3A00%3A00", corpo);
        Assert.Contains("FullDay=1", corpo);
        Assert.Contains("CategoryID=2313", corpo);   // F = Ferie, letto dalle causali
        Assert.Contains("StatusCode=REQUEST", corpo);
        Assert.Contains("Note=ATEC+PM%3A+ponte", corpo);

        var riga = c.QuerySingle<(string Source, string? EcosId, string Status, string? Notes)>(
            "SELECT source, ecos_absence_id, status, notes FROM hr_absences WHERE id = @Id", new { Id = id });
        Assert.Equal(("ATEC", "136500", "PENDING", "ponte"), riga);
    }

    [FactRichiedeMySql]
    public async Task Una_richiesta_a_ore_manda_la_fascia_e_le_ore_vengono_dalla_fascia()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), RispostaCausali(), RispostaInsert("136501", "5374", "REQUEST"));
        var req = new HrCreateAbsenceRequest
        {
            EmployeeId = mario, DateFrom = Giorno, DateTo = Giorno, IsFullDay = false,
            AbsenceType = "PERMIT", HourFrom = "14:30", HourTo = "17:00",
        };
        (int? id, string? errore, string? avviso) = await Servizio(ecos).CreateAbsenceRequestAsync(req, mario, isManagerOrAdmin: false);

        Assert.Null(errore);
        Assert.Null(avviso);
        string corpo = ecos.CorpiInviati[2];
        Assert.Contains("FullDay=0", corpo);
        Assert.Contains("HourBegin=14%3A30%3A00", corpo);
        Assert.Contains("HourEnd=17%3A00%3A00", corpo);
        Assert.Contains("CategoryID=2314", corpo);   // P = ROL
        Assert.DoesNotContain("DateEnd=", corpo);

        var riga = c.QuerySingle<(decimal? Hours, TimeSpan? Da, TimeSpan? A)>(
            "SELECT hours, hour_from, hour_to FROM hr_absences WHERE id = @Id", new { Id = id });
        Assert.Equal((2.5m, new TimeSpan(14, 30, 0), new TimeSpan(17, 0, 0)), riga);
    }

    [FactRichiedeMySql]
    public async Task Se_Ecos_non_risponde_la_richiesta_resta_qui_con_un_avviso()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), RispostaCausali(), InvioEcosTests.EcosFinto.Timeout);
        var req = new HrCreateAbsenceRequest
        {
            EmployeeId = mario, DateFrom = Giorno, DateTo = Giorno, IsFullDay = true, AbsenceType = "SICKNESS",
        };
        (int? id, string? errore, string? avviso) = await Servizio(ecos).CreateAbsenceRequestAsync(req, mario, isManagerOrAdmin: false);

        Assert.Null(errore);
        Assert.NotNull(id);
        Assert.Contains("non su Ecos", avviso);
        var riga = c.QuerySingle<(string? EcosId, string? Notes, string Status)>(
            "SELECT ecos_absence_id, notes, status FROM hr_absences WHERE id = @Id", new { Id = id });
        Assert.Null(riga.EcosId);
        Assert.Contains("Ecos: non inviata", riga.Notes);
        Assert.Equal("PENDING", riga.Status);
    }

    [FactRichiedeMySql]
    public async Task La_giustificazione_dal_cartellino_nasce_su_Ecos_gia_accettata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        int autore = Dipendente(c, "Anna", "Ufficio", null, null);

        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), RispostaCausali(), RispostaInsert("136502", "5374", "ACCEPTED"));
        var req = new HrGiustificaRequest { EmployeeId = mario, Date = Giorno, Causale = "FE" };
        (string? errore, string? avviso) = await Servizio(ecos).SaveGiustificaAsync(req, autore);

        Assert.Null(errore);
        Assert.Null(avviso);
        string corpo = ecos.CorpiInviati[2];
        Assert.Contains("FullDay=1", corpo);
        Assert.Contains("StatusCode=ACCEPTED", corpo);
        Assert.Contains("CategoryID=2313", corpo);

        var riga = c.QuerySingle<(string Source, string? EcosId, string Status, bool Piena)>(
            "SELECT source, ecos_absence_id, status, is_full_day FROM hr_absences WHERE employee_id = @Id", new { Id = mario });
        Assert.Equal(("MANUAL", "136502", "APPROVED", true), riga);

        // Togliere la causale cancella anche su Ecos.
        var ecosDopo = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        (errore, avviso) = await Servizio(ecosDopo).SaveGiustificaAsync(
            new HrGiustificaRequest { EmployeeId = mario, Date = Giorno, Causale = "" }, autore);
        Assert.Null(errore);
        Assert.Contains("AbsenceRequestID=136502", ecosDopo.CorpiInviati[1]);
        Assert.Contains("Delete=1", ecosDopo.CorpiInviati[1]);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_absences WHERE employee_id = @Id", new { Id = mario }));
    }

    [FactRichiedeMySql]
    public void L_import_non_riscrive_le_note_di_una_richiesta_nata_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42", 5374);
        c.Execute(@"INSERT INTO hr_absences (employee_id, date_from, date_to, is_full_day, absence_type, status, source, ecos_absence_id, notes)
                    VALUES (@Id, @G, @G, 1, 'VACATION', 'PENDING', 'ATEC', '136500', 'ponte')", new { Id = mario, G = Giorno });
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto());

        // Su Ecos la accettano: qui lo stato cambia, la nota di chi l'ha scritta resta.
        var daEcos = new EcosAbsenceRequest("136500", EmplCode: "42", "Rossi, Mario", "F", "Ferie", "ACCEPTED", Giorno, Giorno,
            FullDay: true, HourBegin: null, HourEnd: null, Duration: 8m, EmplId: "5374");
        Assert.Equal((0, 1), servizio.SyncAbsences(c, new[] { daEcos }));
        var riga = c.QuerySingle<(string Status, string? Notes, string Source)>(
            "SELECT status, notes, source FROM hr_absences WHERE ecos_absence_id = '136500'");
        Assert.Equal(("APPROVED", "ponte", "ATEC"), riga);
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio(HttpMessageHandler handler)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ecos:UserId"] = "utente",
                ["Ecos:Password"] = "segreta",
                ["Ecos:ClientId"] = "atec",
            })
            .Build();
        var ecos = new EcosClient(config, NullLogger<EcosClient>.Instance, new HttpClient(handler));
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int Dipendente(MySqlConnection c, string nome, string cognome, string? ecosCode, int? emplId)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code, ecos_empl_id) VALUES (@N, @C, @Codice, @EmplId)",
            new { N = nome, C = cognome, Codice = ecosCode, EmplId = emplId });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static int RichiestaEcos(MySqlConnection c, int employeeId, string ecosId, string status)
    {
        c.Execute(@"INSERT INTO hr_absences (employee_id, date_from, date_to, hours, is_full_day, absence_type, status, source, ecos_absence_id, notes)
                    VALUES (@Id, @G, @G, 2.5, 0, 'PERMIT', @Status, 'ECOS', @Ecos, 'ECOS: ROL 14:30-17:00')",
            new { Id = employeeId, G = Giorno, Status = status, Ecos = ecosId });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static string Stato(MySqlConnection c, int absenceId) =>
        c.ExecuteScalar<string>("SELECT status FROM hr_absences WHERE id = @Id", new { Id = absenceId });

    /// <summary>Le causali come le manda Ecos (AnagTSCategoryGetAll, 08/09/2026).</summary>
    private static string RispostaCausali() => """
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
            "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [
                { "CategoryID": "2299", "CategoryCode": "F_ND", "DescShort": "Assenza", "StatusCode": "A", "isAbsence": "True" },
                { "CategoryID": "2313", "CategoryCode": "F", "DescShort": "Ferie", "StatusCode": "A", "isAbsence": "True" },
                { "CategoryID": "2314", "CategoryCode": "P", "DescShort": "ROL", "StatusCode": "A", "isAbsence": "True" },
                { "CategoryID": "2315", "CategoryCode": "M", "DescShort": "Malattia", "StatusCode": "A", "isAbsence": "True" },
                { "CategoryID": "2318", "CategoryCode": "I1", "DescShort": "Infortunio", "StatusCode": "A", "isAbsence": "True" },
                { "CategoryID": "2310", "CategoryCode": "S", "DescShort": "Ore a chiusura", "StatusCode": "I", "isAbsence": "True" } ] } } }
        """;

    /// <summary>Come risponde Ecos a un inserimento riuscito con ReturnAllPostedRecord=1 (richiesta 136492, 08/09/2026).</summary>
    private static string RispostaInsert(string id, string emplId, string stato) => $$"""
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "ERROR_CODE": "0", "RECORDCOUNT": "1", "MESSAGE": "Correct Record Insert" },
            "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": { "AbsenceRequestID": "{{id}}", "AbsenceRequestCode": "3896", "EmplID": "{{emplId}}",
                "StatusCode": "{{stato}}", "Delete": "False" } } } }
        """;
}
