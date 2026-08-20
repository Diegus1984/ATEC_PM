using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M066_RimozioneRuoloAmm : IMigrazione
{
    public int Versione => 66;

    public string Descrizione => "rimozione ruolo AMM dai ruoli di sistema, eredita dal reparto Contabilita";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute("DELETE FROM auth_levels WHERE role_name = 'AMM'");
        c.Execute("UPDATE employees SET user_role = 'RESP_REPARTO' WHERE user_role = 'AMM'");
        log.LogInformation("[Migration v66] Rimozione ruolo AMM dai ruoli di sistema; permessi ereditati dal reparto Contabilita");
    }
}
