using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Le ferie approvate su Ecos, lette giorno per giorno, allineano le barre FERIE del planner
/// Risorse (<c>HrAttendanceService.SyncFeriePlanner</c>, 08/09/2026). La regola di Diego:
/// «vince Ecos» — una barra messa a mano che non coincide con l'approvazione prende le date
/// di Ecos. Una barra manuale senza ferie Ecos invece resta: è pianificazione fatta prima
/// della richiesta.
/// </summary>
public class IntervalliFerieTests
{
    [Fact]
    public void I_giorni_lavorativi_consecutivi_scavalcano_il_weekend()
    {
        // Settembre 2026: 21-25 (lun-ven) e 28-30 (lun-mer) = una ferie sola dal 21 al 30.
        var giorni = new[] { 21, 22, 23, 24, 25, 28, 29, 30 }
            .Select(d => (EmployeeId: 1, Giorno: new DateTime(2026, 9, d)));

        var intervalli = HrAttendanceService.IntervalliFerie(giorni);

        Assert.Single(intervalli);
        Assert.Equal((1, new DateTime(2026, 9, 21), new DateTime(2026, 9, 30)), intervalli[0]);
    }

    [Fact]
    public void Un_giorno_lavorativo_senza_ferie_chiude_l_intervallo()
    {
        // Giovedì 5 e lunedì 9 febbraio: venerdì 6 è lavorativo e non è ferie → due intervalli.
        var giorni = new[] { (EmployeeId: 1, Giorno: new DateTime(2026, 2, 5)), (1, new DateTime(2026, 2, 9)) };

        var intervalli = HrAttendanceService.IntervalliFerie(giorni);

        Assert.Equal(2, intervalli.Count);
        Assert.Equal(new DateTime(2026, 2, 5), intervalli[0].Al);
        Assert.Equal(new DateTime(2026, 2, 9), intervalli[1].Dal);
    }

    [Fact]
    public void Il_festivo_in_mezzo_fa_ponte_e_i_dipendenti_restano_separati()
    {
        // Giovedì 30 aprile e lunedì 4 maggio 2026: in mezzo il 1° maggio (festivo) e il weekend.
        var giorni = new[]
        {
            (EmployeeId: 1, Giorno: new DateTime(2026, 4, 30)), (1, new DateTime(2026, 5, 4)),
            (EmployeeId: 2, Giorno: new DateTime(2026, 5, 4)),
        };

        var intervalli = HrAttendanceService.IntervalliFerie(giorni).OrderBy(i => i.EmployeeId).ToList();

        Assert.Equal(2, intervalli.Count);
        Assert.Equal((1, new DateTime(2026, 4, 30), new DateTime(2026, 5, 4)), intervalli[0]);
        Assert.Equal((2, new DateTime(2026, 5, 4), new DateTime(2026, 5, 4)), intervalli[1]);
    }
}

[Collection(SchemaCondiviso.Nome)]
public class FeriePlannerDaEcosTests
{
    private readonly SchemaCondiviso _schema;

    public FeriePlannerDaEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static readonly DateTime Dal = new(2026, 9, 1);
    private static readonly DateTime Al = new(2026, 10, 31);

    [FactRichiedeMySql]
    public void Le_ferie_approvate_di_Ecos_entrano_nel_planner_e_vince_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi");
        int luigi = Dipendente(c, "Luigi", "Verdi");
        int anna = Dipendente(c, "Anna", "Bianchi");
        HrAttendanceService servizio = Servizio();

        // Mario: Ecos ha approvato 21-25 e 28-30 settembre; nel planner c'è una barra messa a
        // mano più lunga (fino al 2 ottobre). Vince Ecos: la barra diventa 21-30.
        foreach (int d in new[] { 21, 22, 23, 24, 25, 28, 29, 30 })
            GiornoDiFerie(c, mario, new DateTime(2026, 9, d), "r1", 480);
        long barraMario = Barra(c, mario, new DateTime(2026, 9, 21), new DateTime(2026, 10, 2), "Dal VPS");
        // …e una barra nata da qui che non corrisponde più a niente: se ne va.
        Barra(c, mario, new DateTime(2026, 10, 12), new DateTime(2026, 10, 13), HrAttendanceService.DescrizioneFerieHr);

        // Luigi: ferie approvate 5-6 ottobre senza barra → barra nuova; e una barra manuale
        // senza ferie Ecos (19-23 ottobre) → resta, è pianificazione.
        GiornoDiFerie(c, luigi, new DateTime(2026, 10, 5), "r2", 480);
        GiornoDiFerie(c, luigi, new DateTime(2026, 10, 6), "r2", 480);
        Barra(c, luigi, new DateTime(2026, 10, 19), new DateTime(2026, 10, 23), "Pianificate");

