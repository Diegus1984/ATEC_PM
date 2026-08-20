using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v78 — fasi di commessa «spente» (segnalazione #51).
// Con le fasi che nascono dalla configurazione delle sezioni di costo, una commessa si
// trova finestre di fasi che lì non servono. Spegnerle le toglie dall'elenco del
// Bilancio e dalla tendina del Timesheet (niente ore nuove).
// ⚠️ REGOLA DECISA CON DIEGO l'08/08/2026: spegnere NON toglie niente dai conti.
// Se la fase ha già ore imputate, quelle ore e il loro costo continuano a contare nel
// Bilancio, e chi spegne lo legge scritto nella richiesta di conferma. È il contrario
// di quello che farebbe l'Extra Lavoro della #39, che invece esclude davvero.
public sealed class M078_FasiSpente : IMigrazione
{
    public int Versione => 78;

    public string Descrizione => "project_phases.is_off: fasi spente, fuori da elenco e tendina ma dentro i conti";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool haColonna = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_phases'
              AND COLUMN_NAME = 'is_off'") > 0;
        if (!haColonna)
        {
            c.Execute("ALTER TABLE project_phases ADD COLUMN is_off TINYINT(1) NOT NULL DEFAULT 0 AFTER is_local");
        }

        log.LogInformation("[Migration v78] Colonna is_off sulle fasi di commessa{Gia}.", haColonna ? " (c'era gia)" : "");
    }
}
