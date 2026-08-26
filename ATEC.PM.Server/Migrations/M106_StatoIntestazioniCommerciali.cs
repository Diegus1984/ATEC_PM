using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M106 — Correzione della v105, trovata rileggendo i dati subito dopo il deploy del 25/08/2026.
///
/// <para>Spostando i componenti commerciali, la v105 creava l'intestazione del gruppo in
/// <c>bom_items</c> <b>copiando lo stato dalla riga padre d'officina</b>. Quel padre era in
/// <c>DC</c> («da costruire»), che nella DDP Commerciale <b>non esiste</b>: la matrice degli
/// avanzamenti non ha nemmeno una transizione con <c>DC</c> per il tipo COMMERCIAL. Risultato:
/// una riga bloccata, senza nessuno stato raggiungibile, che il server avrebbe rifiutato di
/// muovere e che nel menu ⋮ non avrebbe offerto niente.</para>
///
/// <para>Non è un dettaglio estetico: era un vicolo cieco silenzioso — nessun errore finché
/// qualcuno non provava a cambiare stato a quella riga.</para>
///
/// <para>Si riportano le intestazioni a <c>DO</c>, che è quello che l'import di tutti i giorni
/// scrive già e che è coerente coi componenti sotto. Tocca le sole righe che sono davvero
/// un'intestazione (hanno almeno un figlio) e che stanno in uno stato estraneo alla matrice
/// commerciale: una riga normale non viene sfiorata.</para>
/// </summary>
public sealed class M106_StatoIntestazioniCommerciali : IMigrazione
{
    public int Versione => 106;

    public string Descrizione =>
        "bom_items: intestazioni di composizione con stato non commerciale riportate a DO";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int corrette = c.Execute(@"
            UPDATE bom_items b
            SET b.item_status = 'DO', b.updated_at = NOW()
            WHERE EXISTS (SELECT 1 FROM (SELECT id, parent_bom_item_id FROM bom_items) f
                          WHERE f.parent_bom_item_id = b.id)
              AND NOT EXISTS (SELECT 1 FROM ddp_status_transitions t
                              WHERE t.ddp_type = 'COMMERCIAL'
                                AND (t.from_key = b.item_status OR t.to_key = b.item_status))");

        log.LogInformation("[Migration v106] Intestazioni di composizione riportate a DO: {Corrette}", corrette);
    }
}
