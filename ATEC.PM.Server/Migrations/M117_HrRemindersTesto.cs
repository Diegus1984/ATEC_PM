using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M117 — la Cronologia Email (PIANO-HR-PORT-ORIGINALE.md, A1).
///
/// <para>Fino a qui <c>hr_reminders</c> rispondeva a una domanda sola: «questo buco l'ho
/// già chiesto?». Bastava per accendere il tooltip sul calendario, non per rileggere la
/// mail: il <c>MailLog</c> del programma «Timbrature» conservava anche destinatario,
/// oggetto e corpo, ed è quello che la Cronologia deve mostrare.</para>
///
/// <para>🪤 Le righe già scritte restano <b>senza testo</b>: chi legge la Cronologia deve
/// dire «testo non conservato», non far finta che sia stata spedita una mail vuota.</para>
///
/// <para>🪤 La chiave unica <c>(employee_id, work_date)</c> <b>resta</b>: il secondo
/// sollecito sulla stessa giornata aggiorna la riga invece di accodarne una. La domanda
/// vera è «questo buco l'ho già chiesto?», non «quante volte». Per la storia di TUTTE le
/// mail servirebbe una tabella a parte — non è quello che è stato chiesto.</para>
/// </summary>
public sealed class M117_HrRemindersTesto : IMigrazione
{
    public int Versione => 117;

    public string Descrizione =>
        "HR: hr_reminders conserva destinatario, oggetto e corpo del sollecito (Cronologia Email)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool email = AddColumnIfMissing(c, "hr_reminders", "email",
            "VARCHAR(200) NULL COMMENT 'Indirizzo a cui è stato spedito il sollecito' AFTER channel");

        bool oggetto = AddColumnIfMissing(c, "hr_reminders", "subject",
            "VARCHAR(300) NULL COMMENT 'Oggetto della mail' AFTER email");

        bool corpo = AddColumnIfMissing(c, "hr_reminders", "body",
            "TEXT NULL COMMENT 'Corpo della mail; NULL = riga scritta prima della M117' AFTER subject");

        log.LogInformation(
            "[M117] hr_reminders: email={E}, subject={O}, body={C} — la Cronologia Email può rileggere il testo.",
            email, oggetto, corpo);
    }
}