        // Anna: tre ore di ferie il 17 settembre → il planner ragiona a giorni, non ci va.
        GiornoDiFerie(c, anna, new DateTime(2026, 9, 17), "r3", 180);

        Assert.Equal((1, 1, 1), servizio.SyncFeriePlanner(c, Dal, Al));

        var diMario = FerieDi(c, mario);
        Assert.Single(diMario);
        Assert.Equal((barraMario, new DateTime(2026, 9, 21), new DateTime(2026, 9, 30), "Dal VPS"), diMario[0]);

        var diLuigi = FerieDi(c, luigi);
        Assert.Equal(2, diLuigi.Count);
        Assert.Equal((new DateTime(2026, 10, 5), new DateTime(2026, 10, 6), HrAttendanceService.DescrizioneFerieHr),
            (diLuigi[0].Inizio, diLuigi[0].Fine, diLuigi[0].Descrizione));
        Assert.Equal("Pianificate", diLuigi[1].Descrizione);

        Assert.Empty(FerieDi(c, anna));

        // Rifarlo non cambia niente.
        Assert.Equal((0, 0, 0), servizio.SyncFeriePlanner(c, Dal, Al));
    }

    [FactRichiedeMySql]
    public void Due_barre_sullo_stesso_intervallo_diventano_una()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi");
        foreach (int d in new[] { 21, 22, 23, 24, 25, 28, 29, 30 })
            GiornoDiFerie(c, mario, new DateTime(2026, 9, d), "r1", 480);
        Barra(c, mario, new DateTime(2026, 9, 21), new DateTime(2026, 9, 25), "Prima settimana");
        Barra(c, mario, new DateTime(2026, 9, 28), new DateTime(2026, 9, 30), "Seconda settimana");

        Assert.Equal((0, 1, 1), Servizio().SyncFeriePlanner(c, Dal, Al));

        var ferie = FerieDi(c, mario);
        Assert.Single(ferie);
        Assert.Equal((new DateTime(2026, 9, 21), new DateTime(2026, 9, 30)), (ferie[0].Inizio, ferie[0].Fine));
    }

    [FactRichiedeMySql]
    public void Una_barra_che_esce_dalla_finestra_non_si_tocca()
    {
        // 🪤 Oltre la finestra Ecos potrebbe aver approvato giorni che non abbiamo ancora letto:
        // una barra che sconfina non si accorcia, e non le si mette accanto un doppione.
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi");
        foreach (int d in new[] { 28, 29, 30 })
            GiornoDiFerie(c, mario, new DateTime(2026, 9, d), "r1", 480);
        Barra(c, mario, new DateTime(2026, 9, 28), new DateTime(2026, 11, 15), "Lunga");

        Assert.Equal((0, 0, 0), Servizio().SyncFeriePlanner(c, Dal, Al));

        var ferie = FerieDi(c, mario);
        Assert.Single(ferie);
        Assert.Equal(new DateTime(2026, 11, 15), ferie[0].Fine);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int Dipendente(MySqlConnection c, string nome, string cognome)
    {
        c.Execute("INSERT INTO employees (first_name, last_name) VALUES (@N, @C)", new { N = nome, C = cognome });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void GiornoDiFerie(MySqlConnection c, int employeeId, DateTime giorno, string richiesta, int minuti) =>
        c.Execute(@"
            INSERT INTO hr_absence_days (employee_id, work_date, ecos_absence_id, ecos_refine_id, category_code,
                                         absence_type, status, minutes)
            VALUES (@Id, @Giorno, @Richiesta, '1', 'F', 'VACATION', 'APPROVED', @Minuti)",
            new { Id = employeeId, Giorno = giorno, Richiesta = richiesta, Minuti = minuti });

    private static long Barra(MySqlConnection c, int employeeId, DateTime inizio, DateTime fine, string descrizione)
    {
        c.Execute(@"INSERT INTO res_assignments (employee_id, tipo, data_inizio, data_fine, descrizione)
                    VALUES (@E, 'FERIE', @I, @F, @D)", new { E = employeeId, I = inizio, F = fine, D = descrizione });
        return c.ExecuteScalar<long>("SELECT LAST_INSERT_ID()");
    }

    private static List<(long Id, DateTime Inizio, DateTime Fine, string? Descrizione)> FerieDi(MySqlConnection c, int employeeId) =>
        c.Query<(long Id, DateTime Inizio, DateTime Fine, string? Descrizione)>(
            @"SELECT id, data_inizio, data_fine, descrizione FROM res_assignments
              WHERE employee_id = @E AND tipo = 'FERIE' ORDER BY data_inizio", new { E = employeeId }).ToList();
}
