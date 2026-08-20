using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// ── v70 — BONIFICA dei riferimenti rotti alle sezioni di costo ────────────────
// In produzione 21 delle 54 fasi di anagrafica puntavano a sezioni di costo
// INESISTENTI (id 1..12, mentre le sezioni vere partono da 41): le sezioni sono state
// ricreate da zero e le fasi sono rimaste appese ai vecchi id. La FK esiste ed è
// ON DELETE SET NULL, quindi una cancellazione normale li avrebbe azzerati da sola:
// gli orfani possono essere entrati solo con FOREIGN_KEY_CHECKS=0, cioè da un
// ripristino di dump. Nessuno se n'era accorto perché il codice legge sempre in
// LEFT JOIN e un riferimento rotto si comporta come «nessuna sezione», in silenzio.
//
// Qui si azzerano: il dato dice la verità e le fasi ricompaiono nel pannello
// «Fasi senza sezione» di Configurazione sezioni, da dove si possono riassegnare.
// NON si indovina la sezione giusta: il rimappaggio è una scelta di anagrafica.
// Le altre tre tabelle sono a 0 orfani oggi, ma sono esposte allo stesso incidente.
public sealed class M070_BonificaSezioniOrfane : IMigrazione
{
    public int Versione => 70;

    public string Descrizione => "Bonifica riferimenti rotti alle sezioni di costo (fasi di anagrafica orfane dopo un ripristino di dump)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int fasi = c.Execute(@"
            UPDATE phase_templates pt
            LEFT JOIN cost_section_templates cst ON cst.id = pt.cost_section_template_id
            SET pt.cost_section_template_id = NULL
            WHERE pt.cost_section_template_id IS NOT NULL AND cst.id IS NULL");

        int fasiCommessa = c.Execute(@"
            UPDATE project_phases pp
            LEFT JOIN cost_section_templates cst ON cst.id = pp.cost_section_template_id
            SET pp.cost_section_template_id = NULL
            WHERE pp.cost_section_template_id IS NOT NULL AND cst.id IS NULL");

        int sezioniCommessa = c.Execute(@"
            UPDATE project_cost_sections pcs
            LEFT JOIN cost_section_templates cst ON cst.id = pcs.template_id
            SET pcs.template_id = NULL
            WHERE pcs.template_id IS NOT NULL AND cst.id IS NULL");

        int sezioniOfferta = c.Execute(@"
            UPDATE quote_cost_sections qcs
            LEFT JOIN cost_section_templates cst ON cst.id = qcs.template_id
            SET qcs.template_id = NULL
            WHERE qcs.template_id IS NOT NULL AND cst.id IS NULL");

        log.LogInformation(
            "[Migration v70] Riferimenti rotti alle sezioni di costo azzerati: {Fasi} fasi di anagrafica, {FasiCommessa} fasi di commessa, {SezioniCommessa} sezioni di commessa, {SezioniOfferta} sezioni di offerta. Le fasi scollegate vanno riassegnate a mano da Configurazione sezioni.",
            fasi, fasiCommessa, sezioniCommessa, sezioniOfferta);
    }
}
