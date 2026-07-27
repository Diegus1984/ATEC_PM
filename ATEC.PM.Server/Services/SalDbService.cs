using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using ATEC.PM.Server.Data;

namespace ATEC.PM.Server.Services;

// Service per la gestione dei SAL (Stato Avanzamento Lavori) e delle Fatturazioni associate.
// Gestisce tabelle sal_conditions, project_sal e sal_rows.
public class SalDbService
{
    private readonly DbService _db;
    private readonly ILogger<SalDbService>? _logger;

    public SalDbService(DbService db, ILogger<SalDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Crea le tabelle relative al modulo SAL se non esistono.
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // sal_conditions: Anagrafica globale delle condizioni di pagamento.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_conditions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            label VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // project_sal: Header SAL per commessa (cliente + valore + PO ordine cliente + riferimento offerta ATEC).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_sal (
            project_id INT NOT NULL PRIMARY KEY,
            cliente VARCHAR(300) NOT NULL DEFAULT '',
            valore DECIMAL(14,2) NULL,
            po VARCHAR(150) NOT NULL DEFAULT '',
            rif_offerta VARCHAR(200) NOT NULL DEFAULT '',
            row_version INT NOT NULL DEFAULT 0,
            updated_at DATETIME NULL,
            CONSTRAINT fk_psal_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // sal_rows: Step/righe di pagamento per commessa.
        // Campi v10: %IVA, GG saldo fattura, N° fattura (stringa per zeri iniziali), causale Conto SAP,
        // stato pagamento/incasso (testo da anagrafica sal_payment_states), data incasso e note.
        // La data prevista saldo NON è persistita: è derivata (data_fatt + gg_saldo).
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            step VARCHAR(1000) NOT NULL DEFAULT '',
            perc DECIMAL(6,3) NULL,
            condizione VARCHAR(200) NOT NULL DEFAULT '',
            data_fatt DATE NULL,
            stato VARCHAR(10) NOT NULL DEFAULT '',
            iva_perc INT NULL,
            gg_saldo INT NOT NULL DEFAULT 0,
            n_fatt VARCHAR(50) NOT NULL DEFAULT '',
            conto_sap VARCHAR(200) NOT NULL DEFAULT '',
            pagamento VARCHAR(100) NOT NULL DEFAULT '',
            data_pagamento DATE NULL,
            note VARCHAR(2000) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_by INT NULL,
            paid_by INT NULL,
            paid_at DATETIME NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL,
            CONSTRAINT fk_salrow_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            KEY idx_salrow_project (project_id),
            KEY idx_salrow_pag_saldo (pagamento, data_fatt)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // sal_sap_causali: Anagrafica globale delle causali Conto SAP (stessa shape di sal_conditions).
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_sap_causali (
            id INT AUTO_INCREMENT PRIMARY KEY,
            label VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // sal_payment_states: Anagrafica globale degli stati pagamento/incasso (shape di sal_conditions
        // + colori riga configurabili). color_bg/color_fg (#RRGGBB o #RRGGBBAA) sono OPZIONALI:
        // NULL = stato neutro senza tinta. I colori sono pura estetica; la semantica (lock, incasso,
        // warning, bucket Cash Flow) resta cablata sulle etichette 'Pagata'/'Parzialmente Pagata'.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_payment_states (
            id INT AUTO_INCREMENT PRIMARY KEY,
            label VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            color_bg VARCHAR(9) NULL,
            color_fg VARCHAR(9) NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // sal_prospetto_checks: Storico dei controlli periodici del prospetto (chi + quando).
        // Lo stato corrente è l'ultima riga; il banner scatta a 15 giorni dall'ultimo controllo.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_prospetto_checks (
            id INT AUTO_INCREMENT PRIMARY KEY,
            checked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            checked_by INT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        _logger?.LogInformation("[InitTables] Tabelle SAL (sal_conditions, project_sal, sal_rows, sal_sap_causali, sal_payment_states, sal_prospetto_checks) verificate/create.");
    }

    /// <summary>
    /// Esegue il seed delle condizioni di pagamento standard se vuoto.
    /// </summary>
    public void SeedConditions(MySqlConnection c)
    {
        int count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_conditions");
        if (count > 0) return;

        string[] standardConditions = new[] { "A Vista", "30 gg. dffm.", "60 gg. dffm.", "90 gg. dffm." };
        int order = 1;
        foreach (string cond in standardConditions)
        {
            c.Execute("INSERT INTO sal_conditions (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = cond, Sort = order++ });
        }
        _logger?.LogInformation("[SeedConditions] Condizioni di pagamento SAL standard inserite.");
    }

    /// <summary>
    /// Esegue il seed delle causali Conto SAP standard se vuoto.
    /// </summary>
    public void SeedSapCausali(MySqlConnection c)
    {
        int count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_sap_causali");
        if (count > 0) return;

        string[] standardCausali = new[] { "Acconto", "Ricavo" };
        int order = 1;
        foreach (string causale in standardCausali)
        {
            c.Execute("INSERT INTO sal_sap_causali (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = causale, Sort = order++ });
        }
        _logger?.LogInformation("[SeedSapCausali] Causali Conto SAP standard inserite.");
    }

    /// <summary>
    /// Esegue il seed degli stati pagamento standard se vuoto.
    /// </summary>
    public void SeedPaymentStates(MySqlConnection c)
    {
        int count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_payment_states");
        if (count > 0) return;

        // Difensivo: su un DB pre-v23 la tabella può esistere ancora SENZA le colonne colore
        // (in dev questo seed gira PRIMA di ApplyVersionedMigrations) → INSERT senza colori;
        // ci penserà la v23 a seedarli (UPDATE ... WHERE color_bg IS NULL).
        bool hasColorColumns = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'sal_payment_states'
              AND column_name = 'color_bg'") > 0;

        // Voci di sistema con colori di default pastello (Pagata = verde, Parzialmente Pagata = rosso).
        // I colori sono pura estetica e restano modificabili da anagrafica; la semantica è cablata sull'etichetta.
        (string Label, string ColorBg, string ColorFg)[] standardStates = new (string, string, string)[]
        {
            ("Pagata", "#D1FAE5", "#065F46"),
            ("Parzialmente Pagata", "#FEE2E2", "#991B1B")
        };
        int order = 1;
        foreach ((string Label, string ColorBg, string ColorFg) state in standardStates)
        {
            if (hasColorColumns)
            {
                c.Execute(@"INSERT INTO sal_payment_states (label, sort_order, is_active, color_bg, color_fg)
                    VALUES (@Label, @Sort, TRUE, @ColorBg, @ColorFg)",
                    new { state.Label, Sort = order++, state.ColorBg, state.ColorFg });
            }
            else
            {
                c.Execute("INSERT INTO sal_payment_states (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                    new { state.Label, Sort = order++ });
            }
        }
        _logger?.LogInformation("[SeedPaymentStates] Stati pagamento SAL standard inseriti.");
    }
}
