using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M133 — il numero di protocollo della mutua sulle giornate di malattia.
///
/// <para>Diego, 10/09/2026: «quando segnamo una giornata come malattia dobbiamo inserire il
/// numero di protocollo della mutua». Un certificato copre più giorni ma le giornate si
/// segnano una per volta, e ogni giornata è già una riga di <c>hr_absences</c>: il protocollo
/// sta su ognuna, e i giorni dello stesso certificato portano lo stesso numero. Così non serve
/// nessuna tabella nuova, e togliere una giornata non lascia in giro un certificato orfano.</para>
///
/// <para>Il numero resta TESTO: quello del foglio di agosto ha nove cifre (453206692), ma il
/// formato lo decide la mutua e non è affare nostro. Facoltativo (Diego: «il protocollo si può
/// aggiungere anche dopo»): la malattia si segna subito, il numero arriva quando arriva.</para>
/// </summary>
public sealed class M133_HrProtocolloMutua : IMigrazione
{
    public int Versione => 133;

    public string Descrizione => "HR: hr_absences.sickness_protocol, il protocollo della mutua";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool aggiunta = AiutiMigrazione.AddColumnIfMissing(
            c, "hr_absences", "sickness_protocol",
            "VARCHAR(40) NULL COMMENT 'Numero di protocollo della mutua, sulle giornate di malattia'");

        log.LogInformation(
            aggiunta
                ? "[M133] hr_absences.sickness_protocol aggiunta: il protocollo della mutua ha dove stare."
                : "[M133] hr_absences.sickness_protocol c'era già.");
    }
}
