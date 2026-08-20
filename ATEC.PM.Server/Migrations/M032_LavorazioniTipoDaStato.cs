using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v32: bozze lavorazioni — tipo Interna/Esterna one-shot dallo stato DDP Officina.
// DO/RO/IO → External; DC/COS → Internal (+ fornitore ATEC). Solo se type è ancora vuoto.
public sealed class M032_LavorazioniTipoDaStato : IMigrazione
{
    public int Versione => 32;

    public string Descrizione => "lavorazioni: tipo Internal/External one-shot da stato DDP";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, le bozze restano con tipo vuoto — che è il DEFAULT della colonna,
    // uno stato legale e non un guasto.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        int updatedExt = c.Execute(@"
            UPDATE project_work_requests wr
            JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
            SET wr.type = 'External',
                wr.row_version = wr.row_version + 1
            WHERE wr.is_staging = 1
              AND TRIM(COALESCE(wr.type, '')) = ''
              AND UPPER(TRIM(COALESCE(o.item_status, ''))) IN ('DO', 'RO', 'IO')");

        int updatedInt = c.Execute(@"
            UPDATE project_work_requests wr
            JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
            SET wr.type = 'Internal',
                wr.po_supplier = 'ATEC',
                wr.po_number = '',
                wr.row_version = wr.row_version + 1
            WHERE wr.is_staging = 1
              AND TRIM(COALESCE(wr.type, '')) = ''
              AND UPPER(TRIM(COALESCE(o.item_status, ''))) IN ('DC', 'COS')");

        log.LogInformation(
            "[Migration v32] Tipo bozze: {Ext} External, {Int} Internal",
            updatedExt, updatedInt);
    }
}
