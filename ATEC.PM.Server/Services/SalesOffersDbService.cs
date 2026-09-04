using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Schema e anagrafiche del <b>Registro Offerte</b> (docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md).
///
/// <para><b>Statica come SalDbService</b>: lo stesso codice lo chiamano due percorsi che non si
/// conoscono — il bootstrap (<c>DbService.EnsureModuleTables</c>, per i database nuovi) e la
/// migrazione <c>M122_RegistroOfferte</c> (per quelli esistenti). Una definizione sola letta da
/// entrambi: è la regola nata dalla vista timesheet, che per due versioni è vissuta in due copie
/// divergenti senza che nessuno se ne accorgesse.</para>
///
/// <para><b>InitTables non semina.</b> Gira a ogni avvio: se inserisse righe, ogni riavvio
/// farebbe riapparire i tipi e i venditori che l'amministratore ha tolto. Le semine stanno nei
/// metodi <c>Seed*</c>, che scrivono solo a tabella vuota.</para>
/// </summary>
public static class SalesOffersDbService
{
    /// <summary>
    /// Crea le tabelle del registro se non esistono.
    ///
    /// <para><b>Va chiamata dopo <c>QuoteDbService.InitTables</c></b>: <c>sales_offers</c> ha una
    /// chiave esterna verso <c>quotes</c>, che nasce lì.</para>
    /// </summary>
    public static void InitTables(MySqlConnection c, ILogger? log = null)
    {
        // Tipo impianto: entra nel numero composto (la «S» di S097-2026-EC) e porta la categoria,
        // che è l'unica cosa che rende possibile l'Analisi di mercato per impianti/interventi/ricambi.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sales_offer_types (
            code VARCHAR(10) NOT NULL PRIMARY KEY,
            name VARCHAR(100) NOT NULL DEFAULT '',
            category ENUM('Impianto','Intervento','Ricambio','Altro') NOT NULL DEFAULT 'Altro',
            sort_order INT NOT NULL DEFAULT 0,
            is_active TINYINT(1) NOT NULL DEFAULT 1
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // Venditore: anagrafica a sé e NON una colonna nuova su employees, perché fra le sigle
        // storiche ci sono persone che in ATEC non lavorano più (FF e GS da sole fanno il 79%
        // delle offerte). Il legame con employees è facoltativo e può restare vuoto.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sales_offer_sellers (
            code VARCHAR(10) NOT NULL PRIMARY KEY,
            name VARCHAR(200) NOT NULL DEFAULT '',
            employee_id INT NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            CONSTRAINT fk_sos_employee FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // La riga di registro.
        //
        // NIENTE UNIQUE su (year, number): nei dati storici i numeri 282-285 del 2025 sono
        // duplicati davvero e vanno importati come stanno. L'unicità la fa SalesOfferRules,
        // che può dire di sì all'import e di no a chi crea (come per projects.code).
        //
        // customer_id è FACOLTATIVO e customer_name è il nome scritto sull'offerta: il registro
        // copre anche offerte a clienti che in rubrica non ci sono, e il testo non si riscrive
        // mai quando il nome viene agganciato.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sales_offers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            year INT NOT NULL,
            number INT NULL,
            type_code VARCHAR(10) NOT NULL DEFAULT '',
            seller_code VARCHAR(10) NOT NULL DEFAULT '',
            offer_date DATE NULL,
            customer_id INT NULL,
            customer_name VARCHAR(300) NOT NULL DEFAULT '',
            contact_name VARCHAR(200) NOT NULL DEFAULT '',
            description VARCHAR(1000) NOT NULL DEFAULT '',
            notes TEXT,
            tag VARCHAR(100) NOT NULL DEFAULT '',
            amount DECIMAL(14,2) NULL,
            amount_max DECIMAL(14,2) NULL,
            status ENUM('bozza','aperta','presa','persa','sospesa') NOT NULL DEFAULT 'aperta',
            chance TINYINT NULL,
            order_amount DECIMAL(14,2) NULL,
            is_time_material TINYINT(1) NOT NULL DEFAULT 0,
            advance_pct DECIMAL(5,2) NULL,
            advance_amount DECIMAL(14,2) NULL,
            advance_notes VARCHAR(500) NOT NULL DEFAULT '',
            postponed_year INT NULL,
            offer_path VARCHAR(500) NOT NULL DEFAULT '',
            calc_path VARCHAR(500) NOT NULL DEFAULT '',
            next_contact DATE NULL,
            legacy_id VARCHAR(20) NOT NULL DEFAULT '',
            legacy_number VARCHAR(50) NOT NULL DEFAULT '',
            legacy_ok VARCHAR(50) NOT NULL DEFAULT '',
            is_legacy_series TINYINT(1) NOT NULL DEFAULT 0,
            quote_id INT NULL,
            project_id INT NULL,
            project_code VARCHAR(30) NOT NULL DEFAULT '',
            comm_number VARCHAR(20) NOT NULL DEFAULT '',
            row_version INT NOT NULL DEFAULT 0,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_by INT NULL,
            updated_at DATETIME NULL,
            CONSTRAINT fk_so_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL,
            CONSTRAINT fk_so_quote FOREIGN KEY (quote_id) REFERENCES quotes(id) ON DELETE SET NULL,
            CONSTRAINT fk_so_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL,
            KEY idx_so_year_status (year, status),
            KEY idx_so_customer (customer_id),
            KEY idx_so_seller (seller_code, year),
            KEY idx_so_next_contact (next_contact),
            KEY idx_so_quote (quote_id),
            KEY idx_so_project (project_id),
            KEY idx_so_legacy (legacy_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // Appunti di contatto col referente («richiamato il…»).
        c.Execute(@"CREATE TABLE IF NOT EXISTS sales_offer_followups (
            id INT AUTO_INCREMENT PRIMARY KEY,
            offer_id INT NOT NULL,
            contact_date DATE NULL,
            notes TEXT,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_sof_offer FOREIGN KEY (offer_id) REFERENCES sales_offers(id) ON DELETE CASCADE,
            KEY idx_sof_offer (offer_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // Registro modifiche campo per campo: è la sorgente da cui l'Andamento ricostruisce
        // il portafoglio nel tempo. Nessun tetto di righe (nell'app HTML si tagliava a 50.000
        // perché il documento intero stava in memoria; qui è una tabella).
        c.Execute(@"CREATE TABLE IF NOT EXISTS sales_offer_log (
            id INT AUTO_INCREMENT PRIMARY KEY,
            offer_id INT NOT NULL,
            field VARCHAR(50) NOT NULL,
            old_value VARCHAR(500) NULL,
            new_value VARCHAR(500) NULL,
            changed_by INT NULL,
            changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_sol_offer FOREIGN KEY (offer_id) REFERENCES sales_offers(id) ON DELETE CASCADE,
            KEY idx_sol_offer (offer_id),
            KEY idx_sol_changed (changed_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        log?.LogInformation("[InitTables] Tabelle Registro Offerte (sales_offers, sales_offer_types, sales_offer_sellers, sales_offer_followups, sales_offer_log) verificate/create.");
    }

    /// <summary>
    /// I 13 tipi impianto reali di ATEC, con la categoria che serve all'Analisi di mercato.
    /// Solo a tabella vuota.
    /// </summary>
    public static void SeedTypes(MySqlConnection c, ILogger? log = null)
    {
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offer_types") > 0) return;

        (string Code, string Name, string Cat)[] tipi =
        {
            ("S",   "Sistema",                        "Impianto"),
            ("V",   "Verniciatura",                   "Impianto"),
            ("F",   "Forgiatura",                     "Impianto"),
            ("P",   "Pallettizzazione",               "Impianto"),
            ("A",   "Automazione",                    "Impianto"),
            ("AMU", "Asservimento macchina utensile", "Impianto"),
            ("I",   "Intervento",                     "Intervento"),
            ("M",   "Manutenzione",                   "Intervento"),
            ("RIP", "Riparazione",                    "Intervento"),
            ("T",   "Training",                       "Intervento"),
            ("REV", "Revisione",                      "Intervento"),
            ("SW",  "Software",                       "Intervento"),
            ("R",   "Ricambio",                       "Ricambio"),
        };

        int ordine = 1;
        foreach ((string code, string name, string cat) in tipi)
        {
            c.Execute(@"INSERT INTO sales_offer_types (code, name, category, sort_order, is_active)
                        VALUES (@Code, @Name, @Cat, @Sort, 1)",
                new { Code = code, Name = name, Cat = cat, Sort = ordine++ });
        }
        log?.LogInformation("[SeedTypes] {N} tipi impianto del registro offerte inseriti.", tipi.Length);
    }

    /// <summary>
    /// Le 10 sigle venditore dello storico. Solo a tabella vuota.
    ///
    /// <para>Attive: EC, GM, GV, RC, LS. Spente: FF, GS, MS, DS e AM — non lavorano più in ATEC,
    /// ma sono 1.493 offerte su 1.795 e nei grafici storici devono restare. Tutte e dieci le
    /// sigle hanno un nome: la mappatura l'ha confermata Diego il 04/09/2026.</para>
    ///
    /// <para>Il legame con <c>employees</c> si risolve <b>per nome e cognome</b> e non per sigla:
    /// le iniziali non bastano — in azienda ci sono due GV (Gianpiero Vinardi e Gabriele Vottero)
    /// e tre MC, e il registro parla del secondo.</para>
    /// </summary>
    public static void SeedSellers(MySqlConnection c, ILogger? log = null)
    {
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offer_sellers") > 0) return;

        (string Code, string Name, bool Active)[] venditori =
        {
            ("EC", "Edoardo Carretta",  true),
            ("GM", "Giorgio Maracich",  true),
            ("GV", "Gabriele Vottero",  true),
            ("RC", "Rocco Chiantia",    true),
            ("LS", "Luca Spinello",     true),
            ("FF", "Francesco Ferrari", false),
            ("GS", "Giuseppe Salierno", false),
            ("MS", "Maurizio Spandre",  false),
            ("DS", "Daniele Seidita",   false),
            ("AM", "Alfredo Montera",   false),
        };

        foreach ((string code, string name, bool active) in venditori)
        {
            c.Execute(@"INSERT INTO sales_offer_sellers (code, name, is_active)
                        VALUES (@Code, @Name, @Active)",
                new { Code = code, Name = name, Active = active ? 1 : 0 });
        }

        int legati = c.Execute(@"
            UPDATE sales_offer_sellers s
            JOIN employees e ON CONCAT(e.first_name, ' ', e.last_name) = s.name
            SET s.employee_id = e.id
            WHERE s.employee_id IS NULL AND s.name <> ''");

        log?.LogInformation("[SeedSellers] {N} venditori del registro offerte inseriti, {L} agganciati a un dipendente.",
            venditori.Length, legati);
    }
}
