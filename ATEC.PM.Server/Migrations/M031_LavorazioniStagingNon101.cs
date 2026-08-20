using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v31: Lavorazioni = solo particolari a disegno (prefisso Codex 101).
// Rimuove le bozze staging nate da righe DDP Officina con altri prefissi
// (201/301/401/501/…); le promosse restano (nascoste dalle GET).
public sealed class M031_LavorazioniStagingNon101 : IMigrazione
{
    public int Versione => 31;

    public string Descrizione => "lavorazioni: elimina staging non-101 da DDP Officina";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, restano delle bozze di lavorazione in più nell'elenco. Sono righe
    // normali di project_work_requests: nessuno schema le presuppone assenti.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        int deleted = c.Execute(@"
            DELETE wr FROM project_work_requests wr
            JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
            WHERE wr.is_staging = 1
              AND REPLACE(REPLACE(COALESCE(o.part_number, ''), '.', ''), ' ', '') NOT LIKE '101%'");

        log.LogInformation("[Migration v31] Eliminate {Count} bozze lavorazioni non-101", deleted);
    }
}
