using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// <c>codex_items.sync_hash</c>: l'impronta (SHA256) della versione remota copiata dall'ultimo
/// sync Codex. Da qui il sync riscrive una riga solo se l'impronta è cambiata, invece di
/// riscriverle tutte a ogni giro (375.016 UPDATE in due giorni misurati in produzione il
/// 04/09/2026, su 20.814 righe). Vedi <c>SpecchioCodex</c>.
///
/// <para>Nasce NULL: al primo giro dopo la migrazione ogni riga viene riscritta una volta
/// (impronta assente ≠ impronta remota) e da lì solo le cambiate. Un database nuovo la colonna
/// ce l'ha già dal <c>CREATE TABLE</c> di <c>DbService</c>.</para>
/// </summary>
public sealed class M121_ImprontaSyncCodex : IMigrazione
{
    public int Versione => 121;

    public string Descrizione => "codex_items.sync_hash: impronta della versione remota (sync solo delle righe cambiate)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool aggiunta = AiutiMigrazione.AddColumnIfMissing(c, "codex_items", "sync_hash", "CHAR(64) NULL AFTER synced_at");
        log.LogInformation("[Migration v121] codex_items.sync_hash {Stato}.", aggiunta ? "aggiunta" : "già presente");
    }
}
