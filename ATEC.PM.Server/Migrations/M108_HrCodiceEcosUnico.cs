using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M108 — <c>employees.ecos_empl_code</c> diventa UNICO (piano: <c>PIANO-HR-PRESENZE.md</c>).
///
/// <para>Il codice badge di EcosAgile dice a chi appartiene una timbratura. Se lo stesso
/// codice finisce su due persone, l'import attribuisce le ore <b>a caso</b> fra le due e
/// nessun errore lo segnala: le ore di uno finiscono nel cartellino dell'altro. Il
/// controllo applicativo in <c>HrPresenzeService.AggiornaMappatura</c> c'è, ma fra la
/// lettura e la scrittura passa un istante: due salvataggi quasi simultanei passano
/// entrambi. La difesa vera è qui.</para>
///
/// <para>🪤 <b>I NULL non danno fastidio</b>: in MySQL un indice UNIQUE ammette quanti NULL
/// vuole, quindi i dipendenti non ancora collegati restano tutti scollegati insieme. È il
/// motivo per cui la colonna deve restare NULL (mai stringa vuota) quando si scollega —
/// <c>AggiornaMappatura</c> converte apposta il vuoto in NULL.</para>
///
/// <para>🪤 <b>Prima si bonifica, poi si vincola</b>: se in casa ci fossero già duplicati
/// (mappature fatte a mano prima di questa migrazione) il CREATE UNIQUE INDEX fallirebbe e
/// il server non partirebbe. Qui i duplicati si scollegano tenendo il dipendente più
/// vecchio, e chi resta fuori lo si ricollega dalla pagina.</para>
/// </summary>
public sealed class M108_HrCodiceEcosUnico : IMigrazione
{
    public int Versione => 108;

    public string Descrizione =>
        "HR: employees.ecos_empl_code unico (un codice badge Ecos = una persona sola)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // La colonna arriva con la M107; se qualcuno applicasse questa migrazione su uno
        // schema più vecchio, meglio uscire in silenzio che far fallire l'avvio.
        bool colonna = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'employees'
              AND column_name = 'ecos_empl_code'") > 0;
        if (!colonna)
        {
            log.LogWarning("[M108] employees.ecos_empl_code non c'è: niente da vincolare.");
            return;
        }

        // Stringa vuota = scollegato, ma due stringhe vuote violerebbero l'unicità.
        int svuotati = c.Execute(
            "UPDATE employees SET ecos_empl_code = NULL WHERE ecos_empl_code = ''");

        // Duplicati preesistenti: tiene il dipendente con id più basso (il più vecchio),
        // scollega gli altri. Sono mappature da rifare a mano, non dati persi.
        int scollegati = c.Execute(@"
            UPDATE employees e
            JOIN (SELECT ecos_empl_code AS codice, MIN(id) AS tenuto
                  FROM employees
                  WHERE ecos_empl_code IS NOT NULL
                  GROUP BY ecos_empl_code
                  HAVING COUNT(*) > 1) d
              ON d.codice = e.ecos_empl_code
            SET e.ecos_empl_code = NULL
            WHERE e.id <> d.tenuto");

        if (svuotati > 0 || scollegati > 0)
            log.LogWarning("[M108] Bonifica prima del vincolo: {Vuoti} codici vuoti azzerati, " +
                           "{Doppi} duplicati scollegati (da rifare dalla pagina Timbrature).",
                svuotati, scollegati);

        // L'indice non-unico della M107 va tolto: sarebbe il doppione di questo, e un
        // indice ridondante si paga a ogni scrittura.
        if (!EsisteIndiceUnico(c))
        {
            c.Execute("CREATE UNIQUE INDEX `uq_employees_ecos` ON `employees` (`ecos_empl_code`)",
                commandTimeout: 600);
            log.LogInformation("[M108] Indice unico su employees.ecos_empl_code creato.");
        }
        EliminaIndiceSePresente(c, "employees", "idx_employees_ecos");
    }

    private static bool EsisteIndiceUnico(MySqlConnection c) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'employees'
              AND index_name = 'uq_employees_ecos'") > 0;
}
