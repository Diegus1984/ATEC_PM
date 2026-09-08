using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

// Parte «ferie nel planner Risorse» di HrAttendanceService (08/09/2026, ordine di Diego:
// «fai tutte e due, e vince Ecos»). Le ferie approvate su Ecos, lette giorno per giorno
// (hr_absence_days), diventano barre FERIE in res_assignments; dove una barra messa a mano
// non coincide con quello che Ecos ha approvato, vince Ecos. Manuale Ecos §9.8.
public partial class HrAttendanceService
{
    /// <summary>La descrizione delle barre create da qui: sono derivate, e si tolgono da sole quando l'approvazione sparisce.</summary>
    internal const string DescrizioneFerieHr = "Ferie approvate (HR)";

    /// <summary>
    /// Le sequenze di giornate intere di ferie, per dipendente: giorni consecutivi, dove
    /// «consecutivi» scavalca sabati, domeniche e festivi (Ecos manda solo i giorni
    /// lavorativi; nel planner la barra 21–30 che copre il weekend è quella che si legge).
    /// Un giorno lavorativo senza ferie in mezzo chiude l'intervallo.
    /// </summary>
    internal static List<(int EmployeeId, DateTime Dal, DateTime Al)> IntervalliFerie(
        IEnumerable<(int EmployeeId, DateTime Giorno)> giorniInteri)
    {
        var intervalli = new List<(int, DateTime, DateTime)>();
        foreach (var gruppo in giorniInteri.GroupBy(g => g.EmployeeId))
        {
            List<DateTime> giorni = gruppo.Select(g => g.Giorno.Date).Distinct().OrderBy(d => d).ToList();
            DateTime inizio = giorni[0], precedente = giorni[0];
            for (int i = 1; i < giorni.Count; i++)
            {
                DateTime giorno = giorni[i];
                bool ponte = true;
                for (DateTime x = precedente.AddDays(1); x < giorno; x = x.AddDays(1))
                {
                    if (GiornoLavorativo(x)) { ponte = false; break; }
                }
                if (ponte)
                {
                    precedente = giorno;
                    continue;
                }
                intervalli.Add((gruppo.Key, inizio, precedente));
                inizio = precedente = giorno;
            }
            intervalli.Add((gruppo.Key, inizio, precedente));
        }
        return intervalli;
    }

    private static bool GiornoLavorativo(DateTime giorno) =>
        giorno.DayOfWeek != DayOfWeek.Saturday && giorno.DayOfWeek != DayOfWeek.Sunday
        && !TimesheetRules.IsHoliday(giorno);

