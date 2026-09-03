using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #147 (punto 3): la foto del piano (<c>res_plan_snapshots</c>) ha al massimo UNA
/// riga per allocazione dentro lo stesso batch, garantito dal database e non più solo dal codice
/// di <c>PlanNotificationService</c> (che i doppioni li toglieva a mano, eredità del vecchio
/// <c>ON DUPLICATE KEY UPDATE</c> senza chiave unica).
///
/// <para>Prima della chiave si eliminano gli eventuali doppioni già presenti: resta la riga più
/// recente (id più alto), cioè l'ultima scrittura, che è quella con il contenuto aggiornato.
/// In produzione il 03/09/2026 non ce n'erano (190 righe = 190 coppie): il passo è una rete di
/// sicurezza per non far fallire l'ALTER. Un database nuovo la tabella la crea già con la chiave
/// (<c>ResourcesDbService.InitTables</c>): qui non si fa niente.</para>
/// </summary>
public sealed class M120_ChiaveUnicaFotoPiano : IMigrazione
{
    public int Versione => 120;

    public string Descrizione =>
        "res_plan_snapshots: chiave unica (batch_id, assignment_id), doppioni tolti prima";

    public const string NomeChiave = "uq_snap_batch_assignment";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool tabella = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'res_plan_snapshots'") > 0;
        if (!tabella)
        {
            log.LogInformation("[Migration v120] res_plan_snapshots non esiste ancora: la crea InitTables già con la chiave.");
            return;
        }

        int doppioni = c.Execute(@"
            DELETE s1 FROM res_plan_snapshots s1
            JOIN res_plan_snapshots s2
              ON s2.batch_id = s1.batch_id AND s2.assignment_id = s1.assignment_id AND s2.id > s1.id",
            commandTimeout: 600);

        bool chiave = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'res_plan_snapshots' AND index_name = @Nome",
            new { Nome = NomeChiave }) > 0;
        if (!chiave)
            c.Execute($"ALTER TABLE res_plan_snapshots ADD UNIQUE KEY {NomeChiave} (batch_id, assignment_id)", commandTimeout: 600);

        log.LogInformation("[Migration v120] res_plan_snapshots: {Doppioni} doppioni tolti, chiave unica {Stato}.",
            doppioni, chiave ? "già presente" : "creata");
    }
}
