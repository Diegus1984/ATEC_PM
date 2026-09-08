using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M125 — la chiave di <c>hr_absence_days</c> comprende il <b>giorno</b>.
///
/// <para>🪤 La M124 dava per scontato che <c>AbsenceRequestRefineID</c> fosse il progressivo
/// del tratto <i>dentro la richiesta</i>. Al primo import in produzione (08/09/2026, 11:17)
/// Ecos ha risposto «Duplicate entry '133095-2'»: il progressivo è del tratto <b>dentro il
/// giorno</b> (1 = mattina, 2 = pomeriggio) e si ripete per ogni giorno di una richiesta a
/// più giorni. La chiave logica è (richiesta, giorno, tratto).</para>
///
/// <para>Niente vincolo UNIQUE: se un domani Ecos mandasse due volte lo stesso tratto,
/// l'import non deve fermarsi — lo specchio deduplica in memoria e l'ultima riga vince.
/// Resta un indice normale per le ricerche per chiave.</para>
/// </summary>
public sealed class M125_HrAbsenceDaysChiave : IMigrazione
{
    public int Versione => 125;

    public string Descrizione =>
        "HR: hr_absence_days, chiave (richiesta, giorno, tratto) senza UNIQUE — RefineID si ripete per ogni giorno";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool vecchio = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'hr_absence_days'
              AND index_name = 'uq_hr_absence_days_ecos'") > 0;
        if (vecchio)
            c.Execute("ALTER TABLE `hr_absence_days` DROP INDEX `uq_hr_absence_days_ecos`");

        bool nuovo = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'hr_absence_days'
              AND index_name = 'idx_hr_absence_days_ecos'") > 0;
        if (!nuovo)
            c.Execute("ALTER TABLE `hr_absence_days` ADD INDEX `idx_hr_absence_days_ecos` (`ecos_absence_id`, `work_date`, `ecos_refine_id`)");

        log.LogInformation("[M125] hr_absence_days: via l'UNIQUE (richiesta, tratto)={V}, indice (richiesta, giorno, tratto) creato={N}.",
            vecchio, !nuovo);
    }
}