    /// <summary>
    /// Allinea le barre FERIE del planner Risorse alle ferie approvate su Ecos nella finestra
    /// [<paramref name="dal"/>, <paramref name="al"/>], giornate intere soltanto (le ferie di
    /// tre ore non stanno in un planner che ragiona a giorni).
    ///
    /// <para>Regole, nell'ordine:</para>
    /// <list type="number">
    ///   <item>ogni intervallo di Ecos senza barra sovrapposta → barra nuova «Ferie approvate (HR)»;</item>
    ///   <item>con una barra sovrapposta (manuale o dal VPS) → <b>vince Ecos</b>: la barra prende
    ///   le date dell'intervallo (stesso id, così la sincronizzazione col VPS la vede come
    ///   modifica), le altre sovrapposte allo stesso intervallo si tolgono;</item>
    ///   <item>una barra creata da qui che non corrisponde più a nessun intervallo → via
    ///   (l'approvazione è sparita);</item>
    ///   <item>una barra <i>manuale</i> senza ferie Ecos resta: è pianificazione fatta prima della
    ///   richiesta, e il planner serve anche a quello.</item>
    /// </list>
    /// 🪤 Le barre che escono dalla finestra non si toccano: oltre l'orizzonte Ecos potrebbe
    /// aver approvato giorni che ancora non abbiamo letto.
    /// </summary>
    internal (int Create, int Aggiornate, int Rimosse) SyncFeriePlanner(MySqlConnection c, DateTime dal, DateTime al)
    {
        int tabella = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'res_assignments'");
        if (tabella == 0) return (0, 0, 0);

        Dictionary<int, decimal> oreGiornaliere = c.Query<(int Id, decimal? Ore)>(
                "SELECT id AS Id, hr_daily_hours AS Ore FROM employees")
            .ToDictionary(x => x.Id, x => x.Ore ?? 8m);

        var giorniInteri = AssenzeEcosPerGiorno(c, dal, al, anchePending: false).Values
            .Where(g => g.AbsenceType == "VACATION"
                        && GiornataIntera(g, oreGiornaliere.GetValueOrDefault(g.EmployeeId, 8m)))
            .Select(g => (g.EmployeeId, g.WorkDate))
            .ToList();
        List<(int EmployeeId, DateTime Dal, DateTime Al)> intervalli = IntervalliFerie(giorniInteri);

        List<BarraFerie> barre = c.Query<BarraFerie>(@"
            SELECT id AS Id, employee_id AS EmployeeId, data_inizio AS DataInizio, data_fine AS DataFine,
                   descrizione AS Descrizione
            FROM res_assignments
            WHERE tipo = 'FERIE' AND data_inizio <= @Al AND data_fine >= @Dal",
            new { Dal = dal.Date, Al = al.Date }).ToList();

        var consumate = new HashSet<long>();
        int create = 0, aggiornate = 0, rimosse = 0;

        using (MySqlTransaction tran = c.BeginTransaction())
        {
            foreach ((int employeeId, DateTime inizio, DateTime fine) in intervalli)
            {
                List<BarraFerie> sovrapposte = barre
                    .Where(b => b.EmployeeId == employeeId && !consumate.Contains(b.Id)
                                && b.DataInizio.Date <= fine && b.DataFine.Date >= inizio)
                    .OrderBy(b => b.DataInizio)
                    .ToList();

                if (sovrapposte.Count == 0)
                {
                    c.Execute(@"
                        INSERT INTO res_assignments
                            (employee_id, tipo, data_inizio, data_fine, descrizione, created_at, updated_at)
                        VALUES (@EmployeeId, 'FERIE', @Dal, @Al, @Descrizione, NOW(), NOW())",
                        new { EmployeeId = employeeId, Dal = inizio, Al = fine, Descrizione = DescrizioneFerieHr }, tran);
                    create++;
                    continue;
                }

                // Una barra che esce dalla finestra non si tocca: non sappiamo cosa c'è oltre.
                if (sovrapposte.Any(b => b.DataInizio.Date < dal.Date || b.DataFine.Date > al.Date))
                {
                    foreach (BarraFerie b in sovrapposte) consumate.Add(b.Id);
                    continue;
                }

                BarraFerie prima = sovrapposte[0];
                consumate.Add(prima.Id);
                if (prima.DataInizio.Date != inizio || prima.DataFine.Date != fine)
                {
                    c.Execute(@"
                        UPDATE res_assignments
                        SET data_inizio = @Dal, data_fine = @Al, updated_at = NOW()
                        WHERE id = @Id",
                        new { Dal = inizio, Al = fine, prima.Id }, tran);
                    aggiornate++;
                }
                foreach (BarraFerie altra in sovrapposte.Skip(1))
                {
                    c.Execute("DELETE FROM res_assignments WHERE id = @Id", new { altra.Id }, tran);
                    consumate.Add(altra.Id);
                    rimosse++;
                }
            }

            // Le barre nate da qui e rimaste senza intervallo: l'approvazione non c'è più.
            foreach (BarraFerie orfana in barre.Where(b => !consumate.Contains(b.Id)
                                                            && b.Descrizione == DescrizioneFerieHr
                                                            && b.DataInizio.Date >= dal.Date && b.DataFine.Date <= al.Date))
            {
                c.Execute("DELETE FROM res_assignments WHERE id = @Id", new { orfana.Id }, tran);
                rimosse++;
            }

            tran.Commit();
        }

        if (create + aggiornate + rimosse > 0)
        {
            _logger.LogInformation(
                "[HR] Ferie Ecos nel planner {Dal:dd/MM/yyyy}-{Al:dd/MM/yyyy}: {C} barre create, {A} allineate a Ecos, {R} tolte.",
                dal, al, create, aggiornate, rimosse);
        }
        return (create, aggiornate, rimosse);
    }

    private sealed class BarraFerie
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime DataInizio { get; set; }
        public DateTime DataFine { get; set; }
        public string? Descrizione { get; set; }
    }
}
