using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// L'aggancio HR → planner Risorse (<c>HrAttendanceService.SyncToResourcePlanner</c>): una
/// ferie approvata diventa una riga FERIE in <c>res_assignments</c>.
///
/// <para>La trappola di PIANO-SYNC-RISORSE.md §8 («doppie ferie»): prima si cercava una FERIE
/// con le date IDENTICHE, quindi una ferie già nel piano con date più larghe (messa a mano o
/// arrivata dal VPS) ne faceva nascere una seconda, due barre in conflitto su entrambi i lati.
/// Ora basta una FERIE dello stesso dipendente che si sovrappone al periodo. E la riga nuova
/// porta <c>updated_at</c>: il motore di sincronizzazione lo usa per decidere chi vince.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class FerieNelPlannerTests
{
    private readonly SchemaCondiviso _schema;

    public FerieNelPlannerTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static int Dipendente(MySqlConnection c)
    {
        c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Ferie(MySqlConnection c, int employeeId, DateTime inizio, DateTime fine) =>
        c.Execute(@"INSERT INTO res_assignments (employee_id, tipo, data_inizio, data_fine, descrizione)
                    VALUES (@E, 'FERIE', @I, @F, 'Dal VPS')", new { E = employeeId, I = inizio, F = fine });

    private static List<(DateTime Inizio, DateTime Fine, DateTime? UpdatedAt, string? Descrizione)> FerieDi(MySqlConnection c, int employeeId) =>
        c.Query<(DateTime, DateTime, DateTime?, string?)>(
            "SELECT data_inizio, data_fine, updated_at, descrizione FROM res_assignments WHERE employee_id = @E AND tipo = 'FERIE' ORDER BY id",
            new { E = employeeId }).ToList();

    [FactRichiedeMySql]
    public void Con_una_ferie_sovrapposta_gia_nel_piano_non_ne_crea_una_seconda()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        // Nel piano c'è già 10-21 agosto (dal VPS, date più larghe della richiesta HR).
        Ferie(c, mario, new DateTime(2026, 8, 10), new DateTime(2026, 8, 21));

        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), "VACATION", isApproved: true);
        // Sovrapposizione anche di un giorno solo, ai bordi.
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 21), new DateTime(2026, 8, 25), "VACATION", isApproved: true);
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 3), new DateTime(2026, 8, 10), "VACATION", isApproved: true);

        var ferie = FerieDi(c, mario);
        Assert.Single(ferie);
        Assert.Equal(new DateTime(2026, 8, 10), ferie[0].Inizio);
        Assert.Equal(new DateTime(2026, 8, 21), ferie[0].Fine);
    }

    [FactRichiedeMySql]
    public void Senza_ferie_nel_periodo_la_crea_con_updated_at_valorizzato()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        // Una ferie che NON si sovrappone (settembre) non deve bloccare quella di agosto.
        Ferie(c, mario, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11));
        DateTime prima = c.ExecuteScalar<DateTime>("SELECT NOW()").AddSeconds(-2);

        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), "VACATION", isApproved: true);

        var ferie = FerieDi(c, mario);
        Assert.Equal(2, ferie.Count);
        (DateTime inizio, DateTime fine, DateTime? updatedAt, string? descrizione) = ferie[1];
        Assert.Equal(new DateTime(2026, 8, 12), inizio);
        Assert.Equal(new DateTime(2026, 8, 14), fine);
        Assert.Equal("Ferie approvate (HR)", descrizione);
        Assert.NotNull(updatedAt);
        Assert.True(updatedAt >= prima, $"updated_at {updatedAt} dovrebbe essere adesso (>= {prima})");

        // Una seconda approvazione dello stesso periodo non raddoppia.
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), "VACATION", isApproved: true);
        Assert.Equal(2, FerieDi(c, mario).Count);
    }

    [FactRichiedeMySql]
    public void Il_rifiuto_toglie_solo_la_ferie_con_le_date_identiche_e_le_altre_causali_non_toccano_il_piano()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        Ferie(c, mario, new DateTime(2026, 8, 10), new DateTime(2026, 8, 21));
        Ferie(c, mario, new DateTime(2026, 8, 12), new DateTime(2026, 8, 14));

        // Rifiuto: via SOLO quella con le date uguali (com'era), la più larga resta.
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), "VACATION", isApproved: false);
        var ferie = FerieDi(c, mario);
        Assert.Single(ferie);
        Assert.Equal(new DateTime(2026, 8, 10), ferie[0].Inizio);

        // Un permesso o una malattia non sono ferie: il planner non si tocca.
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 9, 1), new DateTime(2026, 9, 1), "SICK", isApproved: true);
        HrAttendanceService.SyncToResourcePlanner(c, mario, new DateTime(2026, 8, 10), new DateTime(2026, 8, 21), "PERMIT", isApproved: false);
        Assert.Single(FerieDi(c, mario));
    }
}
