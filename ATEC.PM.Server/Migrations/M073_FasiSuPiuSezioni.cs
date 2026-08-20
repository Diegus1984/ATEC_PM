using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// ── v73 — FASI MULTI-SEZIONE (libreria unica di fasi) ────────────────────────
// Una fase dell'anagrafica può stare su PIÙ sezioni di costo: «Call Cliente» sotto
// Program Manager e sotto Progettazione, e in commessa nasce una volta per sezione, così
// le ore restano separate per sezione nel Bilancio. Prima serviva creare tre fasi con lo
// stesso nome: è l'origine dei doppioni dell'anagrafica. Vedi PIANO-FASI-MULTISEZIONE.md.
//
// Tre passi, e l'ordine conta:
//   a) la tabella dei legami;
//   b) un legame per ogni fase che oggi ha una sezione (la JOIN scarta i riferimenti
//      rotti: la v70 li ha già azzerati, ma un dump ripristinato può riportarli e qui
//      farebbero fallire l'INSERT per violazione di FK);
//   c) **il passo che rende sicuro tutto il resto**: congela lo snapshot della sezione
//      sulle fasi di commessa che oggi vivono di fallback. Sei letture in giro fanno
//      COALESCE(pp.cost_section_template_id, pt.cost_section_template_id) — vista
//      v_timesheet_with_section compresa. Da quando una fase avrà N sezioni, quel
//      fallback restituirebbe UNA SEZIONE A CASO, senza nessun errore: le ore di una
//      commessa finirebbero nella ripartizione sbagliata del Bilancio.
//
// Il conteggio finale delle fasi rimaste senza sezione è l'unico modo per accorgersi
// se un numero del Bilancio si muove: sono le ore che restano fuori dalla ripartizione.
public sealed class M073_FasiSuPiuSezioni : IMigrazione
{
    public int Versione => 73;

    public string Descrizione => "phase_template_sections: fasi di anagrafica su piu sezioni di costo + snapshot della sezione congelato sulle fasi di commessa";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(PhaseTemplateSectionsDdl);

        int legami = c.Execute(@"
            INSERT IGNORE INTO phase_template_sections
                (phase_template_id, cost_section_template_id, sort_order, is_default)
            SELECT pt.id, pt.cost_section_template_id, pt.sort_order, pt.is_default
            FROM phase_templates pt
            JOIN cost_section_templates cst ON cst.id = pt.cost_section_template_id");

        int congelate = c.Execute(@"
            UPDATE project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            SET pp.cost_section_template_id = pt.cost_section_template_id
            WHERE pp.cost_section_template_id IS NULL
              AND pt.cost_section_template_id IS NOT NULL");

        int senzaSezione = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_phases WHERE cost_section_template_id IS NULL");

        log.LogInformation(
            "[Migration v73] Fasi multi-sezione: {Legami} legami fase→sezione creati dall'anagrafica esistente, {Congelate} fasi di commessa hanno ora la sezione scritta sulla riga. Restano {SenzaSezione} fasi di commessa senza sezione: le loro ore non entrano nella ripartizione per sezione del Bilancio.",
            legami, congelate, senzaSezione);
    }
}
