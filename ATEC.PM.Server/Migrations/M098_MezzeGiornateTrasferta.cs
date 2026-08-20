using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v98 — MEZZE GIORNATE DI TRASFERTA (segnalazione #98, regola di Paolo Zanoni).
// Fino a ieri una giornata di cantiere valeva sempre 1 giorno di trasferta, qualunque
// fosse il numero di ore scaricate. La regola nuova: fino a 4 ore sul tag cliente la
// giornata vale MEZZA (0,5), da 5 in su vale 1. Quindi:
//  · travel_days passa da INT a DECIMAL(3,1), per poter scrivere 0,5;
//  · le righe derivate già in tabella con travel_days = 1 vengono dimezzate dove le ore
//    di quella giornata (persona + giorno + commessa, sommate su TUTTE le fasi di
//    cantiere) erano al più 4. Le righe a 0 (seconda fase dello stesso giorno) e le
//    manuali (travel_days NULL) non si toccano.
// Il perimetro DA_CLIENTE è riscritto qui su timesheet_entries + project_phases invece
// che sulla vista v_timesheet_with_section, di proposito: su un database appena nato la
// vista potrebbe non esserci ancora quando le migrazioni girano, e la regola usata è la
// stessa (pp.cost_section_template_id → section_type, senza filtri su extra lavoro).
// La soglia «<= 4» vive anche in TravelMath.GiorniDaOre (motore) e nel previsto di
// TravelPlanService.Summaries: se cambia una deve cambiare in tutti e tre i posti.
public sealed class M098_MezzeGiornateTrasferta : IMigrazione
{
    public int Versione => 98;

    public string Descrizione => "giorni trasferta a mezze giornate: travel_days DECIMAL(3,1) + dimezzate le giornate fino a 4 ore";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute("ALTER TABLE travel_step_rows MODIFY COLUMN travel_days DECIMAL(3,1) NULL");

        int dimezzate = c.Execute(@"
            UPDATE travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            JOIN (
                SELECT te.employee_id, te.work_date, pp.project_id, SUM(te.hours) AS ore
                FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                JOIN cost_section_templates cst ON cst.id = pp.cost_section_template_id
                WHERE cst.section_type = 'DA_CLIENTE'
                GROUP BY te.employee_id, te.work_date, pp.project_id
            ) g ON g.employee_id = r.employee_id
               AND g.work_date   = r.work_date
               AND g.project_id  = s.project_id
            SET r.travel_days = 0.5, r.updated_at = NOW()
            WHERE r.source = 'TIMESHEET' AND r.travel_days = 1 AND g.ore <= 4");

        log.LogInformation("[Migration v98] travel_days in mezze giornate: {Dimezzate} giornate fino a 4 ore portate a 0,5.", dimezzate);
    }
}
