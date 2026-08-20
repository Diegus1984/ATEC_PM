using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v23: SAL — colori configurabili sugli stati pagamento (sal_payment_states).
// color_bg/color_fg VARCHAR(9) NULL (#RRGGBB o #RRGGBBAA): NULL = stato neutro senza tinta.
// Priorità colore riga nel foglio: colore del pagamento selezionato > giallo 'emessa' > nessuno.
// I colori sono pura estetica: la semantica (lock, incasso, warning, bucket Cash Flow)
// resta cablata sulle etichette di sistema 'Pagata'/'Parzialmente Pagata'.
public sealed class M023_SalColoriStatiPagamento : IMigrazione
{
    public int Versione => 23;

    public string Descrizione => "SAL: colori configurabili stati pagamento";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Nuove colonne colore, una per volta con check di esistenza (pattern v21)
        (string Column, string Definition)[] colorColumns = new (string, string)[]
        {
            ("color_bg", "VARCHAR(9) NULL AFTER is_active"),
            ("color_fg", "VARCHAR(9) NULL AFTER color_bg")
        };
        foreach ((string Column, string Definition) col in colorColumns)
        {
            bool hasColumn = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = 'sal_payment_states'
                  AND column_name = @Column", new { col.Column }) > 0;
            if (!hasColumn)
            {
                c.Execute($"ALTER TABLE sal_payment_states ADD COLUMN {col.Column} {col.Definition}");
                log.LogInformation("[Migration v23] Aggiunta colonna sal_payment_states.{Column}", col.Column);
            }
        }

        // Seed colori di default delle voci di sistema SOLO se ancora NULL (idempotente,
        // non sovrascrive eventuali personalizzazioni): Pagata = verde pastello (parità con
        // l'attuale riga emerald del foglio v10), Parzialmente Pagata = rosso pastello.
        int seeded = c.Execute(@"UPDATE sal_payment_states
            SET color_bg='#D1FAE5', color_fg='#065F46'
            WHERE LOWER(label)='pagata' AND color_bg IS NULL");
        seeded += c.Execute(@"UPDATE sal_payment_states
            SET color_bg='#FEE2E2', color_fg='#991B1B'
            WHERE LOWER(label)='parzialmente pagata' AND color_bg IS NULL");
        if (seeded > 0)
            log.LogInformation("[Migration v23] Colori di default impostati su {Count} stati pagamento di sistema", seeded);
    }
}
