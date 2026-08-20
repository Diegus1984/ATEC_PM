using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #93 — l'elenco delle segnalazioni smette di essere condiviso: ognuno vede solo
/// le proprie, la vista completa resta all'amministratore e a Paolo Zanoni.
///
/// <para>La chiave nuova è <c>data.bug_reports_all</c>. Chi gestisce le segnalazioni
/// (<c>action.manage_bug_reports</c>) continua a vedere tutto anche senza questa chiave — lo
/// decide il controller (<c>VedeTutte</c>) — quindi qui si semina solo l'eccezione nominativa
/// chiesta dalla segnalazione.</para>
///
/// <para>⚠️ La semina non è un contorno: col fallback invertito (v85) una chiave nuova nasce
/// spenta per CHIUNQUE. L'ADMIN passa comunque dal jolly <c>*</c>, ma Paolo Zanoni il jolly non
/// ce l'ha: se il filtro del controller arrivasse in produzione senza questa riga, proprio lui
/// perderebbe la vista completa nello stesso deploy che la doveva riservare a lui. Per lo stesso
/// motivo la migrazione <b>non è <c>Facoltativa</c></b>: se fallisse col server che parte lo
/// stesso, il filtro sarebbe attivo con la chiave non seminata.</para>
///
/// <para>Origin <c>MANO</c>, non <c>CLASSE</c>: è una concessione alla persona, così «Applica
/// classe» dalla pagina Permessi non gliela porta via. Il lookup è per username e non fallisce
/// se l'utenza non esiste (INSERT … SELECT): su un database senza Zanoni — quello di sviluppo —
/// la migrazione registra la chiave e basta.</para>
/// </summary>
public sealed class M097_VisibilitaSegnalazioni : IMigrazione
{
    public int Versione => 97;

    public string Descrizione =>
        "#93: segnalazioni visibili solo all'autore; vista completa (data.bug_reports_all) a ADMIN (jolly) e Paolo Zanoni";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // `min_level` è solo descrittivo dalla Fase E: 3 = il vecchio livello ADMIN, come
        // action.manage_bug_reports (v85). HIDDEN: a chi non ha la chiave il filtro
        // «Di tutti / Solo le mie» sparisce dalla pagina, non appare spento.
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
            ('data.bug_reports_all', 'Segnalazioni — vede quelle di tutti', 'data', 3, 'HIDDEN')");

        int righe = c.Execute(@"
            INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT e.id, 'data.bug_reports_all', 'FULL', 'MANO'
            FROM employees e
            WHERE e.username = 'paolo.zanoni'");

        log.LogInformation(
            "[Migration v97] #93: chiave data.bug_reports_all registrata, {Righe} concessione/i nominative seminate (paolo.zanoni; l'ADMIN passa dal jolly '*').",
            righe);
    }
}
