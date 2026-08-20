using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v46: invariante «codice ATEC = codice_nuovo, sempre» — gli articoli NATI nelle
// famiglie nuove (201/211/221, generatore locale ⇒ remote_id NULL) ricevono
// codice_nuovo = codice, così mapping Danea / orfani / ricerche non hanno bisogno
// di predicati sul formato. Da qui in poi ci pensa ConfirmReservation.
public sealed class M046_CodiceNuovoFamiglieNuove : IMigrazione
{
    public int Versione => 46;

    public string Descrizione => "codex_items: codice_nuovo = codice per gli articoli nati nelle famiglie nuove";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, quegli articoli restano con codice_nuovo vuoto: il mapping Danea non li
    // risolve e compaiono nell'elenco «da ricodificare» pur avendo già un codice nuovo — se
    // qualcuno gliene assegnasse un altro sarebbe un secondo codice per lo stesso articolo.
    // Resta comunque una pulizia: nessun conto sbaglia, e al riavvio dopo si ritenta da sola.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            UPDATE codex_items
            SET codice_nuovo = codice
            WHERE remote_id IS NULL
              AND (codice_nuovo IS NULL OR codice_nuovo = '')
              AND codice REGEXP '^(201|211|221)[0-9]{9}$'");
        log.LogInformation("[Migration v46] codice_nuovo allineato per gli articoli nati nelle famiglie nuove");
    }
}
