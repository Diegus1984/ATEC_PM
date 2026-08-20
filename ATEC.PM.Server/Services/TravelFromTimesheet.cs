using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// La pagina Trasferta si compila da sola dalle ore del Timesheet (segnalazioni #37 e #52).
///
/// **La regola che fa scattare tutto** (#52): contano solo le ore imputate su una fase la cui
/// SEZIONE DI COSTO è marcata <c>DA_CLIENTE</c>. Le ore su fasi «in Atec» seguono la loro
/// strada normale (ore lavorate, Risorse Atec) ma non toccano la trasferta.
///
/// **Cosa nasce**: uno step per fase (lo step *è* la fase, e ne prende il titolo) e una riga
/// per persona + giorno. La riga porta nominativo, data e giorni; alloggio, vitto, indennità,
/// auto e treno restano da scrivere a mano — sono l'unica cosa che il Timesheet non sa.
///
/// **Cosa NON fa, di proposito:**
/// - non tocca **mai** una riga scritta a mano (<c>source='MANUAL'</c>): il confine è quello;
/// - non porta **ore e costo del personale** (li ha eliminati la #52): quelle ore sono già in
///   «Risorse Atec» dal timesheet, ripeterle qui le conterebbe due volte nel Bilancio;
/// - non **cancella** una riga quando le ore spariscono: la marca <c>hours_missing</c> e la
///   lascia lì, perché dentro può esserci un albergo da 300 € imputato a mano (deciso l'08/08/2026).
///
/// È idempotente: si può rilanciare quante volte si vuole sulla stessa commessa e il risultato
/// non cambia — ci pensa la chiave unica (step, persona, giorno).
/// </summary>
public static class TravelFromTimesheet
{
    /// <summary>Una giornata di cantiere: la chiave con cui nasce (o si ritrova) una riga.</summary>
    private sealed class GiornataCantiere
    {
        public int ProjectPhaseId { get; set; }
        public string PhaseName { get; set; } = "";
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateTime WorkDate { get; set; }

        /// <summary>Ore scaricate su QUESTA fase in questa giornata (per la regola #98).</summary>
        public decimal Ore { get; set; }
    }

    /// <summary>Esito della rigenerazione, per il log e per i test.</summary>
    public readonly record struct Esito(int Righe, int Nuove, int Orfane, int Step)
    {
        public override string ToString() =>
            $"{Righe} righe di cantiere ({Nuove} nuove), {Orfane} rimaste senza ore, {Step} step";
    }

