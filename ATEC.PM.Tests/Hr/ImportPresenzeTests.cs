using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// L'import delle timbrature Ecos su database vero, senza HTTP: si esercita il cuore
/// (<see cref="HrAttendanceService.ImportPunches"/>) con timbrature finte.
///
/// <para>Le proprietà da difendere: <b>idempotenza</b> (reimportare non duplica),
/// <b>mirror di Ecos</b> (una timbratura corretta là si aggiorna qui e la giornata si
/// ricalcola — compresa la giornata VECCHIA se lo spostamento cambia giorno), e le
/// <b>rettifiche</b> come righe separate che il ricalcolo vede come le altre.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class ImportPresenzeTests
{
    private readonly SchemaCondiviso _schema;

    public ImportPresenzeTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Giovedì 5 febbraio 2026, feriale: la giornata canonica del banco di prova.
    private static readonly DateTime Giorno = new(2026, 2, 5);

    // ── IMPORT ────────────────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void Import_crea_il_grezzo_e_calcola_il_cartellino()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();

        HrImportResultDto esito = servizio.ImportPunches(c, GiornataRegolare("42"));

        Assert.True(esito.Success);
        Assert.Equal(4, esito.PunchesAdded);
        Assert.Equal(0, esito.PunchesUpdated);
        Assert.Equal(1, esito.DaysRecalculated);
        Assert.Empty(esito.Unmatched);

        Assert.Equal(4, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));

        // 07:58→08:00, 12:32→12:30, 13:28→13:30, 17:04→17:00 (scatto 30' a favore azienda):
        // 480 minuti ordinari, zero straordinario, pausa un'ora.
        var giornata = c.QuerySingle<(string Entrata1, string Uscita2, int Ordinari, int Straord, int Pausa, bool Anomalia, string Nota)>(
            @"SELECT clock_in_1 AS Entrata1, clock_out_2 AS Uscita2, regular_minutes AS Ordinari,
                     overtime_minutes AS Straord, break_minutes AS Pausa, has_anomaly AS Anomalia, note AS Nota
              FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });

        Assert.Equal("08:00", giornata.Entrata1);
        Assert.Equal("17:00", giornata.Uscita2);
        Assert.Equal(480, giornata.Ordinari);
        Assert.Equal(0, giornata.Straord);
        Assert.Equal(60, giornata.Pausa);
        Assert.False(giornata.Anomalia);
        Assert.Equal("OK", giornata.Nota);
    }

    [FactRichiedeMySql]
    public void Reimportare_lo_stesso_scarico_non_duplica_e_non_ricalcola()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        List<EcosPunch> scarico = GiornataRegolare("42");

        servizio.ImportPunches(c, scarico);
        HrImportResultDto secondo = servizio.ImportPunches(c, scarico);

        Assert.Equal(0, secondo.PunchesAdded);
        Assert.Equal(0, secondo.PunchesUpdated);
        Assert.Equal(0, secondo.DaysRecalculated);
        Assert.Equal(4, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));
    }

    [FactRichiedeMySql]
    public void Timbratura_corretta_in_Ecos_aggiorna_la_riga_e_ricalcola_lo_straordinario()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));

        // In Ecos correggono l'uscita: stesso StampID, orario nuovo (18:04 → 18:00).
        HrImportResultDto esito = servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s4", Giorno.AddHours(18).AddMinutes(4), "42", "OUT"),
        });

        Assert.Equal(0, esito.PunchesAdded);
        Assert.Equal(1, esito.PunchesUpdated);

        var giornata = c.QuerySingle<(int Ordinari, int Straord, string? Fasce)>(
            @"SELECT regular_minutes AS Ordinari, overtime_minutes AS Straord, bands_json AS Fasce
              FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });

        // 08:00-12:30 + 13:30-18:00 = 540 minuti: 480 ordinari + 60 di straordinario
        // diurno feriale (fascia A della circolare).
        Assert.Equal(480, giornata.Ordinari);
        Assert.Equal(60, giornata.Straord);
        Assert.NotNull(giornata.Fasce);
        Assert.Contains("\"A\"", giornata.Fasce);
    }

    [FactRichiedeMySql]
    public void Timbratura_spostata_di_giorno_dissolve_il_cartellino_vecchio()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("solo", Giorno.AddHours(7).AddMinutes(58), "42", "IN"),
        });
        Assert.Equal(1, ConteggioGiornate(c, mario, Giorno));

        // Ecos sposta la timbratura al giorno dopo: il cartellino del 5 deve sparire,
        // non restare calcolato su una timbratura che non c'è più.
        DateTime giornoDopo = Giorno.AddDays(1);
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("solo", giornoDopo.AddHours(7).AddMinutes(58), "42", "IN"),
        });

        Assert.Equal(0, ConteggioGiornate(c, mario, Giorno));
        Assert.Equal(1, ConteggioGiornate(c, mario, giornoDopo));
    }

    [FactRichiedeMySql]
    public void Codice_Ecos_senza_dipendente_finisce_nei_non_abbinati()
    {
        using MySqlConnection c = _schema.Apri();
        CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();

        HrImportResultDto esito = servizio.ImportPunches(c, new List<EcosPunch>
        {
            new("x1", Giorno.AddHours(8), "999", "Pallino, Pinco", "IN", null),
        });

        Assert.True(esito.Success);
        Assert.Equal(0, esito.PunchesAdded);
        Assert.Single(esito.Unmatched);
        Assert.Equal("999 — Pallino, Pinco", esito.Unmatched[0]);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
    }

    // ── RETTIFICHE ────────────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void Rettifica_completa_la_giornata_e_la_sua_rimozione_la_riapre()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        int admin = CreaDipendente(c, "Anna", "Admin", ecosCode: null);
        HrAttendanceService servizio = CreaServizio();

        // Solo l'entrata: anomalia (il ramo-trappola del motore VB, coperto dal banco di prova).
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s1", Giorno.AddHours(7).AddMinutes(58), "42", "IN"),
        });
        Assert.True(Anomalia(c, mario, Giorno));

        string? errore = servizio.AddAdjustment(new HrAdjustmentRequest
        {
            EmployeeId = mario,
            PunchedAt = Giorno.AddHours(17),
            Direction = "out",
            Reason = "Uscita non timbrata (dimenticanza)",
        }, autoreId: admin);

        Assert.Null(errore);
        Assert.False(Anomalia(c, mario, Giorno));

        long rettificaId = c.ExecuteScalar<long>(
            "SELECT id FROM hr_punches WHERE source = 'ADJUSTMENT' AND employee_id = @Id",
            new { Id = mario });

        Assert.Null(servizio.DeleteAdjustment(rettificaId, autoreId: admin));
        Assert.True(Anomalia(c, mario, Giorno));
    }

    [FactRichiedeMySql]
    public void Il_grezzo_del_rilevatore_non_si_elimina_e_la_rettifica_esige_il_motivo()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        int admin = CreaDipendente(c, "Anna", "Admin", ecosCode: null);
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s1", Giorno.AddHours(8), "42", "IN"),
        });
        long ecosId = c.ExecuteScalar<long>("SELECT id FROM hr_punches LIMIT 1");

        Assert.NotNull(servizio.DeleteAdjustment(ecosId, autoreId: admin));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));

        string? senzaReason = servizio.AddAdjustment(new HrAdjustmentRequest
        {
            EmployeeId = mario,
            PunchedAt = Giorno.AddHours(17),
            Direction = "OUT",
            Reason = "  ",
        }, autoreId: admin);
        Assert.NotNull(senzaReason);

        string? versoStorto = servizio.AddAdjustment(new HrAdjustmentRequest
        {
            EmployeeId = mario,
            PunchedAt = Giorno.AddHours(17),
            Direction = "USCITA",
            Reason = "motivo valido",
        }, autoreId: admin);
        Assert.NotNull(versoStorto);
    }

    /// <summary>
    /// Nessuno si certifica le ore da solo (piano §8): la correzione la registra un'altra
    /// persona, e chi non può aggiungersi una timbratura non può nemmeno togliere quella
    /// che il responsabile gli ha messo.
    /// </summary>
    [FactRichiedeMySql]
    public void Nessuno_rettifica_il_proprio_cartellino()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        int capo = CreaDipendente(c, "Anna", "Capo", ecosCode: null);
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s1", Giorno.AddHours(7).AddMinutes(58), "42", "IN"),
        });

        string? seStesso = servizio.AddAdjustment(new HrAdjustmentRequest
        {
            EmployeeId = mario,
            PunchedAt = Giorno.AddHours(19),
            Direction = "OUT",
            Reason = "uscita non timbrata",
        }, autoreId: mario);
        Assert.NotNull(seStesso);
        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE source = 'ADJUSTMENT'"));

        // Il capo può: è il secondo occhio che il piano richiede.
        Assert.Null(servizio.AddAdjustment(new HrAdjustmentRequest
        {
            EmployeeId = mario,
            PunchedAt = Giorno.AddHours(17),
            Direction = "OUT",
            Reason = "uscita non timbrata, verificata",
        }, autoreId: capo));

        // …ma Mario non può cancellarla dal proprio cartellino.
        long rettificaId = c.ExecuteScalar<long>(
            "SELECT id FROM hr_punches WHERE source = 'ADJUSTMENT'");
        Assert.NotNull(servizio.DeleteAdjustment(rettificaId, autoreId: mario));
        Assert.Null(servizio.DeleteAdjustment(rettificaId, autoreId: capo));
    }

    /// <summary>
    /// Una timbratura cancellata su Ecos deve sparire anche qui, altrimenti il cartellino
    /// calcola per sempre su un dato fantasma che nessuno può togliere dalla UI (il grezzo
    /// non si cancella a mano). Solo l'import COMPLETO può farlo: è l'unico che ha in mano
    /// la fotografia intera.
    /// </summary>
    [FactRichiedeMySql]
    public void Import_completo_rimuove_le_timbrature_cancellate_su_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"), full: true);
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));

        // Su Ecos hanno cancellato la doppia strisciata delle 12:32: lo scarico completo
        // non la contiene più.
        List<EcosPunch> senzaUna = GiornataRegolare("42")
            .Where(t => t.ExternalId != "s2").ToList();
        HrImportResultDto esito = servizio.ImportPunches(c, senzaUna, full: true);

        Assert.Equal(3, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
        Assert.True(esito.DaysRecalculated >= 1);

        // L'incrementale invece NON cancella: non ha la fotografia intera, e uno scarico
        // parziale farebbe strage di righe legittime.
        servizio.ImportPunches(c, new List<EcosPunch>(), full: false);
        Assert.Equal(3, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
    }

    /// <summary>
    /// 🪤🪤 Ecos restituisce solo gli ultimi 60 giorni (filtro implicito di
    /// <c>PeopleStampGetAll</c>, scoperto l'08/09/2026): lo scarico «completo» non contiene la
    /// storia, e prima di allora l'import completo la cancellava tutta, scambiando «non
    /// restituita» per «cancellata su Ecos». Si cancella solo dall'orizzonte dello scarico in su.
    /// </summary>
    [FactRichiedeMySql]
    public void Import_completo_non_tocca_la_storia_che_Ecos_non_restituisce_piu()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();

        // Tre mesi fa: importata quando Ecos la restituiva ancora.
        DateTime vecchio = Giorno.AddMonths(-3);
        servizio.ImportPunches(c, GiornataRegolare("42", vecchio, "v"), full: true);
        // Oggi lo scarico completo contiene solo la giornata recente: la storia resta.
        servizio.ImportPunches(c, GiornataRegolare("42"), full: true);
        Assert.Equal(8, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));

        // Uno scarico completo senza la 12:32 recente: quella se ne va, la storia resta.
        List<EcosPunch> senzaUna = GiornataRegolare("42").Where(t => t.ExternalId != "s2").ToList();
        HrImportResultDto esito = servizio.ImportPunches(c, senzaUna, full: true);

        Assert.Equal(7, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
        Assert.Equal(4, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE work_date = @Giorno", new { Giorno = vecchio.Date }));
        Assert.Contains("rimaste com'erano", esito.Message);

        // Scarico vuoto: nessun orizzonte, quindi nessuna cancellazione.
        HrImportResultDto vuoto = servizio.ImportPunches(c, new List<EcosPunch>(), full: true);
        Assert.Equal(7, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
        Assert.Contains("nessuna cancellazione", vuoto.Message);
    }

    /// <summary>
    /// Ecos non toglie mai una timbratura: la marca <c>Delete=1</c> e continua a restituirla,
    /// con <c>UpdateDate</c> alzato — quindi arriva anche all'import incrementale. Prima
    /// dell'08/09/2026 il flag non veniva letto: la timbratura tolta là restava qui a contare,
    /// e nemmeno l'import completo la vedeva sparire perché l'id c'era ancora.
    /// </summary>
    [FactRichiedeMySql]
    public void Una_timbratura_marcata_Delete_se_ne_va_anche_con_l_import_incrementale()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));
        Assert.Equal(4, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));

        // L'incrementale riceve la doppia strisciata delle 12:32 marcata cancellata, e una
        // timbratura cancellata che non abbiamo mai avuta.
        HrImportResultDto esito = servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s2", Giorno.AddHours(12).AddMinutes(32), "42", "OUT") with { Deleted = true },
            Timbratura("s9", Giorno.AddHours(18), "42", "OUT") with { Deleted = true },
        }, full: false);

        Assert.Equal(3, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches"));
        Assert.Null(c.ExecuteScalar<long?>("SELECT id FROM hr_punches WHERE external_id = 's2'"));
        Assert.Null(c.ExecuteScalar<long?>("SELECT id FROM hr_punches WHERE external_id = 's9'"));
        Assert.Equal(0, esito.PunchesAdded);
        Assert.Contains("1 cancellate su Ecos rimosse", esito.Message);
        Assert.True(esito.DaysRecalculated >= 1);
    }

    /// <summary>
    /// Le richieste di assenza seguono gli stati come li scrive Ecos (scheda di
    /// <c>PeopleAbsenceRequestGetAll</c>: ACCEPTED, REQUEST, REJECT) e la cancellazione
    /// logica: una richiesta marcata Delete diventa CANCELLED, una mai vista non si crea.
    /// </summary>
    [FactRichiedeMySql]
    public void Le_richieste_di_assenza_seguono_REJECT_e_Delete_di_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();

        EcosAbsenceRequest Richiesta(string id, string stato, bool cancellata = false) => new(
            id, "42", "Rossi, Mario", "P", "ROL", stato, Giorno, Giorno,
            FullDay: false, HourBegin: "16:00", HourEnd: "17:00", Duration: 1m, Deleted: cancellata);

        servizio.SyncAbsences(c, new[] { Richiesta("r1", "ACCEPTED"), Richiesta("r2", "REJECT") });
        Assert.Equal("APPROVED", Stato(c, "r1"));
        Assert.Equal("REJECTED", Stato(c, "r2"));

        // r1 viene tolta su Ecos; r3 arriva già cancellata e non l'abbiamo mai avuta.
        servizio.SyncAbsences(c, new[] { Richiesta("r1", "ACCEPTED", cancellata: true), Richiesta("r3", "ACCEPTED", cancellata: true) });
        Assert.Equal("CANCELLED", Stato(c, "r1"));
        Assert.Null(Stato(c, "r3"));

        static string? Stato(MySqlConnection c, string idEcos) => c.ExecuteScalar<string?>(
            "SELECT status FROM hr_absences WHERE ecos_absence_id = @Id", new { Id = idEcos });
    }

    [Fact]
    public void L_orizzonte_e_il_primo_UpdateDate_dello_scarico()
    {
        Assert.Null(HrAttendanceService.OrizzonteEcos(new List<EcosPunch>()));
        Assert.Null(HrAttendanceService.OrizzonteEcos(new List<EcosPunch>
        {
            new("a", Giorno.AddHours(8), "42", "Rossi", "IN", null, null),
        }));

        var primo = new DateTime(2026, 2, 3, 7, 59, 0);
        Assert.Equal(primo, HrAttendanceService.OrizzonteEcos(new List<EcosPunch>
        {
            new("a", Giorno.AddHours(8), "42", "Rossi", "IN", null, primo.AddDays(2)),
            new("b", Giorno.AddHours(17), "42", "Rossi", "OUT", null, primo),
            new("c", Giorno.AddHours(18), "42", "Rossi", "OUT", null, null),
        }));
    }

    /// <summary>
    /// Se il ricalcolo si interrompe a metà (deploy, deadlock), le timbrature restano e il
    /// cartellino no: il giro dopo il diff le trova identiche e non ricalcolerebbe più
    /// nulla. La passata di riparazione è ciò che impedisce al buco di essere definitivo.
    /// </summary>
    [FactRichiedeMySql]
    public void Le_giornate_rimaste_indietro_si_rimettono_in_pari_da_sole()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));

        // Si simula il ricalcolo interrotto: il cartellino sparisce, il grezzo resta.
        c.Execute("DELETE FROM hr_days");

        HrImportResultDto esito = servizio.ImportPunches(c, GiornataRegolare("42"));

        Assert.Equal(0, esito.PunchesAdded);
        Assert.Equal(1, ConteggioGiornate(c, mario, Giorno));
        Assert.Equal(480, c.ExecuteScalar<int>(
            "SELECT regular_minutes FROM hr_days WHERE employee_id = @Id", new { Id = mario }));

        // Stesso trattamento per una giornata calcolata con regole ormai vecchie: si
        // riconosce da regole_versione e si rifà, senza che nessuno debba ricordarsene.
        c.Execute("UPDATE hr_days SET rules_version = 0, regular_minutes = 999");
        servizio.RepairDays(c);
        Assert.Equal(480, c.ExecuteScalar<int>(
            "SELECT regular_minutes FROM hr_days WHERE employee_id = @Id", new { Id = mario }));
    }

    [FactRichiedeMySql]
    public void Cartellino_orfano_senza_timbrature_viene_rimosso()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));

        // Il grezzo sparisce (cancellato su Ecos) ma il cartellino resta appeso.
        c.Execute("DELETE FROM hr_punches");
        Assert.Equal(1, ConteggioGiornate(c, mario, Giorno));

        servizio.RepairDays(c);
        Assert.Equal(0, ConteggioGiornate(c, mario, Giorno));
    }

    /// <summary>
    /// Il codice mappato serve a sapere di CHI è una timbratura nuova, non a decidere se
    /// aggiornare una vecchia: una correzione da Ecos su un dipendente nel frattempo
    /// scollegato deve comunque arrivare, altrimenti il mirror si rompe in silenzio.
    /// </summary>
    [FactRichiedeMySql]
    public void Correzione_su_dipendente_scollegato_aggiorna_comunque_la_riga()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));

        Assert.Null(servizio.UpdateEcosMapping(mario, null));

        HrImportResultDto esito = servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s4", Giorno.AddHours(18).AddMinutes(4), "42", "OUT"),
        });

        Assert.Equal(1, esito.PunchesUpdated);
        Assert.Single(esito.Unmatched);   // il codice resta segnalato come da collegare
        Assert.Equal(60, c.ExecuteScalar<int>(
            "SELECT overtime_minutes FROM hr_days WHERE employee_id = @Id", new { Id = mario }));
    }

    /// <summary>
    /// Port di <c>IncludeOvertime</c> nel VB: se l'anagrafica dice di non conteggiare lo
    /// straordinario, il ricalcolo azzera <c>minuti_straord</c> anche su giornate già calcolate.
    /// </summary>
    [FactRichiedeMySql]
    public void Disabling_overtime_in_profile_clears_timesheet()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();
        servizio.ImportPunches(c, GiornataRegolare("42"));
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s4", Giorno.AddHours(18).AddMinutes(4), "42", "OUT"),
        });

        Assert.Equal(60, c.ExecuteScalar<int>(
            "SELECT overtime_minutes FROM hr_days WHERE employee_id = @Id", new { Id = mario }));

        c.Execute("UPDATE employees SET hr_counts_overtime = 0 WHERE id = @Id", new { Id = mario });
        servizio.RecalculateAllEmployeeDays(c, mario);

        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT overtime_minutes FROM hr_days WHERE employee_id = @Id", new { Id = mario }));
    }

    // ── FILTRO DOPPIONI (parità col VB) ───────────────────────────────────────

    /// <summary>
    /// Doppia strisciata a 4 minuti vicino al rientro: nel VB la scartava la CTE SQL PRIMA
    /// del motore. Senza quel primo stadio il doppione fa da ponte nel raggruppamento a
    /// 30' e si porta via il rientro vero — mezz'ora di straordinario persa in silenzio.
    /// </summary>
    [FactRichiedeMySql]
    public void Doppia_strisciata_sotto_i_5_minuti_non_inghiotte_il_rientro()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: "42");
        HrAttendanceService servizio = CreaServizio();

        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("d1", Giorno.AddHours(8), "42", "IN"),
            Timbratura("d2", Giorno.AddHours(12), "42", "OUT"),
            Timbratura("d3", Giorno.AddHours(12).AddMinutes(4), "42", "OUT"),   // doppione
            Timbratura("d4", Giorno.AddHours(12).AddMinutes(31), "42", "IN"),
            Timbratura("d5", Giorno.AddHours(17).AddMinutes(30), "42", "OUT"),
        });

        var giornata = c.QuerySingle<(int Ordinari, int Straord, string Nota)>(
            @"SELECT regular_minutes AS Ordinari, overtime_minutes AS Straord, note AS Nota
              FROM hr_days WHERE employee_id = @Id", new { Id = mario });

        // Turno regolare 08:00-12:00 + 12:30-17:30 con pausa di 30': 540 minuti
        // (480 ordinari + 60 di straordinario), non i 510 del raggruppamento sbagliato.
        Assert.Equal(480, giornata.Ordinari);
        Assert.Equal(60, giornata.Straord);
    }

    // ── CURSORE ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Il cursore si prende dall'orologio DI ECOS (il massimo UpdateDate ricevuto): è
    /// quello su cui l'API valuta il filtro. Col nostro orologio uno scarto fra macchine
    /// aprirebbe una finestra cieca da cui le correzioni non tornano più.
    /// </summary>
    [Fact]
    public void Il_cursore_segue_l_orologio_di_Ecos_quando_c_e()
    {
        var inizio = new DateTime(2026, 2, 24, 10, 0, 0);
        var ultimoAggiornamento = new DateTime(2026, 2, 24, 9, 30, 0);

        DateTime cursore = HrAttendanceService.NuovoCursore(new List<EcosPunch>
        {
            new("a", Giorno.AddHours(8), "42", "Rossi", "IN", null, ultimoAggiornamento.AddHours(-2)),
            new("b", Giorno.AddHours(17), "42", "Rossi", "OUT", null, ultimoAggiornamento),
        }, inizio);

        Assert.Equal(ultimoAggiornamento.AddMinutes(-10), cursore);
    }

    [Fact]
    public void Senza_UpdateDate_il_cursore_ripiega_sul_nostro_orologio_con_un_ora_di_margine()
    {
        var inizio = new DateTime(2026, 2, 24, 10, 0, 0);

        DateTime cursore = HrAttendanceService.NuovoCursore(new List<EcosPunch>
        {
            new("a", Giorno.AddHours(8), "42", "Rossi", "IN", null, null),
        }, inizio);

        Assert.Equal(inizio.AddHours(-1), cursore);
    }

    // ── MAPPATURA ─────────────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void Un_codice_Ecos_non_puo_stare_su_due_persone()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", ecosCode: null);
        int anna = CreaDipendente(c, "Anna", "Verdi", ecosCode: null);
        HrAttendanceService servizio = CreaServizio();

        Assert.Null(servizio.UpdateEcosMapping(mario, "42"));

        string? conflitto = servizio.UpdateEcosMapping(anna, "42");
        Assert.NotNull(conflitto);
        Assert.Contains("Mario Rossi", conflitto);

        // Scollegare: codice vuoto → NULL.
        Assert.Null(servizio.UpdateEcosMapping(mario, "  "));
        Assert.Null(c.ExecuteScalar<string?>(
            "SELECT ecos_empl_code FROM employees WHERE id = @Id", new { Id = mario }));
    }

    // ── PARSER DURATE ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8h 0m", 480)]
    [InlineData("8h 30m", 510)]
    [InlineData("0h 0m", 0)]
    [InlineData("---", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Il_parser_delle_durate_rilegge_il_formato_del_motore(string? durata, int attesi)
    {
        Assert.Equal(attesi, HrAttendanceService.MinutesFrom(durata));
    }

    // ── ATTREZZI ──────────────────────────────────────────────────────────────

    /// <summary>La giornata canonica: 07:58 IN, 12:32 OUT, 13:28 IN, 17:04 OUT.</summary>
    private static List<EcosPunch> GiornataRegolare(string emplCode) => new()
    {
        Timbratura("s1", Giorno.AddHours(7).AddMinutes(58), emplCode, "IN"),
        Timbratura("s2", Giorno.AddHours(12).AddMinutes(32), emplCode, "OUT"),
        Timbratura("s3", Giorno.AddHours(13).AddMinutes(28), emplCode, "IN"),
        Timbratura("s4", Giorno.AddHours(17).AddMinutes(4), emplCode, "OUT"),
    };

    /// <summary>La stessa giornata canonica su un altro giorno, con id esterni propri.</summary>
    private static List<EcosPunch> GiornataRegolare(string emplCode, DateTime giorno, string prefisso) => new()
    {
        Timbratura(prefisso + "1", giorno.AddHours(7).AddMinutes(58), emplCode, "IN"),
        Timbratura(prefisso + "2", giorno.AddHours(12).AddMinutes(32), emplCode, "OUT"),
        Timbratura(prefisso + "3", giorno.AddHours(13).AddMinutes(28), emplCode, "IN"),
        Timbratura(prefisso + "4", giorno.AddHours(17).AddMinutes(4), emplCode, "OUT"),
    };

    /// <summary>
    /// 🪤 <c>UpdateDate</c> un minuto dopo la timbratura, come nella realtà (il terminale
    /// sincronizza subito): è da lì che l'import completo misura l'orizzonte sotto cui non
    /// cancella. Senza, nessuna cancellazione risulterebbe mai giustificata.
    /// </summary>
    private static EcosPunch Timbratura(string id, DateTime orario, string emplCode, string verso) =>
        new(id, orario, emplCode, "Rossi, Mario", verso, "Sede", orario.AddMinutes(1));

    private HrAttendanceService CreaServizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int CreaDipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@Nome, @Cognome, @Codice)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static int ConteggioGiornate(MySqlConnection c, int employeeId, DateTime giorno) =>
        c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno = giorno });

    private static bool Anomalia(MySqlConnection c, int employeeId, DateTime giorno) =>
        c.ExecuteScalar<bool>(
            "SELECT has_anomaly FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno = giorno });
}
