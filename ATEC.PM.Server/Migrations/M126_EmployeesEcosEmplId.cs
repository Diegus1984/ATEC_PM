using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M126 — <c>employees.ecos_empl_id</c>: l'<c>EmplID</c> di Ecos accanto all'<c>EmplCode</c>.
///
/// <para>Il collegamento delle persone è per <c>EmplCode</c> (il codice badge, dal dialogo
/// «Collega Ecos»). Ma <c>PeopleAbsenceRequestGetAll</c> <b>non manda l'EmplCode</b>: solo
/// l'<c>EmplID</c>, l'id interno stabile (guida §11). Per questo in produzione, dal 27/08 al
/// 08/09/2026, nessuna richiesta di Ecos era mai entrata in <c>hr_absences</c>: la mappatura
/// per codice non trovava niente e le saltava tutte in silenzio.</para>
///
/// <para>La colonna si riempie <b>da sola</b>: ogni scarico che porta tutti e due i campi
/// (timbrature, badge, giorni di assenza) insegna la coppia codice→id
/// (<c>HrAttendanceService.ImparaEmplId</c>). Nessuno la compila a mano.</para>
/// </summary>
public sealed class M126_EmployeesEcosEmplId : IMigrazione
{
    public int Versione => 126;

    public string Descrizione =>
        "HR: employees.ecos_empl_id, l'EmplID di Ecos imparato dagli scarichi (le richieste non hanno EmplCode)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool colonna = AddColumnIfMissing(c, "employees", "ecos_empl_id",
            "INT NULL COMMENT 'EmplID di EcosAgile, imparato dagli scarichi (timbrature, badge, assenze)' AFTER ecos_empl_code");
        bool indice = CreaIndiceSeManca(c, "employees", "idx_employees_ecos_empl_id", "ecos_empl_id");

        log.LogInformation("[M126] employees.ecos_empl_id: colonna={C}, indice={I}.", colonna, indice);
    }
}
