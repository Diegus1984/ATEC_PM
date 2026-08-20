using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// BUG-014 — la notifica «Ore anomale» rinasceva a ogni giro del controllo (fino a 8 copie).
///
/// <para><b>Il difetto.</b> Il dedup di quel controllo — l'unico fra gli otto — cercava una
/// notifica già creata <i>nella giornata di <c>work_date</c></i>. Ma le ore si registrano quasi
/// sempre il giorno dopo: la notifica nasceva oggi, la ricerca la cercava ieri, non la trovava
/// mai e ne creava un'altra ogni 6 ore finché quella giornata restava nella finestra di 2
/// giorni.</para>
///
/// <para><b>Perché una colonna e non un dedup «già segnalata oggi»</b> come gli altri controlli.
/// Gli altri sette avvisano di una <i>scadenza</i>, che ha senso ricordare ogni mattina finché
/// non è gestita: lì la chiave giusta è (entità, oggi). Questo avvisa invece di <i>una giornata
/// di lavoro</i> precisa: la chiave è (persona, giorno lavorato). Con il solo riferimento
/// <c>EMPLOYEE + employee_id</c> quella coppia non è esprimibile — due giornate anomale della
/// stessa persona sono lo stesso riferimento — e l'allineamento a <c>CURDATE()</c> avrebbe
/// tolto le copie facendo sparire, in silenzio, la seconda giornata.</para>
///
/// <para>La colonna serve anche a un guasto che c'era <b>già</b> e che nessuno aveva segnalato:
/// la pulizia dei promemoria superati (<c>CleanResolvedNotifications</c>, punto 0) tiene una
/// riga sola per (destinatario, tipo, riferimento) — quindi, appena nasceva l'anomalia del 14,
/// cancellava quella del 13. Da qui in avanti quel raggruppamento include la giornata.</para>
///
/// <para><b>Il backfill legge il testo del messaggio</b>, che ha una forma fissa
/// («12,5h registrate il 13/08/2026»): è l'unico posto dove la giornata sia mai stata scritta.
/// Le notifiche che non combaciano restano con la giornata a NULL e si comportano come prima —
/// una sola per persona. Non è una perdita: sono anomalie già passate, non più ripetibili.</para>
///
/// <para><b>Non è <c>Facoltativa</c></b>: aggiunge una colonna di schema su cui il dedup nuovo
/// si appoggia. Se saltasse, il controllo cercherebbe una colonna che non c'è e il giro delle
/// notifiche andrebbe in errore a ogni ciclo.</para>
/// </summary>
public sealed class M093_AnomalieOrePerGiorno : IMigrazione
{
    public int Versione => 93;

    public string Descrizione =>
        "BUG-014: notifications.reference_date — le anomalie ore deduplicano per persona + giorno lavorato";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. La colonna ────────────────────────────────────────────────────────
        if (AiutiMigrazione.AddColumnIfMissing(c, "notifications", "reference_date",
                "DATE NULL AFTER reference_id"))
            log.LogInformation("[v93] notifications.reference_date aggiunta.");

        // ── 2. La giornata delle notifiche già nate, ricostruita dal messaggio ───
        // Il LIKE con i jolly di posizione (`_` = un carattere) tiene fuori qualunque testo di
        // forma diversa: STR_TO_DATE su una stringa che non è una data scriverebbe NULL con un
        // warning, e a quel punto non si distinguerebbe più «non l'ho capita» da «non c'era».
        int giornateRicostruite = c.Execute(@"
            UPDATE notifications
            SET reference_date = STR_TO_DATE(SUBSTRING_INDEX(message, ' il ', -1), '%d/%m/%Y')
            WHERE notification_type = 'TIMESHEET_ANOMALY'
              AND reference_type = 'EMPLOYEE'
              AND reference_date IS NULL
              AND message LIKE '%h registrate il __/__/____'");
        log.LogInformation("[v93] Giornata ricostruita dal testo su {N} notifiche.", giornateRicostruite);

        // ── 3. Le copie già arrivate alla campanella ─────────────────────────────
        // Una riga sola per (destinatario, persona, giornata): si tiene la più recente, che è
        // anche quella col totale ore aggiornato. Le notifiche di cui il backfill non ha capito
        // la giornata hanno tutte reference_date NULL, quindi si fondono fra loro — è il
        // comportamento di prima, e per il pregresso va bene.
        // `<=>` e non `=`: con NULL l'uguaglianza normale non è mai vera e non cancellerebbe
        // niente proprio dove le copie sono più fitte.
        int copie = c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            JOIN (
                SELECT nr2.employee_id, n2.reference_id, n2.reference_date, MAX(n2.id) AS keep_id
                FROM notification_recipients nr2
                JOIN notifications n2 ON n2.id = nr2.notification_id
                WHERE n2.notification_type = 'TIMESHEET_ANOMALY'
                  AND n2.reference_type = 'EMPLOYEE'
                GROUP BY nr2.employee_id, n2.reference_id, n2.reference_date
                HAVING COUNT(*) > 1
            ) dup ON dup.employee_id = nr.employee_id
                 AND dup.reference_id = n.reference_id
                 AND dup.reference_date <=> n.reference_date
            WHERE n.notification_type = 'TIMESHEET_ANOMALY'
              AND n.reference_type = 'EMPLOYEE'
              AND n.id < dup.keep_id");
        log.LogInformation("[v93] Copie della stessa giornata rimosse dalla campanella: {N}.", copie);

        // Le notifiche rimaste senza nessun destinatario non le vede più nessuno: via.
        int orfane = c.Execute(@"
            DELETE FROM notifications
            WHERE notification_type = 'TIMESHEET_ANOMALY'
              AND reference_type = 'EMPLOYEE'
              AND id NOT IN (SELECT DISTINCT notification_id FROM notification_recipients)");
        log.LogInformation("[v93] Notifiche anomalia ore rimaste senza destinatari: {N} eliminate.", orfane);
    }
}
