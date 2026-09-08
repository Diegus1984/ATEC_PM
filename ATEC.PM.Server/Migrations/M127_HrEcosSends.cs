using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M127 — «Invia a Ecos»: il registro degli invii e, su <c>hr_punches</c>, l'orario che Ecos
/// ha dopo un nostro invio.
///
/// <para>Il pulsante manda a Ecos gli orari ARROTONDATI (<c>PeopleStampPost</c> con
/// <c>Edit=true</c>, manuale Ecos §4.2 e §9.3). Ecos non tiene l'ora originale: la storia
/// resta solo qui. Per questo <c>hr_punches.punched_at</c> non si tocca mai — è l'ora
/// timbrata — e accanto nascono <c>ecos_punched_at</c> (l'orario che Ecos ha per colpa
/// nostra) ed <c>ecos_sent_at</c>. Il registro <c>hr_ecos_sends</c> tiene ogni invio con
/// esito, autore, ora originale e ora inviata: è la memoria che manca a Ecos.</para>
/// </summary>
public sealed class M127_HrEcosSends : IMigrazione
{
    public int Versione => 127;

    public string Descrizione =>
        "HR: registro invii a Ecos (hr_ecos_sends) e hr_punches.ecos_punched_at / ecos_sent_at";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool ora = AddColumnIfMissing(c, "hr_punches", "ecos_punched_at",
            "DATETIME NULL COMMENT 'Orario che Ecos ha dopo un nostro Invia a Ecos; NULL = mai inviato, o cambiato poi su Ecos' AFTER location");
        bool quando = AddColumnIfMissing(c, "hr_punches", "ecos_sent_at",
            "DATETIME NULL COMMENT 'Ultimo invio a Ecos di questa timbratura' AFTER ecos_punched_at");

        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_ecos_sends (
                id             BIGINT       NOT NULL AUTO_INCREMENT,
                employee_id    INT          NOT NULL,
                work_date      DATE         NOT NULL,
                punch_id       BIGINT       NULL COMMENT 'hr_punches.id, senza vincolo: la timbratura puo sparire, il registro resta',
                ecos_stamp_id  VARCHAR(50)  NOT NULL COMMENT 'StampID di Ecos',
                direction      VARCHAR(20)  NOT NULL,
                punched_at     DATETIME     NOT NULL COMMENT 'Ora timbrata originale: quella che Ecos perde',
                sent_time      DATETIME     NOT NULL COMMENT 'Ora inviata a Ecos (arrotondata)',
                previous_time  DATETIME     NULL COMMENT 'Ora che Ecos aveva prima di questo invio',
                outcome        VARCHAR(10)  NOT NULL COMMENT 'OK / ERROR',
                message        VARCHAR(500) NULL,
                sent_by        INT          NULL,
                sent_at        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (id),
                KEY idx_hr_ecos_sends_day (employee_id, work_date),
                KEY idx_hr_ecos_sends_punch (punch_id),
                CONSTRAINT fk_hr_ecos_sends_emp FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE,
                CONSTRAINT fk_hr_ecos_sends_by FOREIGN KEY (sent_by)
                    REFERENCES employees (id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

        log.LogInformation("[M127] Invia a Ecos: colonne su hr_punches (orario={A}, data={B}), tabella hr_ecos_sends pronta.", ora, quando);
    }
}
