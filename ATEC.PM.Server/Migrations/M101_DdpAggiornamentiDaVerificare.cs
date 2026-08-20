using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M101 — La card «DDP Commesse» della Dashboard elenca le DDP aggiornate dai colleghi e le
/// toglie dall'elenco quando chi guarda le ha aperte (segnalazione #114).
///
/// <para>Servono due cose che il database non sapeva:</para>
/// <list type="number">
/// <item><c>updated_by</c> su <c>bom_items</c> e <c>ddp_officina_items</c>: <c>created_by</c>
/// (v81) dice chi ha <b>inserito</b> la riga, non chi l'ha toccata per ultimo. Senza questa
/// colonna l'elenco mostrerebbe a ciascuno anche le proprie modifiche, mentre la #114 chiede
/// le DDP «aggiornate da colleghi».</item>
/// <item><c>ddp_review_acks</c>: la presa visione, <b>per persona</b>. Non è la filigrana
/// condivisa di <c>project_hours_checks</c> (v99, un pulsante «Verifica effettuata» che vale
/// per tutti): qui ognuno ha il suo elenco, e il fatto che il collega abbia già guardato non
/// cancella l'avviso a me.</item>
/// </list>
///
/// <para>Nessun backfill, come per la v99: la tabella nasce vuota, quindi al primo avvio ogni
/// DDP toccata negli ultimi giorni risulta da verificare. È il comportamento giusto — nessuno
/// l'ha ancora aperta con questo strumento — e si azzera aprendo la DDP.</para>
/// </summary>
public sealed class M101_DdpAggiornamentiDaVerificare : IMigrazione
{
    public int Versione => 101;

    public string Descrizione =>
        "ddp_review_acks (presa visione DDP per persona) + updated_by su bom_items e ddp_officina_items";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Stessa forma di created_by (v81), FK non bloccante: se salta, la colonna resta e
        // l'elenco funziona lo stesso — perde solo il vincolo di integrità.
        bool colBom = AddColumnIfMissing(c, "bom_items", "updated_by", "INT NULL AFTER created_by");
        if (colBom)
        {
            try
            {
                c.Execute("ALTER TABLE bom_items ADD CONSTRAINT fk_bom_items_updated_by FOREIGN KEY (updated_by) REFERENCES employees(id) ON DELETE SET NULL");
            }
            catch (Exception ex)
            {
                log.LogWarning("[Migration v101] Errore FK bom_items.updated_by (non bloccante): {Message}", ex.Message);
            }
        }

        bool colOff = AddColumnIfMissing(c, "ddp_officina_items", "updated_by", "INT NULL AFTER created_by");
        if (colOff)
        {
            try
            {
                c.Execute("ALTER TABLE ddp_officina_items ADD CONSTRAINT fk_ddp_officina_items_updated_by FOREIGN KEY (updated_by) REFERENCES employees(id) ON DELETE SET NULL");
            }
            catch (Exception ex)
            {
                log.LogWarning("[Migration v101] Errore FK ddp_officina_items.updated_by (non bloccante): {Message}", ex.Message);
            }
        }

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_review_acks (
            employee_id INT NOT NULL,
            project_id INT NOT NULL,
            ddp_type VARCHAR(12) NOT NULL,
            seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (employee_id, project_id, ddp_type),
            CONSTRAINT fk_dra_project FOREIGN KEY (project_id)
                REFERENCES projects(id) ON DELETE CASCADE,
            INDEX ix_dra_project (project_id, ddp_type)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        log.LogInformation(
            "[Migration v101] Presa visione DDP pronta: updated_by su bom_items={Bom} e ddp_officina_items={Off}; nessuna DDP risulta ancora vista (si azzera aprendola).",
            colBom, colOff);
    }
}
