using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// ── v72 — un reparto solo per il costo orario (decisione #10 della #37) ──────
// La vista timesheet risolveva il reparto con MIN(department_id), la dashboard commessa
// con `is_primary → is_responsible → id`: due regole per lo stesso costo orario, quindi
// due possibili verità sul costo delle ore di chi sta in più reparti.
// Si allinea la vista alla regola della dashboard (il reparto PRINCIPALE).
// Il log conta le persone toccate e quante di loro cambiano davvero costo: oggi in
// produzione è 1 persona e 0 cambi di importo, ed è per questo che si fa adesso.
public sealed class M072_VistaTimesheetRepartoPrincipale : IMigrazione
{
    public int Versione => 72;

    public string Descrizione => "v_timesheet_with_section: reparto principale (is_primary) invece di MIN(department_id)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int divergenti = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM (
                SELECT ed.employee_id,
                       MIN(ed.department_id) AS vecchia,
                       SUBSTRING_INDEX(GROUP_CONCAT(ed.department_id
                           ORDER BY ed.is_primary DESC, ed.is_responsible DESC, ed.id), ',', 1) AS nuova
                FROM employee_departments ed GROUP BY ed.employee_id
                HAVING vecchia <> nuova
            ) t");
        int cambiaCosto = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM (
                SELECT ed.employee_id,
                       MIN(dv.hourly_cost) AS costo_vecchio,
                       SUBSTRING_INDEX(GROUP_CONCAT(dn.hourly_cost
                           ORDER BY ed.is_primary DESC, ed.is_responsible DESC, ed.id), ',', 1) AS costo_nuovo
                FROM employee_departments ed
                JOIN departments dv ON dv.id = ed.department_id
                JOIN departments dn ON dn.id = ed.department_id
                GROUP BY ed.employee_id
                HAVING costo_vecchio <> costo_nuovo
            ) t");

        // La vista v_timesheet_with_section la riallinea EnsureViews, a ogni avvio e in tutti
        // gli ambienti (blocco A2, 15/08/2026). Qui c'era un `c.Execute(TimesheetSectionViewSql)`:
        // eseguiva la definizione di OGGI dentro una migrazione vecchia, e su un database
        // ancora indietro (ripristino di un backup di mesi fa) falliva, perché quella
        // definizione nomina tabelle e colonne nate da migrazioni successive.
        log.LogInformation(
            "[Migration v72] Regola del reparto principale: {Divergenti} persone cambiano reparto di riferimento, di cui {CambiaCosto} con un costo orario diverso (la vista la riallinea EnsureViews all'avvio).",
            divergenti, cambiaCosto);
    }
}
