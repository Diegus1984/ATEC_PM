using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #139 — «Consegnato il» diventa editabile anche per la DDP Commerciale (bom_items),
/// con la stessa configurazione di Data Prevista.
/// </summary>
public sealed class M118_ConsegnatoIlCommerciale : IMigrazione
{
    public int Versione => 118;

    public string Descrizione =>
        "bom_items.delivered_at: Consegnato il editabile anche in DDP Commerciale (#139)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool col = AddColumnIfMissing(c, "bom_items", "delivered_at", "DATE NULL AFTER date_needed");

        // Backfill: ultimo passaggio a DISP da cronistoria
        int filled = c.Execute(@"
            UPDATE bom_items b
            SET delivered_at = (
                SELECT MAX(DATE(ev.changed_at)) FROM ddp_item_events ev
                WHERE ev.item_type = 'COMMERCIAL' AND ev.item_id = b.id
                  AND ev.to_status = 'DISP'
            )
            WHERE b.delivered_at IS NULL
              AND EXISTS (
                SELECT 1 FROM ddp_item_events ev
                WHERE ev.item_type = 'COMMERCIAL' AND ev.item_id = b.id
                  AND ev.to_status = 'DISP'
              )");

        log.LogInformation("[Migration v118] bom_items.delivered_at aggiunta={Col}, backfill={N} righe.", col, filled);
    }
}
