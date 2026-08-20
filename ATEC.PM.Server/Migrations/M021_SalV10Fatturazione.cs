using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v21: SAL v10 — campi fatturazione/incasso su sal_rows, PO/riferimento offerta su project_sal,
// anagrafiche causali Conto SAP e stati pagamento.
// Le righe legacy con stato='pagata' diventano stato='emessa' + pagamento='Pagata'
// (paid_by/paid_at restano come audit dell'incasso).
// NB: creava anche `sal_prospetto_checks`, non più (controllo periodico rimosso il 03/08/2026).
public sealed class M021_SalV10Fatturazione : IMigrazione
{
    public int Versione => 21;

    public string Descrizione => "SAL v10: campi fatturazione/incasso su sal_rows, po/rif_offerta su project_sal, anagrafiche causali SAP e stati pagamento, controlli prospetto";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Tabelle nuove (sal_sap_causali, sal_payment_states);
        // no-op sulle tabelle SAL già esistenti (CREATE TABLE IF NOT EXISTS).
        SalDbService.InitTables(c, log);

        // Nuove colonne sal_rows, una per volta con check di esistenza; la catena AFTER
        // replica l'ordine del CREATE TABLE (tra 'stato' e 'sort_order').
        (string Column, string Definition)[] salRowColumns = new (string, string)[]
        {
            ("iva_perc",       "INT NULL AFTER stato"),
            ("gg_saldo",       "INT NULL AFTER iva_perc"),
            ("n_fatt",         "VARCHAR(50) NOT NULL DEFAULT '' AFTER gg_saldo"),
            ("conto_sap",      "VARCHAR(200) NOT NULL DEFAULT '' AFTER n_fatt"),
            ("pagamento",      "VARCHAR(100) NOT NULL DEFAULT '' AFTER conto_sap"),
            ("data_pagamento", "DATE NULL AFTER pagamento"),
            ("note",           "VARCHAR(2000) NOT NULL DEFAULT '' AFTER data_pagamento")
        };
        foreach ((string Column, string Definition) col in salRowColumns)
        {
            bool hasColumn = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = 'sal_rows'
                  AND column_name = @Column", new { col.Column }) > 0;
            if (!hasColumn)
            {
                c.Execute($"ALTER TABLE sal_rows ADD COLUMN {col.Column} {col.Definition}");
                log.LogInformation("[Migration v21] Aggiunta colonna sal_rows.{Column}", col.Column);
            }
        }

        // Indice per warning incasso / prospetto (pagamento + data fattura)
        bool hasIndex = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = 'sal_rows'
              AND index_name = 'idx_salrow_pag_saldo'") > 0;
        if (!hasIndex)
        {
            c.Execute("ALTER TABLE sal_rows ADD INDEX idx_salrow_pag_saldo (pagamento, data_fatt)");
            log.LogInformation("[Migration v21] Aggiunto indice idx_salrow_pag_saldo a sal_rows");
        }

        // Header esteso project_sal: PO - Ordine cliente + Riferimento Offerta ATEC
        (string Column, string Definition)[] headerColumns = new (string, string)[]
        {
            ("po",          "VARCHAR(150) NOT NULL DEFAULT '' AFTER valore"),
            ("rif_offerta", "VARCHAR(200) NOT NULL DEFAULT '' AFTER po")
        };
        foreach ((string Column, string Definition) col in headerColumns)
        {
            bool hasColumn = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = 'project_sal'
                  AND column_name = @Column", new { col.Column }) > 0;
            if (!hasColumn)
            {
                c.Execute($"ALTER TABLE project_sal ADD COLUMN {col.Column} {col.Definition}");
                log.LogInformation("[Migration v21] Aggiunta colonna project_sal.{Column}", col.Column);
            }
        }

        // Seed anagrafiche (idempotenti: solo se tabella vuota)
        SalDbService.SeedSapCausali(c, log);
        SalDbService.SeedPaymentStates(c, log);

        // Migrazione dati: lo stato legacy 'pagata' si sdoppia in fatturazione 'emessa'
        // + pagamento 'Pagata' (idempotente: al secondo giro non resta nessuna 'pagata').
        int migratedRows = c.Execute("UPDATE sal_rows SET pagamento='Pagata', stato='emessa' WHERE stato='pagata'");
        if (migratedRows > 0)
            log.LogInformation("[Migration v21] {Count} righe sal_rows migrate da stato 'pagata' a emessa + Pagata", migratedRows);

        log.LogInformation("[Migration v21] Schema SAL v10 applicato (colonne, indice, anagrafiche, migrazione stato pagata)");
    }
}
