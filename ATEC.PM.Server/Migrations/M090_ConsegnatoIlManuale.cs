using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #82 — «Consegnato il» diventa una data scrivibile come Data Richiesta / Data ordine.
///
/// Prima era solo ricavata dalla cronistoria (ultimo passaggio a CON/COS/DISP). Ora ha una colonna
/// propria: si edita in griglia, e in migrazione si riempie dalle date già presenti negli eventi
/// così le righe già chiuse non perdono il valore a video.
/// </summary>
public sealed class M090_ConsegnatoIlManuale : IMigrazione
{
    public int Versione => 90;

    public string Descrizione =>
        "ddp_officina_items.delivered_at: Consegnato il editabile (#82)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'delivered_at'");
        if (hasCol == 0)
        {
            c.Execute(@"ALTER TABLE ddp_officina_items
                ADD COLUMN delivered_at DATE NULL AFTER order_date");
            log.LogInformation("[Migration v90] Colonna delivered_at aggiunta.");
        }

        // Backfill: stesso calcolo che faceva la SELECT (ultimo passaggio a chiusura positiva).
        int filled = c.Execute(@"
            UPDATE ddp_officina_items o
            SET delivered_at = (
                SELECT MAX(DATE(ev.changed_at)) FROM ddp_item_events ev
                WHERE ev.item_type = 'OFFICINA' AND ev.item_id = o.id
                  AND ev.to_status IN ('CON','COS','DISP')
            )
            WHERE o.delivered_at IS NULL
              AND EXISTS (
                SELECT 1 FROM ddp_item_events ev
                WHERE ev.item_type = 'OFFICINA' AND ev.item_id = o.id
                  AND ev.to_status IN ('CON','COS','DISP')
              )");
        log.LogInformation("[Migration v90] Backfill delivered_at da cronistoria: {N} righe.", filled);
    }
}
