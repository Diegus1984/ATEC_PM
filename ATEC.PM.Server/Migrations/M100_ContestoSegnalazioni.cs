using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M100 — Migliorie alla gestione delle segnalazioni bug (Lotti L1-L9).
/// Aggiunge:
/// - `bug_reports.context`: testo con metadati tecnici (rotta, query, build, browser, viewport, trace ID errore API);
/// - `bug_reports.fixed_in_build`: stringa con la versione/build in cui è stato risolto il bug;
/// - `bug_reports.archived_at`: data/ora di archiviazione (sostituisce la cancellazione a mano);
/// - `bug_report_attachments.is_reply`: flag 0/1 per distinguere gli allegati della risposta da quelli della segnalazione.
/// </summary>
public sealed class M100_ContestoSegnalazioni : IMigrazione
{
    public int Versione => 100;

    public string Descrizione =>
        "M100: colonne context, fixed_in_build, archived_at su bug_reports; colonna is_reply su bug_report_attachments";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool colContext = AddColumnIfMissing(c, "bug_reports", "context", "TEXT NULL");
        bool colBuild = AddColumnIfMissing(c, "bug_reports", "fixed_in_build", "VARCHAR(40) NULL");
        bool colArchived = AddColumnIfMissing(c, "bug_reports", "archived_at", "DATETIME NULL");
        bool colIsReply = AddColumnIfMissing(c, "bug_report_attachments", "is_reply", "TINYINT(1) NOT NULL DEFAULT 0");

        CreaIndiceSeManca(c, "bug_reports", "idx_bug_archived", "archived_at");

        log.LogInformation(
            "[Migration v100] Segnalazioni estese: context={Context}, fixed_in_build={Build}, archived_at={Archived}, is_reply={IsReply}",
            colContext, colBuild, colArchived, colIsReply);
    }
}