    /// <summary>
    /// Rigenera le righe derivate della commessa. Da chiamare dopo ogni scrittura sul
    /// Timesheet che riguardi questa commessa, FUORI da una transazione (<see
    /// cref="TravelPlanService.SyncToBudget"/> ne apre una sua).
    /// </summary>
    public static Esito Rebuild(IDbConnection c, int projectId, int? userId = null)
    {
        // 1. Le giornate di cantiere, dalla vista che sa già risolvere sezione e reparto.
        //    GROUP BY: due imputazioni sulla stessa fase nello stesso giorno sono una
        //    giornata sola di trasferta, non due.
        List<GiornataCantiere> giornate = c.Query<GiornataCantiere>(@"
            SELECT v.project_phase_id AS ProjectPhaseId,
                   MAX(v.phase_name)   AS PhaseName,
                   v.employee_id       AS EmployeeId,
                   MAX(v.employee_name) AS EmployeeName,
                   v.work_date         AS WorkDate,
                   SUM(v.hours)        AS Ore
            FROM v_timesheet_with_section v
            JOIN cost_section_templates cs ON cs.id = v.cost_section_template_id
            WHERE v.project_id = @Pid
              AND cs.section_type = 'DA_CLIENTE'
            GROUP BY v.project_phase_id, v.employee_id, v.work_date
            ORDER BY v.work_date, v.employee_id, v.project_phase_id",
            new { Pid = projectId }).ToList();

        // 2. Uno step per fase. La descrizione insegue il nome della fase: se in commessa la
        //    fase viene rinominata, il titolo della tabella cambia con lei.
        var stepPerFase = new Dictionary<int, int>();
        foreach (var fase in giornate.GroupBy(g => g.ProjectPhaseId))
        {
            string nome = fase.First().PhaseName;
            int? stepId = c.ExecuteScalar<int?>(
                "SELECT id FROM travel_steps WHERE project_id = @Pid AND project_phase_id = @Fase",
                new { Pid = projectId, Fase = fase.Key });

            if (stepId == null)
            {
                stepId = c.ExecuteScalar<int>(@"
                    INSERT INTO travel_steps (project_id, project_phase_id, description, sort_order)
                    VALUES (@Pid, @Fase, @Nome,
                            COALESCE((SELECT m FROM (SELECT MAX(sort_order) + 1 AS m FROM travel_steps WHERE project_id = @Pid) x), 0));
                    SELECT LAST_INSERT_ID()",
                    new { Pid = projectId, Fase = fase.Key, Nome = nome });
            }
            else
            {
                c.Execute(@"UPDATE travel_steps SET description = @Nome
                            WHERE id = @Id AND description <> @Nome",
                    new { Id = stepId, Nome = nome });
            }
            stepPerFase[fase.Key] = stepId.Value;
        }

        // 3. «Giorni trasferta»: il valore della giornata alla prima riga di quella persona,
        //    0 alle altre. Due fasi di cantiere nello stesso giorno restano UN giorno di
        //    trasferta, o l'indennità verrebbe contata due volte (deciso l'08/08/2026).
        //    Quanto vale la giornata lo decidono le ore TOTALI scaricate sul tag cliente in
        //    quel giorno, sommate su tutte le fasi (#98): fino a 4 ore → 0,5, oltre → 1.
        var orePerGiornata = giornate
            .GroupBy(g => (g.EmployeeId, g.WorkDate.Date))
            .ToDictionary(gr => gr.Key, gr => gr.Sum(x => x.Ore));

        var primaDellaGiornata = new HashSet<(int Employee, DateTime Date)>();
        int nuove = 0;
        foreach (GiornataCantiere g in giornate)
        {
            bool prima = primaDellaGiornata.Add((g.EmployeeId, g.WorkDate.Date));
            int righeToccate = c.Execute(@"
                INSERT INTO travel_step_rows
                    (step_id, employee_id, person_name, work_date, source, travel_days,
                     hours_missing, sort_order)
                VALUES
                    (@StepId, @EmployeeId, @PersonName, @WorkDate, @Source, @Days, 0,
                     COALESCE((SELECT m FROM (SELECT MAX(sort_order) + 1 AS m FROM travel_step_rows WHERE step_id = @StepId) x), 0))
                ON DUPLICATE KEY UPDATE
                    person_name   = VALUES(person_name),
                    source        = VALUES(source),
                    travel_days   = VALUES(travel_days),
                    -- Le ore sono tornate: la riga smette di essere segnalata.
                    hours_missing = 0,
                    updated_at    = NOW()",
                new
                {
                    StepId = stepPerFase[g.ProjectPhaseId],
                    g.EmployeeId,
                    PersonName = g.EmployeeName,
                    WorkDate = g.WorkDate.Date,
                    Source = TravelSources.Timesheet,
                    Days = prima
                        ? TravelMath.GiorniDaOre(orePerGiornata[(g.EmployeeId, g.WorkDate.Date)])
                        : 0m,
                });
            // MySQL: 1 = inserita, 2 = aggiornata (0 = uguale a com'era).
            if (righeToccate == 1) nuove++;
        }

        // 4. Le righe derivate che non hanno più ore dietro NON si cancellano: si segnalano.
        //    Se la persona ha spostato le ore su un altro giorno, questa riga resta con le sue
        //    spese e con l'avviso, così è chi legge a decidere se cancellarla.
        var vive = giornate
            .Select(g => $"{stepPerFase[g.ProjectPhaseId]}|{g.EmployeeId}|{g.WorkDate:yyyy-MM-dd}")
            .ToHashSet();

        var derivate = c.Query<(int Id, int StepId, int EmployeeId, DateTime WorkDate, bool HoursMissing)>(@"
            SELECT r.id AS Id, r.step_id AS StepId, r.employee_id AS EmployeeId,
                   r.work_date AS WorkDate, r.hours_missing AS HoursMissing
            FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            WHERE s.project_id = @Pid AND r.source = @Source AND r.work_date IS NOT NULL",
            new { Pid = projectId, Source = TravelSources.Timesheet }).ToList();

        var orfane = derivate
            .Where(r => !vive.Contains($"{r.StepId}|{r.EmployeeId}|{r.WorkDate:yyyy-MM-dd}"))
            .ToList();

        List<int> daSegnalare = orfane.Where(r => !r.HoursMissing).Select(r => r.Id).ToList();
        if (daSegnalare.Count > 0)
        {
            // Insieme all'avviso si azzerano i GIORNI, e non è un dettaglio: se quel giorno la
            // persona aveva due fasi, il giorno valeva 1 su questa riga e 0 sull'altra. Tolte
            // le ore di qui, l'altra riga eredita l'1 al giro successivo: senza questo azzeramento
            // la stessa giornata verrebbe contata due volte (e con lei l'indennità).
            // Le spese scritte a mano — alloggio, vitto, indennità — restano tutte.
            c.Execute(@"UPDATE travel_step_rows
                        SET hours_missing = 1, travel_days = 0, updated_at = NOW()
                        WHERE id IN @Ids",
                new { Ids = daSegnalare });
        }

        // 5. I costi trasferta rientrano nel Riepilogo Costi. Le righe appena nate valgono
        //    zero finché nessuno ci scrive un alloggio, quindi qui non si muove un numero:
        //    si chiama lo stesso, perché è la regola del modulo (ogni scrittura → sync).
        TravelPlanService.SyncToBudget(c, projectId, userId);

        return new Esito(giornate.Count, nuove, orfane.Count, stepPerFase.Count);
    }

    /// <summary>
    /// Rigenera la trasferta delle commesse toccate da un'imputazione di ore. Si passa l'id
    /// della fase (o quello della riga di timesheet appena cancellata, già risolto in fase):
    /// se la fase non è di cantiere non fa nulla, quindi la si può chiamare sempre.
    /// </summary>
    public static void RebuildForPhase(IDbConnection c, int projectPhaseId, int? userId = null)
    {
        int? projectId = c.ExecuteScalar<int?>(
            "SELECT project_id FROM project_phases WHERE id = @Id", new { Id = projectPhaseId });
        if (projectId is > 0) Rebuild(c, projectId.Value, userId);
    }
}
