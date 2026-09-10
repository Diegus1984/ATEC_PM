using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// La pausa dedotta che va su Ecos (09/09/2026, Diego dal «Controllo di ieri»: «se le ore
/// calcolate sono corrette vorrei poterle sincronizzare con Ecos, se l'ora di pausa non esiste,
/// la devo inserire»). La regola di QUALI timbrature si inventano è la parte delicata: sbagliare
/// vuol dire strisciate in più su Ecos e giornate «da verificare» al prossimo import.
/// </summary>
public class PausaDedottaTests
{
    private static readonly DateTime Giorno = new(2026, 2, 5);

    private static (string, DateTime) Esistente(string verso, int h, int m) => (verso, Giorno.AddHours(h).AddMinutes(m));

    [Fact]
    public void Con_due_sole_timbrature_la_pausa_dedotta_sono_due_strisciate_da_inserire()
    {
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("OUT", 17, 0) });

        Assert.Equal(2, d.Count);
        Assert.Equal(("OUT", Giorno.AddHours(12).AddMinutes(30)), (d[0].Direction, d[0].Quando));
        Assert.Equal(("IN", Giorno.AddHours(13).AddMinutes(30)), (d[1].Direction, d[1].Quando));
    }

    [Fact]
    public void Con_una_entrata_e_due_uscite_parte_solo_il_rientro()
    {
        // L'uscita con l'asterisco è una timbratura VERA (12:30 timbrata): è coperta, resta.
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa implicita (1 IN / 2 OUT)", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("OUT", 12, 30), Esistente("OUT", 17, 0) });

        HrAttendanceService.TimbraturaDedotta sola = Assert.Single(d);
        Assert.Equal("IN", sola.Direction);
        Assert.Equal(Giorno.AddHours(13).AddMinutes(30), sola.Quando);
    }

    [Theory]
    // Quattro timbrature con la pausa troppo corta: la pausa è forzata ma le strisciate CI SONO.
    [InlineData("AUTO_P: Pausa 1h forzata")]
    [InlineData("OK")]
    [InlineData("Turno mattutino")]
    [InlineData("⚠ INCOMPLETO: Uscita mancante")]
    [InlineData("")]
    [InlineData(null)]
    public void Nelle_altre_giornate_non_si_inventa_niente(string? nota)
    {
        Assert.Empty(HrAttendanceService.TimbratureDedotte(
            Giorno, nota, "12:30*", "13:30*", new[] { Esistente("IN", 8, 0) }));
    }

    [Fact]
    public void La_nota_con_la_coda_della_notte_vale_lo_stesso_e_le_24_00_non_si_leggono()
    {
        Assert.Equal(2, HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta · 🌙 Notte: il turno finisce domattina", "12:30*", "13:30*",
            Array.Empty<(string, DateTime)>()).Count);

        // Un orario che non è un'ora del giorno resta fuori da sé.
        Assert.Empty(HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "24:00*", "00:00", Array.Empty<(string, DateTime)>()));
    }

    [Theory]
    [InlineData("⚠ INCOMPLETO: Uscita mancante", "OUT")]
    [InlineData("⚠ INCOMPLETO: Solo entrata", "OUT")]
    [InlineData("⚠ INCOMPLETO: Solo uscita", "IN")]
    [InlineData("⚠ INCOMPLETO: Solo entrata · 🌙 Notte: il turno finisce domattina", "OUT")]
    [InlineData("OK", null)]
    [InlineData("AUTO_P: Pausa 1h detratta", null)]
    [InlineData("⚠ ERR: Verificare timbrature", null)]
    [InlineData(null, null)]
    public void La_timbratura_che_manca_e_sempre_l_uscita(string? nota, string? atteso) =>
        Assert.Equal(atteso, HrAttendanceService.VersoMancante(nota));

    [Fact]
    public void Una_pausa_gia_coperta_da_una_rettifica_non_si_reinserisce()
    {
        // HR ha già messo il rientro delle 13:30 come rettifica: parte solo l'uscita.
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("IN", 13, 30), Esistente("OUT", 17, 0) });

        HrAttendanceService.TimbraturaDedotta sola = Assert.Single(d);
        Assert.Equal("OUT", sola.Direction);
    }
}

