using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v47: righe distinta nate col fornitore vuoto perché il catalogo era monco
// (bug sync fornitori IDAnag, fixato 22/07/2026): ricopia il fornitore
// dell'articolo di catalogo SOLO dove in riga manca. One-shot, idempotente.
public sealed class M047_FornitoreDistintaDaCatalogo : IMigrazione
{
    public int Versione => 47;

    public string Descrizione => "bom_items: backfill supplier_id dal catalogo (righe pre-fix sync fornitori)";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, quelle righe di distinta restano col fornitore vuoto: com'erano
    // prima di questa migrazione, che serve solo a ripararle.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        int n = c.Execute(@"
            UPDATE bom_items b
            JOIN catalog_items ci ON ci.id = b.catalog_item_id
            SET b.supplier_id = ci.supplier_id
            WHERE b.supplier_id IS NULL AND ci.supplier_id IS NOT NULL", commandTimeout: 600);
        log.LogInformation("[Migration v47] Fornitore ricopiato dal catalogo su {Count} righe distinta", n);
    }
}
