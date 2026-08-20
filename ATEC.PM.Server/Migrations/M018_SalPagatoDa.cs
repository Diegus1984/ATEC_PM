using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v18: blocco righe pagate + audit fields + indici
public sealed class M018_SalPagatoDa : IMigrazione
{
    public int Versione => 18;

    public string Descrizione => "sal_rows paid_by + paid_at + index";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Verifica se la colonna paid_by esiste già in sal_rows
        bool hasPaidBy = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'sal_rows'
              AND column_name = 'paid_by'") > 0;
        if (!hasPaidBy)
        {
            c.Execute("ALTER TABLE sal_rows ADD COLUMN paid_by INT NULL, ADD COLUMN paid_at DATETIME NULL");
            log.LogInformation("[Migration v18] Aggiunte colonne paid_by e paid_at a sal_rows");
        }

        // Verifica se l'indice idx_salrow_stato_data esiste
        bool hasIndex = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = 'sal_rows'
              AND index_name = 'idx_salrow_stato_data'") > 0;
        if (!hasIndex)
        {
            c.Execute("ALTER TABLE sal_rows ADD INDEX idx_salrow_stato_data (stato, data_fatt)");
            log.LogInformation("[Migration v18] Aggiunto indice idx_salrow_stato_data a sal_rows");
        }
    }
}