/// <summary>
/// «Allinea Ecos» sul database: il resoconto dice cosa si scrive, la pausa dedotta diventa
/// timbrature vere (su Ecos e qui), la giornata si ricalcola subito, un timeout non si riprova.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class AllineaEcosTests
{
    private readonly SchemaCondiviso _schema;

    public AllineaEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static readonly DateTime Giorno = new(2026, 2, 5);

    [FactRichiedeMySql]
    public void Il_resoconto_elenca_modifiche_e_pausa_da_inserire_con_la_giornata_calcolata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(12), "OUT");
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto());
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);

        Assert.True(piano.Configured);
        Assert.Equal("AUTO_P: Pausa 1h detratta", piano.Note);
        Assert.Equal("8h 0m", piano.RegularHours);
        Assert.Equal("1h 0m", piano.BreakTime);
        Assert.Equal("12:30*", piano.ClockOut1);
        Assert.Equal(
            new[] { "UPDATE", "UPDATE", "INSERT_BREAK", "INSERT_BREAK" },
            piano.Operations.Select(o => o.Kind).ToArray());
        Assert.Equal("Entrata 07:58 → 08:00", piano.Operations[0].Label);
        Assert.Equal("Uscita 17:12 → 17:00", piano.Operations[1].Label);
        Assert.Equal("Uscita 12:30 (pausa)", piano.Operations[2].Label);
        Assert.Equal("Entrata 13:30 (pausa)", piano.Operations[3].Label);
        Assert.Equal(4, piano.ToWrite);
        Assert.True(piano.CanSend);
        Assert.Equal("", piano.Message);

        // Il cartellino porta lo stesso flag della riga: la pausa è da inserire.
        HrDayDto giornata = servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno);
        Assert.True(giornata.EcosBreakToInsert);
    }

    [FactRichiedeMySql]
    public void Con_un_anomalia_non_si_allinea_niente()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(12).AddMinutes(33), "OUT");
        Grezza(c, mario, "s3", Giorno.AddHours(13).AddMinutes(19), "IN");
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto());
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);

        Assert.True(piano.HasAnomaly);
        Assert.False(piano.CanSend);
        Assert.Contains("anomalia", piano.Message);
        // Le modifiche degli orari ci sarebbero, ma non si toccano finché la giornata non è sistemata.
        Assert.DoesNotContain(piano.Operations, o => o.Kind == "INSERT_BREAK");
    }

    [FactRichiedeMySql]
    public async Task La_pausa_dedotta_si_inserisce_su_Ecos_e_la_giornata_diventa_timbrata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        long uscita = Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");

        // Dopo la scrittura la giornata si rilegge da Ecos, che le due strisciate appena
        // inserite NON le restituisce ancora (manuale §7): devono restare lo stesso.
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"), InvioEcosTests.RispostaInsert("s10", "5374", "42"),
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                InvioEcosTests.Riga("s2", "2026-02-05 17:00:00", "42", "OUT")));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore);

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(0, esito.Sent);
        Assert.Equal(0, esito.Inserted);
        Assert.Equal(2, esito.BreakInserted);
        Assert.Contains("2 timbrature di pausa inserite", esito.Message);
        Assert.True(esito.Resynced);
        Assert.Contains("Riletta da Ecos", esito.Message);

        // Token, badge, due inserimenti col badge (uscita 12:30, rientro 13:30), poi la rilettura.
        Assert.Equal(6, ecos.UrlChiamati.Count);
        Assert.Contains("PeopleStampGetAll", ecos.UrlChiamati[5]);
        Assert.Contains("ApiName=PeopleBadgeGetAll", ecos.UrlChiamati[1]);
        Assert.Contains("ApiName=PeopleStampPost&ReturnAllPostedRecord=1", ecos.UrlChiamati[2]);
        Assert.Contains("BadgeCode=246b3548", ecos.CorpiInviati[2]);
        Assert.Contains("VersusCode=OUT", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+12%3A30%3A00", ecos.CorpiInviati[2]);
        Assert.Contains("VersusCode=IN", ecos.CorpiInviati[3]);
        Assert.Contains("StampDateTime=2026-02-05+13%3A30%3A00", ecos.CorpiInviati[3]);
        Assert.Contains("pausa+pranzo+dedotta", ecos.CorpiInviati[3]);

        // Qui la pausa è nata come timbratura di Ecos (StampID), col motivo e l'autore.
        var pausa = c.Query<(string Source, string ExternalId, DateTime PunchedAt, string Direction, string? Reason, int? CreatedBy)>(
            @"SELECT source, external_id, punched_at, direction, reason, created_by FROM hr_punches
              WHERE employee_id = @Id AND id NOT IN (@E, @U) ORDER BY punched_at",
            new { Id = mario, E = entrata, U = uscita }).ToList();
        Assert.Equal(2, pausa.Count);
        Assert.Equal(("ECOS", "s9", Giorno.AddHours(12).AddMinutes(30), "OUT"), (pausa[0].Source, pausa[0].ExternalId, pausa[0].PunchedAt, pausa[0].Direction));
        Assert.Equal(("ECOS", "s10", Giorno.AddHours(13).AddMinutes(30), "IN"), (pausa[1].Source, pausa[1].ExternalId, pausa[1].PunchedAt, pausa[1].Direction));
        Assert.Equal(HrAttendanceService.TimbraturaDedotta.MotivoPausa, pausa[0].Reason);
        Assert.Equal(autore, pausa[0].CreatedBy);

        // La giornata si è ricalcolata subito: pausa timbrata, non più dedotta, stesse ore.
        var giornata = c.QuerySingle<(string Note, int Regular, int Break, string ClockOut1)>(
            "SELECT note, regular_minutes, break_minutes, clock_out_1 FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal("OK", giornata.Note);
        Assert.Equal(480, giornata.Regular);
        Assert.Equal(60, giornata.Break);
        Assert.Equal("12:30", giornata.ClockOut1);

        // Registro: due inserimenti (senza orario precedente), legati alle timbrature nuove.
        var registro = c.Query<(long? PunchId, string StampId, DateTime? Previous, string Outcome, string? Message)>(
            "SELECT punch_id, ecos_stamp_id, previous_time, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id ORDER BY id",
            new { Id = mario }).ToList();
        Assert.Equal(2, registro.Count);
        Assert.All(registro, r => Assert.Equal("OK", r.Outcome));
        Assert.All(registro, r => Assert.Null(r.Previous));
        Assert.All(registro, r => Assert.NotNull(r.PunchId));
        Assert.Equal("s9", registro[0].StampId);
        Assert.Contains("pausa dedotta", registro[0].Message);

        // Poi non c'è più niente da scrivere, e il flag sulla riga si spegne.
        HrEcosPlanDto dopo = servizio.GetEcosPlan(mario, Giorno);
        Assert.Equal(0, dopo.ToWrite);
        Assert.False(dopo.CanSend);
        Assert.False(servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno).EcosBreakToInsert);
    }

    [FactRichiedeMySql]
    public async Task La_protezione_delle_timbrature_appena_scritte_dura_qualche_giorno_poi_vince_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"), InvioEcosTests.RispostaInsert("s10", "5374", "42"),
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                InvioEcosTests.Riga("s2", "2026-02-05 17:00:00", "42", "OUT"))));
        servizio.RecalculateDay(c, mario, Giorno);
        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore);
        Assert.Equal(2, esito.BreakInserted);
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));

        // Una seconda rilettura a mano, sempre senza le due strisciate: restano (sono nostre, di oggi).
        string[] soloLeVecchie =
        {
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                InvioEcosTests.Riga("s2", "2026-02-05 17:00:00", "42", "OUT")),
        };
        await Servizio(new InvioEcosTests.EcosFinto(soloLeVecchie)).ImportWindowAsync(mario, Giorno, Giorno);
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));

        // Passati i giorni di protezione, se Ecos ancora non le ha, vince Ecos: si tolgono e
        // la giornata torna con la pausa dedotta (e da scrivere).
        c.Execute("UPDATE hr_punches SET ecos_sent_at = DATE_SUB(NOW(), INTERVAL 4 DAY) WHERE employee_id = @Id AND external_id IN ('s9', 's10')",
            new { Id = mario });
        await Servizio(new InvioEcosTests.EcosFinto(soloLeVecchie)).ImportWindowAsync(mario, Giorno, Giorno);
        Assert.Equal(2, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));
        Assert.Equal("AUTO_P: Pausa 1h detratta", c.ExecuteScalar<string>(
            "SELECT note FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno", new { Id = mario, Giorno }));
    }

    [FactRichiedeMySql]
    public async Task Un_timeout_sulla_pausa_resta_nel_registro_e_non_si_riprova_alla_cieca()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");

        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"), InvioEcosTests.EcosFinto.Timeout);
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore);

        Assert.False(esito.Success);
        Assert.Equal(0, esito.BreakInserted);
        Assert.Equal(1, esito.Failed);
        // Al primo errore ci si ferma: il rientro non si è nemmeno tentato.
        Assert.Equal(3, ecos.UrlChiamati.Count);
        // Nessuna timbratura nata qui; nel registro l'esito incerto senza timbratura.
        Assert.Equal(2, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));
        var registro = c.QuerySingle<(long? PunchId, string Outcome, string? Message)>(
            "SELECT punch_id, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario });
        Assert.Null(registro.PunchId);
        Assert.Equal("ERROR", registro.Outcome);
        Assert.StartsWith("Esito incerto", registro.Message);

        // Una pausa a metà non si completa alla cieca: il resoconto lo dice e non scrive niente.
        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);
        Assert.Equal(0, piano.ToWrite);
        Assert.False(piano.CanSend);
        Assert.Contains(piano.Operations, o => o.Kind == "UNCERTAIN");
        Assert.Contains("senza risposta certa", piano.Message);

        // E nemmeno un secondo «Invia» la riprova.
        var ecosDopo = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken());
        HrEcosSendResultDto secondo = await Servizio(ecosDopo).SendDayToEcosAsync(mario, Giorno, autore);
        Assert.True(secondo.Success);
        Assert.Empty(ecosDopo.UrlChiamati);
    }

    [FactRichiedeMySql]
    public async Task Gli_orari_decisi_a_mano_da_HR_vincono_sull_arrotondamento_anche_per_la_pausa()
    {
        // Diego, 09/09/2026 sera: «devo poter modificare a mano gli orari». Entrata 08:10 e
        // uscita 17:21: il motore direbbe 08:00 e 17:30 con la pausa 12:30-13:30. HR decide
        // 08:00, 17:00 e la pausa 12:00-13:00: su Ecos e qui va quello.
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(8).AddMinutes(10), "IN");
        long uscita = Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(21), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate(), InvioEcosTests.RispostaUpdate(),
            InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"), InvioEcosTests.RispostaInsert("s10", "5374", "42"),
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                InvioEcosTests.Riga("s2", "2026-02-05 17:00:00", "42", "OUT")));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        var scelte = new List<HrEcosTimeDto>
        {
            new() { PunchId = entrata, Direction = "IN", Time = "08:00" },
            new() { PunchId = uscita, Direction = "OUT", Time = "17:00" },
            new() { PunchId = null, Direction = "OUT", Time = "12:00" },
            new() { PunchId = null, Direction = "IN", Time = "13:00" },
        };
        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore, scelte);

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(2, esito.Sent);
        Assert.Equal(2, esito.BreakInserted);
        // Su Ecos vanno gli orari di HR, non quelli del motore.
        Assert.Contains("StampDateTime=2026-02-05+08%3A00%3A00", ecos.CorpiInviati[1]);
        Assert.Contains("StampDateTime=2026-02-05+17%3A00%3A00", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+12%3A00%3A00", ecos.CorpiInviati[4]);
        Assert.Contains("StampDateTime=2026-02-05+13%3A00%3A00", ecos.CorpiInviati[5]);
        // E qui sono lo specchio: la giornata ricalcolata è 08:00-12:00 / 13:00-17:00, otto ore.
        var giornata = c.QuerySingle<(string In1, string Out1, string In2, string Out2, int Regular, string Note)>(
            "SELECT clock_in_1, clock_out_1, clock_in_2, clock_out_2, regular_minutes, note FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal(("08:00", "12:00", "13:00", "17:00", 480, "OK"), giornata);
        Assert.Equal(Giorno.AddHours(17), c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE id = @Id", new { Id = uscita }));
        // Il registro tiene l'orario originale (17:21) e quello scritto (17:00).
        var registro = c.QuerySingle<(DateTime PunchedAt, DateTime SentTime)>(
            "SELECT punched_at, sent_time FROM hr_ecos_sends WHERE punch_id = @Id", new { Id = uscita });
        Assert.Equal((Giorno.AddHours(17).AddMinutes(21), Giorno.AddHours(17)), registro);
    }

    [FactRichiedeMySql]
    public async Task L_uscita_che_manca_scritta_a_mano_si_inserisce_su_Ecos_e_la_giornata_torna_regolare()
    {
        // Diego, 09/09/2026 sera: «quando inserisco l'ora mancante deve inserirsi automaticamente
        // dove manca, senza motivo, poi la scriviamo su Ecos». Entrata-uscita-rientro, l'uscita
        // finale no: HR scrive 17:00 nella riga dell'uscita e preme «Scrivi su Ecos».
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(12).AddMinutes(30), "OUT");
        Grezza(c, mario, "s3", Giorno.AddHours(13).AddMinutes(30), "IN");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"),
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                InvioEcosTests.Riga("s2", "2026-02-05 12:30:00", "42", "OUT"),
                InvioEcosTests.Riga("s3", "2026-02-05 13:30:00", "42", "IN")));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        // Prima: la giornata è incompleta, il cartellino dice quale verso manca e il resoconto lo elenca.
        HrDayDto prima = servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno);
        Assert.True(prima.HasAnomaly);
        Assert.Equal("OUT", prima.EcosMissingToInsert);
        Assert.Contains(servizio.GetEcosPlan(mario, Giorno).Operations, o => o.Kind == "MISSING" && o.Direction == "OUT");

        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore,
            new List<HrEcosTimeDto> { new() { PunchId = null, Direction = "OUT", Time = "17:00", Kind = "MISSING" } });

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(1, esito.Inserted);
        Assert.Equal(0, esito.BreakInserted);
        Assert.Contains("VersusCode=OUT", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+17%3A00%3A00", ecos.CorpiInviati[2]);
        Assert.Contains("mancante", ecos.CorpiInviati[2]);

        // Qui è nata come timbratura di Ecos, con un motivo scritto da noi, e la giornata è regolare.
        var nuova = c.QuerySingle<(string Source, string ExternalId, string? Reason)>(
            "SELECT source, external_id, reason FROM hr_punches WHERE employee_id = @Id AND punched_at = @Quando",
            new { Id = mario, Quando = Giorno.AddHours(17) });
        Assert.Equal(("ECOS", "s9", HrAttendanceService.TimbraturaDedotta.MotivoMancante), nuova);
        var giornata = c.QuerySingle<(string Out2, int Regular, string Note, bool Anomalia)>(
            "SELECT clock_out_2, regular_minutes, note, has_anomaly FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal(("17:00", 480, "OK", false), giornata);
        Assert.Null(servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno).EcosMissingToInsert);
    }

    /// <summary>
    /// Chi ha timbrato solo l'uscita ha dimenticato di timbrare la mattina: quella che si
    /// inserisce è l'ENTRATA, e va PRIMA di quello che c'è (10/09/2026, caso Obreja).
    /// </summary>
    [FactRichiedeMySql]
    public async Task L_entrata_che_manca_si_inserisce_prima_della_prima_timbratura()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(17), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"),
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaTimbrature(
                InvioEcosTests.Riga("s1", "2026-02-05 17:00:00", "42", "OUT")));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        // Le 18:00 stanno DOPO l'uscita: non è un'entrata del mattino.
        HrEcosSendResultDto tardi = await servizio.SendDayToEcosAsync(mario, Giorno, autore,
            new List<HrEcosTimeDto> { new() { PunchId = null, Direction = "IN", Time = "18:00", Kind = "MISSING" } });
        Assert.False(tardi.Success);
        Assert.Contains("prima della prima timbratura", tardi.Message);

        // Alle 08:00 invece sì.
        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore,
            new List<HrEcosTimeDto> { new() { PunchId = null, Direction = "IN", Time = "08:00", Kind = "MISSING" } });

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(1, esito.Inserted);
        Assert.Contains("VersusCode=IN", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+08%3A00%3A00", ecos.CorpiInviati[2]);

        // La giornata torna intera: otto ore con la pausa dedotta.
        var giornata = c.QuerySingle<(string In1, string Note)>(
            "SELECT clock_in_1, note FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal("08:00", giornata.In1);
        Assert.StartsWith("AUTO_P", giornata.Note);
    }

    [FactRichiedeMySql]
    public async Task L_uscita_mancante_deve_venire_dopo_l_ultima_timbratura_e_solo_dove_manca_davvero()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(12).AddMinutes(30), "OUT");
        Grezza(c, mario, "s3", Giorno.AddHours(13).AddMinutes(30), "IN");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken());
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        // 12:00 sta prima del rientro delle 13:30: non è un'uscita di fine giornata.
        HrEcosSendResultDto prima = await servizio.SendDayToEcosAsync(mario, Giorno, mario,
            new List<HrEcosTimeDto> { new() { PunchId = null, Direction = "OUT", Time = "12:00", Kind = "MISSING" } });
        Assert.False(prima.Success);
        Assert.Contains("dopo l'ultima timbratura", prima.Message);
        Assert.Empty(ecos.UrlChiamati);

        // Una giornata regolare non ha niente che manca: si rifiuta senza toccare Ecos.
        Grezza(c, mario, "s4", Giorno.AddHours(17), "OUT");
        servizio.RecalculateDay(c, mario, Giorno);
        HrEcosSendResultDto poi = await servizio.SendDayToEcosAsync(mario, Giorno, mario,
            new List<HrEcosTimeDto> { new() { PunchId = null, Direction = "OUT", Time = "18:00", Kind = "MISSING" } });
        Assert.False(poi.Success);
        Assert.Contains("non ha una timbratura mancante", poi.Message);
        Assert.Empty(ecos.UrlChiamati);
    }

    /// <summary>
    /// Il lettore a volte registra il gesto al contrario: la timbratura delle 17 finisce come
    /// ENTRATA, e la giornata racconta il rovescio di quello che è successo. HR corregge il
    /// verso dal dettaglio, la correzione va prima su Ecos e la giornata si rifà (Diego,
    /// 10/09/2026, dopo il caso Obreja).
    /// </summary>
    [FactRichiedeMySql]
    public async Task Il_verso_sbagliato_si_corregge_prima_su_Ecos_e_poi_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int capo = Dipendente(c, null);
        long unica = Grezza(c, mario, "s1", Giorno.AddHours(17).AddMinutes(3), "IN");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        // Come sta adesso: per il motore è entrato alle 17 e non è più uscito.
        Assert.Equal("⚠ INCOMPLETO: Solo entrata",
            c.ExecuteScalar<string>("SELECT note FROM hr_days WHERE employee_id = @Id", new { Id = mario }));

        Assert.Null(await servizio.SetPunchDirectionAsync(unica, "OUT", capo));

        // Su Ecos è partito il verso, non l'orario.
        Assert.Equal(2, ecos.UrlChiamati.Count);
        Assert.Contains("Edit=true", ecos.UrlChiamati[1]);
        Assert.Contains("StampID=s1", ecos.CorpiInviati[1]);
        Assert.Contains("VersusCode=OUT", ecos.CorpiInviati[1]);
        Assert.DoesNotContain("StampDateTime", ecos.CorpiInviati[1]);

        // Qui la timbratura è un'uscita, e la giornata dice la cosa giusta.
        Assert.Equal("OUT", c.ExecuteScalar<string>("SELECT direction FROM hr_punches WHERE id = @Id", new { Id = unica }));
        var giornata = c.QuerySingle<(string In1, string Out1, string Note)>(
            "SELECT clock_in_1, clock_out_1, note FROM hr_days WHERE employee_id = @Id", new { Id = mario });
        Assert.Equal(("??:??", "17:00", "⚠ INCOMPLETO: Solo uscita"), giornata);

        // E il registro tiene la storia della correzione.
        Assert.Contains("Verso corretto",
            c.ExecuteScalar<string>("SELECT message FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario }));
    }

    [FactRichiedeMySql]
    public async Task Se_Ecos_rifiuta_il_verso_qui_resta_com_era()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int capo = Dipendente(c, null);
        long unica = Grezza(c, mario, "s1", Giorno.AddHours(17), "IN");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaErrore("-2", "Record not found"));
        HrAttendanceService servizio = Servizio(ecos);

        string? errore = await servizio.SetPunchDirectionAsync(unica, "OUT", capo);

        Assert.NotNull(errore);
        Assert.Contains("rifiutato", errore);
        Assert.Equal("IN", c.ExecuteScalar<string>("SELECT direction FROM hr_punches WHERE id = @Id", new { Id = unica }));
        Assert.Equal("ERROR",
            c.ExecuteScalar<string>("SELECT outcome FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario }));
    }

    [FactRichiedeMySql]
    public async Task Il_verso_non_si_cambia_sul_proprio_cartellino_ne_in_quello_che_e_gia()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int capo = Dipendente(c, null);
        long unica = Grezza(c, mario, "s1", Giorno.AddHours(17), "IN");
        var ecos = new InvioEcosTests.EcosFinto();
        HrAttendanceService servizio = Servizio(ecos);

        Assert.Contains("tuo cartellino", await servizio.SetPunchDirectionAsync(unica, "OUT", mario));
        Assert.Contains("già un'entrata", await servizio.SetPunchDirectionAsync(unica, "IN", capo));
        Assert.Contains("non trovata", await servizio.SetPunchDirectionAsync(999999, "OUT", capo));
        Assert.Empty(ecos.UrlChiamati);
    }

    [FactRichiedeMySql]
    public async Task Una_timbratura_di_Ecos_si_cancella_prima_su_Ecos_e_poi_qui()
    {
        // Segnalazione #152 (Diego, 09/09/2026): Buda 03/09 aveva l'uscita delle 12:30 due volte
        // e da qui non si poteva togliere. Ecos è la bibbia: prima la cancellazione logica là.
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(12).AddMinutes(30), "OUT");
        long doppia = Grezza(c, mario, "s3", Giorno.AddHours(12).AddMinutes(30), "OUT");
        Grezza(c, mario, "s4", Giorno.AddHours(13).AddMinutes(30), "IN");
        Grezza(c, mario, "s5", Giorno.AddHours(17), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        Assert.Null(await servizio.DeletePunchAsync(doppia, autore));

        // Token + una PeopleStampPost con Edit=true, StampID e Delete=1.
        Assert.Equal(2, ecos.UrlChiamati.Count);
        Assert.Contains("Edit=true", ecos.UrlChiamati[1]);
        Assert.Contains("StampID=s3", ecos.CorpiInviati[1]);
        Assert.Contains("Delete=1", ecos.CorpiInviati[1]);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE id = @Id", new { Id = doppia }));
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));
        var registro = c.QuerySingle<(string StampId, string Outcome, string Message)>(
            "SELECT ecos_stamp_id, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario });
        Assert.Equal("s3", registro.StampId);
        Assert.Equal("OK", registro.Outcome);
        Assert.Contains("Delete", registro.Message);
        var giornata = c.QuerySingle<(string Out1, string Note, bool Anomalia)>(
            "SELECT clock_out_1, note, has_anomaly FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal(("12:30", "OK", false), giornata);
    }

    [FactRichiedeMySql]
    public async Task Se_Ecos_rifiuta_la_cancellazione_la_timbratura_resta_anche_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        long s1 = Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaErrore("-2", "Record not found"));
        HrAttendanceService servizio = Servizio(ecos);

        string? errore = await servizio.DeletePunchAsync(s1, autore);
        Assert.NotNull(errore);
        Assert.Contains("rifiutato", errore);
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE id = @Id", new { Id = s1 }));
        Assert.Equal("ERROR", c.ExecuteScalar<string>("SELECT outcome FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario }));

        // Sul proprio cartellino non si cancella niente, e Ecos non si chiama nemmeno.
        Assert.Contains("tuo cartellino", await servizio.DeletePunchAsync(s1, mario));
        Assert.Equal(2, ecos.UrlChiamati.Count);
    }

    [FactRichiedeMySql]
    public async Task Un_orario_scritto_male_ferma_tutto_prima_di_toccare_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(8).AddMinutes(10), "IN");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken());

        HrEcosSendResultDto esito = await Servizio(ecos).SendDayToEcosAsync(mario, Giorno, mario,
            new List<HrEcosTimeDto> { new() { PunchId = entrata, Direction = "IN", Time = "8.00" } });

        Assert.False(esito.Success);
        Assert.Contains("Orario non valido", esito.Message);
        Assert.Empty(ecos.UrlChiamati);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

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

    private static int Dipendente(MySqlConnection c, string? ecosCode, int? emplId = null)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code, ecos_empl_id) VALUES ('Mario', 'Rossi', @Codice, @EmplId)",
            new { Codice = ecosCode, EmplId = emplId });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static long Grezza(MySqlConnection c, int employeeId, string stampId, DateTime orario, string verso)
    {
        c.Execute(@"INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
                    VALUES (@Id, @Giorno, @Ora, @Verso, 'ECOS', @Stamp)",
            new { Id = employeeId, Giorno = orario.Date, Ora = orario, Verso = verso, Stamp = stampId });
        return c.ExecuteScalar<long>("SELECT LAST_INSERT_ID()");
    }
}
