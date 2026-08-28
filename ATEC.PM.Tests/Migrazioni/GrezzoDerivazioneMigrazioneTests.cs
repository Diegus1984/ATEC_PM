using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Segnalazione #135 — le colonne su cui si regge il grezzo di un particolare a disegno.
///
/// <para>Il test parte dalla forma <b>vecchia</b> della tabella: su uno schema già aggiornato la
/// M116 non farebbe niente e il test resterebbe verde senza provare nulla.</para>
///
/// <para>La seconda passata è l'altra metà del lavoro: una migrazione che fallisce viene
/// <b>ritentata al riavvio</b>, e sul database di sviluppo gira accanto a uno schema che le
/// colonne ce le ha già.</para>
/// </summary>
public class GrezzoDerivazioneMigrazioneTests
{
    private static readonly string[] ColonneNuove =
        ["raw_codex_code", "raw_auto_qty", "raw_sources", "raw_internal_share"];

    [FactRichiedeMySql]
    public void La_migrazione_aggiunge_le_colonne_del_grezzo_e_si_puo_rifare()
    {
        using var db = new DatabaseDiProva("grezzo116");
        db.CreaSchemaCompleto(); // qui la M116 è già passata
        using MySqlConnection c = db.Apri();

        // ── si torna alla forma pre-#135 ──
        foreach (string colonna in ColonneNuove)
            c.Execute($"ALTER TABLE bom_items DROP COLUMN `{colonna}`");
        c.Execute("DROP INDEX idx_bom_raw_codex ON bom_items");
        Assert.Equal(0, ColonnePresenti(c));

        // ── la migrazione ──
        new M116_GrezzoDerivazione101().Applica(c, NullLogger.Instance);

        Assert.Equal(ColonneNuove.Length, ColonnePresenti(c));
        Assert.Equal(1, c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'bom_items'
              AND index_name = 'idx_bom_raw_codex'"));

        // ── e la si può rifare: nessuna eccezione, niente di doppio ──
        new M116_GrezzoDerivazione101().Applica(c, NullLogger.Instance);
        Assert.Equal(ColonneNuove.Length, ColonnePresenti(c));
    }

    /// <summary>
    /// La tabella della derivazione nasce col bootstrap dello schema, che su un database già
    /// popolato non gira: la migrazione se la crea da sé se manca, o dalla #135 in poi il
    /// ricalcolo del grezzo troverebbe «Unknown table».
    /// </summary>
    [FactRichiedeMySql]
    public void La_migrazione_ricrea_la_tabella_della_derivazione_se_manca()
    {
        using var db = new DatabaseDiProva("grezzoref116");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();

        c.Execute("DROP TABLE codex_item_references");

        new M116_GrezzoDerivazione101().Applica(c, NullLogger.Instance);

        Assert.Equal(1, c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'codex_item_references'"));
        // La UNIQUE è quella che tiene «un particolare, un solo grezzo».
        Assert.Equal(1, c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'codex_item_references'
              AND index_name = 'uq_source_ref'"));
    }

    private static int ColonnePresenti(MySqlConnection c) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'bom_items'
              AND column_name IN ('raw_codex_code','raw_auto_qty','raw_sources','raw_internal_share')");
}
