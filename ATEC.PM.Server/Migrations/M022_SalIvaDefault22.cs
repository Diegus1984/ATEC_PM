using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M022_SalIvaDefault22 : IMigrazione
{
    public int Versione => 22;

    public string Descrizione => "SAL v10: default IVA 22% sulle righe legacy senza %IVA";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, le righe SAL vecchie restano con %IVA a NULL: il DTO la espone
    // come valore facoltativo e a video quell'IVA vale 0 invece di 22 — un numero da correggere
    // a mano, non un gestionale fermo.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Regola di business SAL: le righe senza %IVA (legacy, pre-v10) valgono 22%
        // come nel prototipo; le righe nuove nascono già a 22 (default in CreateRow).
        int fixedRows = c.Execute("UPDATE sal_rows SET iva_perc = 22 WHERE iva_perc IS NULL");
        if (fixedRows > 0)
            log.LogInformation("[Migration v22] {Count} righe sal_rows legacy portate a IVA 22%", fixedRows);
    }
}
