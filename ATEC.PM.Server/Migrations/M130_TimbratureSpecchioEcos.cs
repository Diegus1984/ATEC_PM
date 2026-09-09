using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M130 — le timbrature già scritte su Ecos prendono l'orario che Ecos ha (specchio).
///
/// <para>Diego, 09/09/2026 sera: «Ecos è la bibbia: se qualcuno ha modificato le ore, Ecos
/// viene aggiornato e qui devono essere lo specchio dell'allineamento, chissene frega di
/// come sono arrivate». Fino a oggi «Scrivi su Ecos» lasciava in <c>hr_punches.punched_at</c>
/// l'orario timbrato (07:48) e metteva quello scritto (08:00) solo in <c>ecos_punched_at</c>:
/// il dettaglio della giornata continuava a mostrare 07:48. Da ora il servizio allinea anche
/// <c>punched_at</c>; questa migrazione fa lo stesso per le righe scritte prima (le prove del
/// 09/09 pomeriggio: Frattini e Buda dell'08/09, più la prova del 04/09 su Diego). L'orario
/// originale resta nel registro <c>hr_ecos_sends</c>.</para>
///
/// <para>Le giornate toccate (e le vicine, per i turni di notte) vengono segnate con
/// <c>rules_version = 0</c>: le ricalcola <c>RepairDays</c> al primo import dopo l'avvio,
/// senza rifare qui il motore.</para>
/// </summary>
public sealed class M130_TimbratureSpecchioEcos : IMigrazione
{
    public int Versione => 130;

    public string Descrizione =>
        "HR: le timbrature gia scritte su Ecos prendono l'orario che Ecos ha (punched_at = ecos_punched_at), giornate da ricalcolare";

    public void Applica(MySqlConnection c, ILogger log)
    {
        const string daAllineare = @"
            ecos_punched_at IS NOT NULL AND ecos_sent_at IS NOT NULL AND punched_at <> ecos_punched_at";

        List<(int EmployeeId, DateTime WorkDate)> giornate = c.Query<(int EmployeeId, DateTime WorkDate)>(
            "SELECT DISTINCT employee_id AS EmployeeId, work_date AS WorkDate FROM hr_punches WHERE " + daAllineare).ToList();

        int righe = c.Execute("UPDATE hr_punches SET punched_at = ecos_punched_at WHERE " + daAllineare);

        int giorni = 0;
        foreach ((int employeeId, DateTime workDate) in giornate)
        {
            giorni += c.Execute(@"
                UPDATE hr_days SET rules_version = 0
                WHERE employee_id = @Id AND work_date BETWEEN @Prima AND @Dopo",
                new { Id = employeeId, Prima = workDate.AddDays(-1), Dopo = workDate.AddDays(1) });
        }

        log.LogInformation(
            "[M130] {Righe} timbrature allineate all'orario scritto su Ecos, {Giorni} giornate da ricalcolare al primo import.",
            righe, giorni);
    }
}
