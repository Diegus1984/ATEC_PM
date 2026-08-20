using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #87 — la Stampa 3D entra in DDP Officine e in Lavorazioni Officine.
///
/// <para><b>Le tariffe orarie prendono un nome.</b> Fin qui <c>tariff_options</c> teneva solo
/// un importo, e per le officine interne ce n'era uno solo: il costo unitario si calcolava
/// «ore × la prima tariffa che c'è». Con tre lavorazioni a costo diverso (meccanica 50 €/h,
/// carpenteria 35 €/h, stampa 3D 20 €/h) l'importo da solo non basta più a dire quale
/// lavorazione si sta pagando: serve l'etichetta, ed è quella che si sceglie nella finestra
/// del particolare.</para>
///
/// <para><b>La tariffa scelta resta scritta sulla riga</b> (<c>hourly_rate</c>). Senza,
/// riaprendo un particolare non si saprebbe con quale delle tre è stato fatto il conto, e la
/// prima modifica alle ore lo rifarebbe con la tariffa sbagliata — in silenzio, perché
/// 5 × 20 e 5 × 50 sono due numeri entrambi plausibili. Il backfill la ricostruisce dal
/// rapporto costo/ore <b>solo</b> quando cade esattamente su una tariffa esistente.</para>
///
/// <para><b>Non è <c>Facoltativa</c></b>: le due colonne le legge il codice nuovo (la finestra
/// del particolare e l'anagrafica costi). Senza, la DDP Officina andrebbe in errore in lettura.</para>
/// </summary>
public sealed class M095_Stampa3D : IMigrazione
{
    public int Versione => 95;

    public string Descrizione =>
        "#87 Stampa 3D: tariff_options.label (tariffe orarie con nome) + ddp_officina_items.hourly_rate";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. Il nome della tariffa ─────────────────────────────────────────────
        if (AddColumnIfMissing(c, "tariff_options", "label", "VARCHAR(100) NOT NULL DEFAULT '' AFTER tariff_type"))
            log.LogInformation("[v95] tariff_options.label aggiunta.");

        // ── 2. Le tre tariffe delle officine interne ─────────────────────────────
        // INSERT IGNORE sulla UNIQUE (tariff_type, value): non tocca quelle già a mano.
        // Il nome si scrive a parte, e solo dove manca: se qualcuno ha già battezzato la
        // tariffa da 50 «Officina meccanica», quel nome è più giusto di questo.
        c.Execute(@"INSERT IGNORE INTO tariff_options (tariff_type, value)
            VALUES ('HOURLY_RATE', 20.000), ('HOURLY_RATE', 35.000), ('HOURLY_RATE', 50.000)");

        int nomi = 0;
        nomi += NominaTariffa(c, 50.000m, "Meccanica");
        nomi += NominaTariffa(c, 35.000m, "Carpenteria");
        nomi += NominaTariffa(c, 20.000m, "Stampa 3D");
        log.LogInformation("[v95] Tariffe orarie battezzate: {N}.", nomi);

        // ── 3. La tariffa usata per il costo, sulla riga ─────────────────────────
        if (AddColumnIfMissing(c, "ddp_officina_items", "hourly_rate",
                "DECIMAL(10,2) NULL AFTER work_hours"))
            log.LogInformation("[v95] ddp_officina_items.hourly_rate aggiunta.");

        // Ricostruzione dal conto già fatto: costo unitario ÷ ore. Si scrive solo se il
        // risultato è **esattamente** una tariffa oraria in anagrafica — altrimenti sarebbe
        // un numero inventato che poi la finestra mostrerebbe come se qualcuno l'avesse scelto.
        int ricostruite = c.Execute(@"
            UPDATE ddp_officina_items o
            SET o.hourly_rate = ROUND(o.unit_cost / o.work_hours, 2)
            WHERE o.hourly_rate IS NULL
              AND o.work_hours IS NOT NULL AND o.work_hours > 0
              AND o.unit_cost > 0
              AND EXISTS (
                  SELECT 1 FROM tariff_options t
                  WHERE t.tariff_type = 'HOURLY_RATE'
                    AND t.value = ROUND(o.unit_cost / o.work_hours, 2)
              )");
        log.LogInformation("[v95] Tariffa oraria ricostruita su {N} righe di distinta officina.", ricostruite);
    }

    /// <summary>Scrive il nome della tariffa oraria di quel valore, se non ne ha già uno.</summary>
    private static int NominaTariffa(MySqlConnection c, decimal valore, string nome) =>
        c.Execute(@"UPDATE tariff_options SET label = @Nome
                    WHERE tariff_type = 'HOURLY_RATE' AND value = @Valore AND label = ''",
            new { Nome = nome, Valore = valore });
}
