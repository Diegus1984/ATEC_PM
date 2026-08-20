using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v49: default stato nuove righe DDP commerciale = VER (verificare magazzino).
// Solo bom_items: la officina resta su DO.
public sealed class M049_StatoRigaVer : IMigrazione
{
    public int Versione => 49;

    public string Descrizione => "bom_items: default item_status VER (DDP commerciale)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute("ALTER TABLE bom_items ALTER item_status SET DEFAULT 'VER'");
        log.LogInformation("[Migration v49] bom_items.item_status DEFAULT → VER");
    }
}
