using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v77 — «Ore lavorazione» sulle righe della DDP Officine (segnalazione #54).
// Il costo unitario di un pezzo costruito in casa non si digita a naso: si imputano le
// ORE e le si moltiplica per la tariffa oraria delle officine interne (anagrafica
// tariffe, tipo HOURLY_RATE). Le ore restano scritte sulla riga — servono a spiegare
// da dove esce il costo e, un domani, a confrontarle con le ore vere.
// NULL = riga senza ore imputate (≠ zero ore, che è un dato).
public sealed class M077_OreLavorazioneOfficina : IMigrazione
{
    public int Versione => 77;

    public string Descrizione => "ddp_officina_items.work_hours: ore di lavorazione per il costo unitario delle officine interne";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool haColonna = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'work_hours'") > 0;
        if (!haColonna)
        {
            c.Execute("ALTER TABLE ddp_officina_items ADD COLUMN work_hours DECIMAL(10,2) NULL AFTER quantity_produced");
        }

        log.LogInformation("[Migration v77] Colonna work_hours sulle righe officina{Gia}.", haColonna ? " (c'era gia)" : "");
    }
}
