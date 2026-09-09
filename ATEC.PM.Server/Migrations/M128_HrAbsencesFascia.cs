using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M128 — <c>hr_absences.hour_from</c> / <c>hour_to</c>: la fascia oraria di una richiesta a ore.
///
/// <para>Serve per mandare le richieste su Ecos (#151): <c>PeopleAbsenceRequestPost</c> vuole
/// <c>HourBegin</c>/<c>HourEnd</c> per le richieste non a giornata intera, e finora qui si
/// salvava solo il numero di ore. Per le richieste che arrivano da Ecos la fascia si copia
/// dall'import; per quelle nate qui la mette chi le inserisce (o, per le giustificazioni dal
/// cartellino, la si ricava dalle timbrature della giornata).</para>
/// </summary>
public sealed class M128_HrAbsencesFascia : IMigrazione
{
    public int Versione => 128;

    public string Descrizione => "HR: hr_absences.hour_from / hour_to, la fascia oraria delle richieste a ore (#151)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool da = AddColumnIfMissing(c, "hr_absences", "hour_from",
            "TIME NULL COMMENT 'Inizio della fascia per le richieste a ore (HourBegin di Ecos)' AFTER hours");
        bool a = AddColumnIfMissing(c, "hr_absences", "hour_to",
            "TIME NULL COMMENT 'Fine della fascia per le richieste a ore (HourEnd di Ecos)' AFTER hour_from");
        log.LogInformation("[M128] hr_absences: fascia oraria (hour_from={A}, hour_to={B}).", da, a);
    }
}
