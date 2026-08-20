using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v39: matrice degli avanzamenti di stato DDP v7 (MATRICE_STATI_DDP_V7 + relazione tecnica
// DDP-MATRICE-STATI del 20/07/2026). Tre interventi:
//  1) consolidamento stati legacy sulle righe: CON/COS/SPED → DISP, MOD → RAM
//     (bom_items + ddp_officina_items) + disattivazione dei 4 stati + etichetta DISP v7;
//  2) pulizia ddp_aggregation_states dalle chiavi legacy (le righe non le porteranno più);
//  3) seed one-shot della matrice transizioni (l'editor in Conf. DDP la modifica liberamente).
public sealed class M039_MatriceStatiDdp : IMigrazione
{
    public int Versione => 39;

    public string Descrizione => "matrice stati DDP v7: transizioni + consolidamento CON/COS/SPED/MOD";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int remapped = 0;
        remapped += c.Execute("UPDATE bom_items SET item_status='DISP', updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) IN ('CON','COS','SPED')");
        remapped += c.Execute("UPDATE bom_items SET item_status='RAM',  updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) = 'MOD'");
        remapped += c.Execute("UPDATE ddp_officina_items SET item_status='DISP', updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) IN ('CON','COS','SPED')");
        remapped += c.Execute("UPDATE ddp_officina_items SET item_status='RAM',  updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) = 'MOD'");

        c.Execute("UPDATE ddp_statuses SET is_active=FALSE WHERE status_key IN ('CON','COS','SPED','MOD')");
        c.Execute("UPDATE ddp_statuses SET label='DISPONIBILE / CONSEGNATO' WHERE status_key='DISP' AND label='DISPONIBILE'");

        c.Execute("DELETE FROM ddp_aggregation_states WHERE status_key IN ('CON','COS','SPED','MOD')");

        // Matrice v7: riga INIZIO (nessuno stato) non è memorizzata → finestra completa.
        // ('ANN','') e ('SOST','') = terminali governati senza uscite.
        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (from_key, to_key) VALUES
            ('VER','CHEK'),('VER','RO'),('VER','DO'),('VER','IO'),('VER','DC'),('VER','DISP'),('VER','SOSP'),('VER','ANN'),('VER','SOST'),
            ('CHEK','RO'),('CHEK','DO'),('CHEK','IO'),('CHEK','DC'),('CHEK','DISP'),('CHEK','SOSP'),('CHEK','ANN'),('CHEK','SOST'),
            ('RO','CHEK'),('RO','DO'),('RO','IO'),('RO','DC'),('RO','SOSP'),('RO','ANN'),('RO','SOST'),
            ('DO','IO'),('DO','SOSP'),('DO','ANN'),('DO','SOST'),
            ('IO','DC'),('IO','MIT'),('IO','PAR'),('IO','DISP'),('IO','RAM'),('IO','ASS'),('IO','SOSP'),('IO','ANN'),('IO','SOST'),
            ('DC','MIT'),('DC','PAR'),('DC','DISP'),('DC','RAM'),('DC','ASS'),('DC','SOSP'),('DC','ANN'),('DC','SOST'),
            ('MIT','PAR'),('MIT','DISP'),('MIT','RAM'),('MIT','ASS'),('MIT','SOSP'),('MIT','ANN'),('MIT','SOST'),
            ('PAR','DISP'),('PAR','RAM'),('PAR','ASS'),('PAR','SOSP'),('PAR','ANN'),('PAR','SOST'),
            ('DISP','MIT'),('DISP','PAR'),('DISP','RAM'),('DISP','ASS'),('DISP','SOSP'),('DISP','ANN'),('DISP','SOST'),
            ('RAM','VER'),('RAM','CHEK'),('RAM','DISP'),('RAM','SOSP'),('RAM','ANN'),('RAM','SOST'),
            ('ASS','RAM'),('ASS','SOSP'),('ASS','ANN'),('ASS','SOST'),
            ('SOSP','VER'),('SOSP','CHEK'),('SOSP','RO'),('SOSP','DO'),('SOSP','IO'),('SOSP','DC'),('SOSP','MIT'),('SOSP','PAR'),('SOSP','DISP'),('SOSP','ASS'),('SOSP','ANN'),('SOSP','SOST'),
            ('ANN',''),
            ('SOST','')");

        log.LogInformation("[Migration v39] Matrice stati DDP v7 seedata; {Remapped} righe rimappate (CON/COS/SPED→DISP, MOD→RAM)", remapped);
    }
}
