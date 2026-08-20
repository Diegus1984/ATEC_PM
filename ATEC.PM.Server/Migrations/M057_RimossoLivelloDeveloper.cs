using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M057_RimossoLivelloDeveloper : IMigrazione
{
    public int Versione => 57;

    public string Descrizione => "rimosso il livello DEVELOPER: ADMIN è il livello più alto";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Via il livello DEVELOPER (4): esisteva solo in tabella, non era assegnabile
        // dalla scheda utente e — visto che i controlli erano per NOME ruolo — chi lo
        // avesse avuto sarebbe stato trattato peggio di un tecnico. Il vertice è ADMIN.
        int migrati = c.Execute("UPDATE employees SET user_role = 'ADMIN' WHERE user_role = 'DEVELOPER'");

        // Nessuna feature deve restare a un livello che non esiste più: si riporta ad ADMIN.
        int riallineate = c.Execute("UPDATE auth_features SET min_level = 3 WHERE min_level > 3");

        c.Execute("DELETE FROM auth_levels WHERE role_name = 'DEVELOPER'");

        log.LogInformation(
            "[Migration v57] Livello DEVELOPER rimosso ({Migrati} utenti portati ad ADMIN, {Riallineate} feature riportate al livello ADMIN)",
            migrati, riallineate);
    }
}
