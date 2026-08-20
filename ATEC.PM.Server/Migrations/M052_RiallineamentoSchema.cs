using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v52: riallineamento schema ↔ codice (29/07/2026). Tre gruppi di interventi, tutti
// idempotenti; su un database creato da zero sono già no-op perché le stesse colonne
// sono ora nelle CREATE TABLE di InitDatabase.
//  1) colonne che il codice usa da sempre ma che nessun percorso di creazione produceva
//     (costing commessa: pin/shadow e %imprevisti/%margine sui materiali; colori dei
//     gruppi di costo; snapshot e date sulle fasi): su un DB nuovo Preventivo commessa
//     e Fasi andavano in «Unknown column»;
//  2) project_phases.phase_template_id NULL-able: le fasi LOCALI si inseriscono con
//     template NULL e su un DB nuovo la INSERT falliva;
//  3) pulizia delle foreign key duplicate su quote_material_items.parent_item_id —
//     la ALTER non condizionata in QuoteDbService ne aggiungeva una ad ogni avvio
//     (557 sul DB di sviluppo). Ne resta esattamente una; la ALTER è ora guardata.
public sealed class M052_RiallineamentoSchema : IMigrazione
{
    public int Versione => 52;

    public string Descrizione => "riallineamento schema: colonne costing/fasi mancanti, phase_template_id NULL-able, dedup FK quote_material_items";

    public void Applica(MySqlConnection c, ILogger log)
    {
        (string Table, string Column, string Definition)[] missingColumns = new (string, string, string)[]
        {
            ("cost_section_groups",    "bg_color",           "VARCHAR(10) NOT NULL DEFAULT '#3B82F6' AFTER name"),
            ("cost_section_groups",    "text_color",         "VARCHAR(10) NOT NULL DEFAULT '#FFFFFF' AFTER bg_color"),
            ("project_cost_sections",  "contingency_pinned", "TINYINT(1) NOT NULL DEFAULT 0 AFTER margin_pct"),
            ("project_cost_sections",  "margin_pinned",      "TINYINT(1) NOT NULL DEFAULT 0 AFTER contingency_pinned"),
            ("project_cost_sections",  "is_shadowed",        "TINYINT(1) NOT NULL DEFAULT 0 AFTER margin_pinned"),
            ("project_material_items", "contingency_pct",    "DECIMAL(7,4) NOT NULL DEFAULT 0 AFTER sort_order"),
            ("project_material_items", "margin_pct",         "DECIMAL(7,4) NOT NULL DEFAULT 0 AFTER contingency_pct"),
            ("project_material_items", "contingency_pinned", "TINYINT(1) NOT NULL DEFAULT 0 AFTER margin_pct"),
            ("project_material_items", "margin_pinned",      "TINYINT(1) NOT NULL DEFAULT 0 AFTER contingency_pinned"),
            ("project_material_items", "is_shadowed",        "TINYINT(1) NOT NULL DEFAULT 0 AFTER margin_pinned"),
            ("project_phases",         "name",               "VARCHAR(100) NULL AFTER phase_template_id"),
            ("project_phases",         "category",           "VARCHAR(50) NULL AFTER name"),
            ("project_phases",         "cost_section_template_id", "INT NULL AFTER category"),
            ("project_phases",         "is_local",           "TINYINT(1) NOT NULL DEFAULT 0 AFTER cost_section_template_id"),
            ("project_phases",         "start_date",         "DATE NULL AFTER notes"),
            ("project_phases",         "end_date",           "DATE NULL AFTER start_date"),
        };
        int added = 0;
        foreach ((string Table, string Column, string Definition) col in missingColumns)
        {
            if (AddColumnIfMissing(c, col.Table, col.Column, col.Definition))
            {
                added++;
                log.LogInformation("[Migration v52] Aggiunta colonna {Table}.{Column}", col.Table, col.Column);
            }
        }

        bool templateNullable = c.ExecuteScalar<string?>(@"
            SELECT IS_NULLABLE FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'project_phases'
              AND column_name = 'phase_template_id'") == "YES";
        if (!templateNullable)
        {
            c.Execute("ALTER TABLE project_phases MODIFY phase_template_id INT NULL");
            log.LogInformation("[Migration v52] project_phases.phase_template_id ora NULL-able (fasi locali)");
        }

        // Duplicati FK: si tiene la prima e si eliminano le altre, in blocchi per non
        // generare una ALTER chilometrica su installazioni con centinaia di duplicati.
        List<string> parentFks = c.Query<string>(@"
            SELECT constraint_name FROM information_schema.key_column_usage
            WHERE table_schema = DATABASE()
              AND table_name = 'quote_material_items'
              AND column_name = 'parent_item_id'
              AND referenced_table_name IS NOT NULL
            ORDER BY constraint_name").ToList();
        if (parentFks.Count > 1)
        {
            List<string> toDrop = parentFks.Skip(1).ToList();
            for (int i = 0; i < toDrop.Count; i += 50)
            {
                string clauses = string.Join(", ", toDrop.Skip(i).Take(50)
                    .Select(fk => $"DROP FOREIGN KEY `{fk}`"));
                c.Execute($"ALTER TABLE quote_material_items {clauses}", commandTimeout: 600);
            }
            log.LogWarning("[Migration v52] Rimosse {Count} foreign key duplicate su quote_material_items.parent_item_id", toDrop.Count);
        }

        log.LogInformation("[Migration v52] Riallineamento schema completato ({Added} colonne aggiunte)", added);
    }
}
