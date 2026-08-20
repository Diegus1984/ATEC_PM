using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M024_SalGgSaldoDefault : IMigrazione
{
    public int Versione => 24;

    public string Descrizione => "SAL: default GG saldo 0 sulle righe legacy senza valore";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, quelle righe restano con GG saldo a NULL: l'elenco scadenze le
    // filtra già via esplicitamente, quindi non si rompe niente.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Regola di business SAL: GG saldo nullo (legacy) → 0 giorni (stesso giorno fattura).
        int fixedRows = c.Execute("UPDATE sal_rows SET gg_saldo = 0 WHERE gg_saldo IS NULL");
        if (fixedRows > 0)
            log.LogInformation("[Migration v24] {Count} righe sal_rows legacy portate a GG saldo 0", fixedRows);
    }
}
