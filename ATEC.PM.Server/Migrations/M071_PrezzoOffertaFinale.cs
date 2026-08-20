using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// ── v71 — «Prezzo offerta finale» imputabile a mano (segnalazione #35) ────────
// Fin qui era solo derivato (ultima riga della Scheda Prezzi) e non c'era **nessuna**
// colonna dove scriverlo. Paolo: «Solo valore da mostrare» — quindi l'override NON
// ricalcola Offerta e Contingency, sostituisce soltanto il numero a video.
// NULL = nessun override, si mostra il calcolato: è anche il modo per tornare indietro.
public sealed class M071_PrezzoOffertaFinale : IMigrazione
{
    public int Versione => 71;

    public string Descrizione => "project_pricing.final_price_override: prezzo offerta finale imputabile a mano";

    public void Applica(MySqlConnection c, ILogger log)
    {
        AddColumnIfMissing(c, "project_pricing", "final_price_override", "DECIMAL(14,2) NULL");
        log.LogInformation("[Migration v71] Colonna final_price_override aggiunta a project_pricing.");
    }
}
