using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #78 — la chat esce dalla commessa.
///
/// <para><b>1. <c>project_chats.project_id</c> diventa facoltativo.</b> Nella finestra «Nuova chat»
/// il primo campo sceglie commessa, altra attività oppure «— Nessuna —»: quest'ultima ha bisogno di
/// una riga senza commessa, che oggi la colonna NOT NULL vieta. La chiave esterna resta (con
/// <c>ON DELETE CASCADE</c>): una commessa eliminata continua a portarsi via le sue chat, e le
/// righe con <c>NULL</c> non sono vincolate a niente.</para>
///
/// <para><b>2. La Chat va a tutti.</b> Il menu principale deve mostrarla a chiunque entri nel
/// gestionale. Col motore dei permessi sulla persona questo vuol dire una riga <c>project.chat</c>
/// per ognuno: chi ce l'ha già resta com'è, i due dinieghi della Contabilità (nati come «assenza di
/// riga» e trasformati in <c>NO</c> dalla v87) diventano concessioni piene. Il pacchetto della
/// classe la contiene già per TECH/RESP_REPARTO/PM, quindi un «Applica classe» non la toglie più;
/// l'ADMIN passa dal jolly.</para>
/// </summary>
public sealed class M088_ChatSenzaCommessa : IMigrazione
{
    public int Versione => 88;

    public string Descrizione =>
        "Chat senza commessa (project_chats.project_id nullable) + funzione project.chat concessa a tutti (#78)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. project_id facoltativo ────────────────────────────────────────────────
        //
        // MySQL non lascia cambiare in NULL una colonna sotto chiave esterna senza toglierla
        // prima: si stacca il vincolo, si modifica, lo si rimette. Tutto idempotente, così un
        // ritentativo dopo un fallimento a metà non trova la strada sbarrata.
        bool nullable = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'project_chats'
              AND column_name = 'project_id' AND is_nullable = 'YES'") > 0;

        if (!nullable)
        {
            string? fk = c.ExecuteScalar<string?>(@"
                SELECT constraint_name FROM information_schema.key_column_usage
                WHERE table_schema = DATABASE() AND table_name = 'project_chats'
                  AND column_name = 'project_id' AND referenced_table_name = 'projects'
                LIMIT 1");

            if (!string.IsNullOrEmpty(fk))
                c.Execute($"ALTER TABLE project_chats DROP FOREIGN KEY `{fk}`");

            c.Execute("ALTER TABLE project_chats MODIFY COLUMN project_id INT NULL");

            c.Execute(@"ALTER TABLE project_chats
                ADD CONSTRAINT fk_project_chats_project
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE");

            log.LogInformation("[Migration v88] project_chats.project_id ora accetta NULL (chat senza commessa).");
        }

        // ── 2. La Chat a tutti quelli che entrano nel gestionale ─────────────────────
        //
        // Solo chi ha un utente (username valorizzato): gli anagrafici senza accesso non
        // devono comparire nella pagina dei permessi con una riga in più.
        int concesse = c.Execute(@"
            INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT e.id, 'project.chat', 'FULL', 'MANO'
            FROM employees e
            WHERE COALESCE(e.username,'') <> ''
            ON DUPLICATE KEY UPDATE
                access = 'FULL',
                origin = CASE WHEN employee_feature_access.access = 'NO' THEN 'MANO' ELSE employee_feature_access.origin END");

        // Anche nei pacchetti di classe, per le classi che non la avessero: «Applica classe»
        // non deve poter rispegnere una funzione che ormai è di tutti.
        c.Execute(@"
            INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
            SELECT DISTINCT class_name, 'project.chat', 'FULL' FROM auth_class_features");

        log.LogInformation(
            "[Migration v88] Funzione «project.chat» concessa a tutti gli utenti del gestionale ({Righe} righe scritte/aggiornate).",
            concesse);
    }
}
