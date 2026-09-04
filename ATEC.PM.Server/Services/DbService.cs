using ATEC.PM.Server.Migrations;
using ATEC.PM.Server.Data;
using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
// Gli attrezzi che questo file condivide con le migrazioni (la vista del timesheet, le sezioni
// delle fasi, AddColumnIfMissing…): stanno in Migrations/AiutiMigrazione.cs perché una classe
// di migrazione non vede i membri privati di DbService, e una seconda copia qui sarebbe la
// trappola che ha lasciato in produzione una vista diversa da quella di sviluppo (v69).
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Services;

public class DbService
{
    private readonly string _cs;
    private readonly ILogger<DbService> _logger;

    /// <summary>
    /// Se una migrazione fallisce, l'avvio si ferma (default). Portare a false in
    /// <c>appsettings.json</c> (<c>Migrations:StopOnError</c>) solo per rimettere in piedi il
    /// server in emergenza: si torna al comportamento vecchio, dove un errore era un warning e
    /// l'applicazione proseguiva con lo schema incompleto.
    /// </summary>
    private readonly bool _stopOnMigrationError;

    /// <summary>
    /// Quanti secondi aspettare il lock delle migrazioni prima di rinunciare
    /// (<c>Migrations:LockTimeoutSeconds</c>). Vedi <see cref="PrendiIlLockDelleMigrazioni"/>.
    /// </summary>
    private readonly int _lockTimeoutSeconds;

    public DbService(IConfiguration config, ILogger<DbService> logger)
    {
        _cs = config.GetConnectionString("Default")!;
        _logger = logger;
        _stopOnMigrationError = config.GetValue("Migrations:StopOnError", true);
        _lockTimeoutSeconds = config.GetValue("Migrations:LockTimeoutSeconds", 30);
    }

    public MySqlConnection Open()
    {
        var conn = new MySqlConnection(_cs);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Esegue UPDATE dinamico su un singolo campo con whitelist di sicurezza.
    /// Restituisce null se ok, oppure il messaggio di errore se il campo non è consentito.
    /// </summary>
    public string? UpdateField(string table, int id, string field, string? value,
        HashSet<string> allowedFields, string? extraWhere = null, object? extraParams = null)
    {
        if (!allowedFields.Contains(field))
            return $"Campo '{field}' non consentito";

        using var c = Open();
        string where = extraWhere != null ? $"id=@id AND {extraWhere}" : "id=@id";
        string sql = $"UPDATE `{table}` SET `{field}`=@Value WHERE {where}";

        DynamicParameters dp = new();
        dp.Add("Value", value);
        dp.Add("id", id);
        if (extraParams != null)
        {
            foreach (var prop in extraParams.GetType().GetProperties())
                dp.Add(prop.Name, prop.GetValue(extraParams));
        }

        c.Execute(sql, dp);
        return null;
    }

    /// <summary>
    /// Il database risponde? Una <c>SELECT 1</c>, niente di più: è la sonda dietro
    /// <c>/api/health/ready</c>, quella su cui gli script di aggiornamento decidono se il server
    /// è tornato su davvero o se serve il rollback.
    /// <para>Sta qui, e non dentro l'endpoint, per poterla provare nei test in tutti e due i casi
    /// — database raggiungibile e non — senza dover spegnere il MySQL di chi sviluppa.</para>
    /// </summary>
    /// <returns>null se tutto a posto, altrimenti il messaggio dell'errore.</returns>
    public async Task<string?> ProvaDatabaseAsync()
    {
        try
        {
            await using MySqlConnection c = new(_cs);
            await c.OpenAsync();
            await c.ExecuteScalarAsync<int>("SELECT 1");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public string GetConfig(string key, string defaultValue = "")
    {
        using var c = Open();
        return c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key=@Key", new { Key = key }) ?? defaultValue;
    }

    /// <summary>
    /// Connessione al SERVER MySQL senza selezionare il database: è l'unica che funziona
    /// quando il database non esiste ancora (Open() fallirebbe con l'errore 1049).
    /// </summary>
    private MySqlConnection OpenServer()
    {
        var csb = new MySqlConnectionStringBuilder(_cs) { Database = "" };
        var conn = new MySqlConnection(csb.ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Attende che il server MySQL risponda (backoff, utile in Docker/servizio Windows al boot)
    /// e crea il database se manca. DEVE essere la PRIMA cosa fatta all'avvio, prima di
    /// qualunque Open(): al primo avvio su una macchina nuova il database non esiste e ogni
    /// tentativo di connessione con Database=... fallirebbe.
    /// </summary>
    /// <returns>true se il database è stato creato adesso (primo avvio).</returns>
    public bool EnsureDatabaseExists(int maxRetries = 5)
    {
        string dbName = new MySqlConnectionStringBuilder(_cs).Database;
        if (string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException(
                "ConnectionStrings:Default non specifica il database: impossibile crearlo/verificarlo.");

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using MySqlConnection conn = OpenServer();

                bool exists = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @Db",
                    new { Db = dbName }) > 0;

                if (exists)
                {
                    _logger.LogInformation("[DB] Database '{Db}' presente (tentativo {Attempt}/{Max})",
                        dbName, attempt, maxRetries);
                    return false;
                }

                try
                {
                    conn.Execute($"CREATE DATABASE `{dbName.Replace("`", "``")}` " +
                                 "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
                }
                catch (Exception createEx)
                {
                    // Utente senza permesso CREATE ma con privilegi solo sul proprio schema:
                    // information_schema può non mostrarlo. Se la connessione diretta funziona
                    // il database c'è davvero e si prosegue; altrimenti l'errore è fatale.
                    using (MySqlConnection probe = new(_cs)) { probe.Open(); }
                    _logger.LogWarning("[DB] CREATE DATABASE non riuscita ({Message}) ma il database '{Db}' è raggiungibile: proseguo.",
                        createEx.Message, dbName);
                    return false;
                }

                _logger.LogWarning("[DB] Database '{Db}' non esisteva: creato ora (primo avvio).", dbName);
                return true;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                int waitMs = attempt * 2000;
                _logger.LogWarning("[DB] Server MySQL non raggiungibile (tentativo {Attempt}/{Max}): {Message}. Riprovo tra {Wait}ms...",
                    attempt, maxRetries, ex.Message, waitMs);
                Thread.Sleep(waitMs);
            }
        }
    }

    /// <summary>
    /// Porta il database allo schema di questa build: lo crea se non c'è, applica le migrazioni
    /// mancanti, riallinea le viste.
    /// <para><b>Sotto lock (blocco A2, 15/08/2026).</b> Tutto il lavoro sullo schema sta dentro
    /// un lock esclusivo di MySQL: due processi che migrano lo stesso database insieme farebbero
    /// girare la stessa migrazione due volte, e il DDL di MySQL non è transazionale — non c'è
    /// nessun rollback a rimettere le cose a posto. Non è teorico: il servizio Windows può
    /// riavviarsi mentre una migrazione è in corso, e durante un aggiornamento la vecchia
    /// istanza e la nuova possono sovrapporsi per qualche secondo.</para>
    /// </summary>
    public void InitDatabase(bool productionMode = false)
    {
        _logger.LogInformation("[InitDatabase] Avvio verifica/creazione schema (mode={Mode})...",
            productionMode ? "PRODUCTION" : "DEVELOPMENT");
        EnsureDatabaseExists();

        // UNA connessione sola per tutto il lavoro: il lock di MySQL appartiene alla SESSIONE,
        // quindi prenderlo su una connessione e lavorare su un'altra non proteggerebbe niente.
        using var c = Open();

        // L'acquisizione sta DENTRO il try: se solleva a metà — il driver che uccide la query
        // proprio mentre MySQL concede il lock — il rilascio deve girare lo stesso. Rilasciare un
        // lock che non si ha non fa niente; lasciarne uno appeso a una connessione tornata nel
        // pool bloccherebbe ogni avvio successivo.
        try
        {
            PrendiIlLockDelleMigrazioni(c);
            CostruisciOAggiornaSchema(c, productionMode);
        }
        finally
        {
            RilasciaIlLockDelleMigrazioni(c);
        }
    }

    private void CostruisciOAggiornaSchema(MySqlConnection c, bool productionMode)
    {
        // Database appena creato (o svuotato a mano): non c'è nulla su cui migrare, serve lo
        // schema completo. Vale ANCHE in produzione, dove il percorso normale applica solo le
        // migrazioni versionate: senza questo, il primo avvio lascerebbe il database vuoto.
        bool freshDatabase = !TableExists(c, "employees");

        EnsureSchemaMigrationsTable(c);

        if (productionMode && !freshDatabase)
        {
            _logger.LogInformation("[InitDatabase] Schema versione corrente: {Version}", GetSchemaVersion(c));

            HashSet<int> appliedVersions = GetAppliedVersions(c);
            BackfillLegacyVersions(c, appliedVersions, databasePopolato: true);

            // Tabelle dei moduli PRIMA delle migrazioni: sono CREATE TABLE IF NOT EXISTS puri
            // (nessun seed di dati), quindi sicuri anche in produzione. Senza questa chiamata un
            // modulo nuovo senza migrazione dedicata resterebbe per sempre senza tabelle qui.
            EnsureModuleTables(c);
            ApplyVersionedMigrations(c, appliedVersions, _stopOnMigrationError);
            EnsureViews(c);
            EnsureCatalogo(c);
            LogTableCount(c);
            return;
        }

        if (productionMode)
            _logger.LogWarning("[InitDatabase] Database vuoto in PRODUCTION: eseguo la creazione completa dello schema (bootstrap primo avvio).");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 0 — Tabelle senza dipendenze
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS app_config (
            config_key VARCHAR(100) PRIMARY KEY,
            config_value VARCHAR(500) DEFAULT '',
            description VARCHAR(200) DEFAULT '',
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(100) NOT NULL,
            hourly_cost DECIMAL(8,2) NOT NULL DEFAULT 0,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            sort_order INT DEFAULT 0,
            is_active BOOLEAN DEFAULT TRUE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS employees (
            id INT AUTO_INCREMENT PRIMARY KEY,
            first_name VARCHAR(100) NOT NULL,
            last_name VARCHAR(100) NOT NULL,
            email VARCHAR(200) DEFAULT '',
            emp_type VARCHAR(20) DEFAULT 'INTERNAL',
            supplier_id INT NULL,
            status VARCHAR(20) DEFAULT 'ACTIVE',
            username VARCHAR(50),
            password_hash VARCHAR(255) DEFAULT '',
            user_role VARCHAR(20) DEFAULT 'TECH',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            INDEX idx_emp_status (status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS customers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            company_name VARCHAR(200) NOT NULL,
            contact_name VARCHAR(100) DEFAULT '',
            email VARCHAR(200) DEFAULT '',
            pec VARCHAR(255) DEFAULT '',
            phone VARCHAR(100) DEFAULT '',
            cell VARCHAR(50) DEFAULT '',
            address VARCHAR(300) DEFAULT '',
            vat_number VARCHAR(50) DEFAULT '',
            fiscal_code VARCHAR(50) DEFAULT '',
            payment_terms VARCHAR(255) DEFAULT '',
            sdi_code VARCHAR(50) DEFAULT '',
            easyfatt_code VARCHAR(50) DEFAULT '',
            easyfatt_id INT DEFAULT 0,
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_Customer_Vat (vat_number)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS suppliers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            company_name VARCHAR(200) NOT NULL,
            contact_name VARCHAR(100) DEFAULT '',
            email VARCHAR(200) DEFAULT '',
            phone VARCHAR(100) DEFAULT '',
            address VARCHAR(300) DEFAULT '',
            vat_number VARCHAR(50) DEFAULT '',
            fiscal_code VARCHAR(50) DEFAULT '',
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_Supplier_Vat (vat_number)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS holidays (
            id INT AUTO_INCREMENT PRIMARY KEY,
            holiday_date DATE NOT NULL,
            description VARCHAR(100) DEFAULT '',
            year INT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS tariff_options (
            id INT AUTO_INCREMENT PRIMARY KEY,
            tariff_type VARCHAR(30) NOT NULL,
            -- Nome della tariffa (#87, v95): «Meccanica», «Stampa 3D», «Carpenteria»…
            -- Vuoto = tariffa che si legge dal solo importo (com'erano tutte prima).
            label VARCHAR(100) NOT NULL DEFAULT '',
            value DECIMAL(10,3) NOT NULL,
            UNIQUE KEY UQ_TariffVal (tariff_type, value)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_destinations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Stati DDP (etichetta + colori riga/combo, editabili da Conf. DDP).
        // La chiave (status_key) è il valore salvato in bom_items.item_status: NON va cambiata.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_statuses (
            id INT AUTO_INCREMENT PRIMARY KEY,
            status_key VARCHAR(30) NOT NULL UNIQUE,
            label VARCHAR(100) NOT NULL,
            color_bg VARCHAR(9) NOT NULL DEFAULT '#CCCCCC',
            color_fg VARCHAR(9) NOT NULL DEFAULT '#000000',
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Seed delle causali DDP reali. INSERT IGNORE su status_key: idempotente, NON sovrascrive
        // le modifiche fatte dall'utente. I gruppi condividono il colore.
        // L'ordine è quello del FLUSSO DI LAVORO chiesto nella segnalazione #54 (08/08/2026):
        // offerta → ordine → arrivo → magazzino → eccezioni; i tre stati fuori dalla lista della
        // DDP Officine (RAM, CHEK, VER) stanno in coda. CON e COS sono tornati stati veri: la v39
        // li aveva assorbiti dentro DISP, ma in officina «consegnato» (comprato fuori) e
        // «costruito» (fatto in casa) sono due cose diverse — vedi migrazione v75.
        c.Execute(@"INSERT IGNORE INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order) VALUES
            ('RO',   'RICHIESTA OFFERTA',                               '#FFC000', '#000000', 1),
            ('DO',   'DA ORDINARE',                                     '#FF0000', '#FFFFFF', 2),
            ('DC',   'DA COSTRUIRE',                                    '#006400', '#FFFFFF', 3),
            ('IO',   'IN ORDINE',                                       '#FFFF00', '#000000', 4),
            ('PAR',  'PARZIALMENTE CONSEGNATO o COSTRUITO',             '#7030A0', '#FFFFFF', 5),
            ('CON',  'CONSEGNATO',                                      '#00B050', '#FFFFFF', 6),
            ('COS',  'COSTRUITO',                                       '#2E7D32', '#FFFFFF', 7),
            ('DISP', 'DISPONIBILE / CONSEGNATO',                        '#00B050', '#FFFFFF', 8),
            ('MIT',  'MATERIALE IN TRATTAMENTO',                        '#7FC1B0', '#000000', 9),
            ('ANN',  'ANNULLATO',                                       '#000000', '#FFFFFF', 10),
            ('SOSP', 'SOSPESO',                                         '#000000', '#FFFFFF', 11),
            ('SOST', 'SOSTITUITO',                                      '#000000', '#FFFFFF', 12),
            ('ASS',  'ASSEGNATO AL MONTATORE',                          '#B4B4B4', '#000000', 13),
            ('RAM',  'RIMESSO A MAGAZZINO',                             '#000000', '#FFFFFF', 14),
            ('CHEK', 'MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE','#8B008B', '#FFFFFF', 15),
            ('VER',  'VERIFICARE SE DISPONIBILE A MAG',                 '#00B0F0', '#FFFFFF', 16)");

        // Matrice degli avanzamenti di stato (riga = stato corrente, colonna = stato selezionabile).
        // Un record (from,to) = transizione ammessa; il record sentinella (from,'') marca uno stato
        // "governato" senza uscite (terminale: ANN, SOST). Stati SENZA alcun record = non governati
        // dalla matrice → finestra opzioni completa (retro-compatibilità con stati custom di Conf. DDP).
        // from_key speciale 'INIZIO' = finestra di partenza delle righe senza stato.
        // Seed nella migrazione v39, sdoppiata per tipo dalla v40 (ddp_type COMMERCIAL/OFFICINA:
        // la commerciale non contempla DC — il materiale commerciale si acquista). One-shot:
        // l'editor in Conf. DDP può togliere archi senza vederseli riapparire a ogni avvio.
        // Creata qui nella forma v39 (senza tipo): sui DB nuovi la v40 la porta subito al PK triplo.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_status_transitions (
            from_key VARCHAR(30) NOT NULL,
            to_key VARCHAR(30) NOT NULL DEFAULT '',
            PRIMARY KEY (from_key, to_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Trattamenti DDP Officina (anagrafica editabile da Conf. DDP; le righe distinta
        // salvano il trattamento per NOME). Stessa definizione della migrazione v30.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_treatments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL UNIQUE,
            sort_order INT NOT NULL DEFAULT 0,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active) VALUES
            ('ANODIZZATO', 10, 1),
            ('BRUNITO', 20, 1),
            ('ZINCATO', 30, 1),
            ('VERNICIATO', 40, 1),
            ('SABBIATO', 50, 1)");

        // NB: il backfill dei trattamenti già digitati sulle righe officina sta più in basso,
        // dopo la CREATE di ddp_officina_items (qui la tabella non esiste ancora su un DB nuovo).

        // Aggregazioni di stato DDP (matrice Stati × Aggregazioni, editabile da "Aggregazioni DDP").
        // kind: SET=unione stati · ALL=tutti (conteggio per stato) · DATED=stati+data prev. · SUBGROUPS=7 card.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_aggregations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(150) NOT NULL,
            description VARCHAR(500) DEFAULT '',
            kind VARCHAR(20) NOT NULL DEFAULT 'SET',
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_aggregation_states (
            aggregation_id INT NOT NULL,
            status_key VARCHAR(30) NOT NULL,
            PRIMARY KEY (aggregation_id, status_key),
            FOREIGN KEY (aggregation_id) REFERENCES ddp_aggregations(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS material_categories (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            default_commission_markup DECIMAL(5,3) NOT NULL DEFAULT 1.100,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_groups (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            bg_color VARCHAR(10) NOT NULL DEFAULT '#3B82F6',
            text_color VARCHAR(10) NOT NULL DEFAULT '#FFFFFF',
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 1 — Dipendono da livello 0
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            department_id INT NOT NULL,
            is_responsible BOOLEAN DEFAULT FALSE,
            is_primary BOOLEAN DEFAULT FALSE,
            UNIQUE KEY UQ_EmpDept (employee_id, department_id),
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_competences (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            department_id INT NOT NULL,
            notes VARCHAR(255) DEFAULT '',
            UNIQUE KEY UQ_EmpComp (employee_id, department_id),
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // `absences` NON si crea più qui: la sostituisce `hr_absences` (M112), che la droppa
        // se vuota. Il bootstrap gira PRIMA delle migrazioni (vedi sotto): lasciare la CREATE
        // qui la faceva rinascere vuota a ogni avvio e il DROP di M112 — che passa una volta
        // sola — non teneva.

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_templates (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            section_type VARCHAR(20) NOT NULL DEFAULT 'IN_SEDE',
            group_id INT NOT NULL,
            is_default_project BOOLEAN NOT NULL DEFAULT TRUE,
            is_default_quote BOOLEAN NOT NULL DEFAULT TRUE,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (group_id) REFERENCES cost_section_groups(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_template_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_template_id INT NOT NULL,
            department_id INT NOT NULL,
            FOREIGN KEY (section_template_id) REFERENCES cost_section_templates(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS catalog_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(100) NOT NULL,
            description VARCHAR(2000) DEFAULT '',
            category VARCHAR(255) DEFAULT '',
            subcategory VARCHAR(255) DEFAULT '',
            unit VARCHAR(50) DEFAULT 'PZ',
            unit_cost DECIMAL(10,4) DEFAULT 0,
            list_price DECIMAL(10,4) DEFAULT 0,
            supplier_id INT NULL,
            supplier_code VARCHAR(100) DEFAULT '',
            manufacturer VARCHAR(255) DEFAULT '',
            barcode VARCHAR(50) DEFAULT '',
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            easyfatt_id INT NULL,
            atec_code VARCHAR(15) NULL,
            codex_item_id INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_CatalogItem_Code (code),
            INDEX IX_CatalogItems_Description (description(255)),
            INDEX IX_CatalogItems_AtecCode (atec_code),
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS notifications (
            id INT AUTO_INCREMENT PRIMARY KEY,
            notification_type VARCHAR(30) NOT NULL,
            severity VARCHAR(10) NOT NULL DEFAULT 'INFO',
            title VARCHAR(200) NOT NULL,
            message VARCHAR(500) NOT NULL DEFAULT '',
            reference_type VARCHAR(20) NOT NULL DEFAULT '',
            reference_id INT NOT NULL DEFAULT 0,
            -- Giornata a cui l'avviso si riferisce, quando il riferimento da solo non basta a
            -- dire di cosa si parla: le anomalie ore sono (persona, giorno lavorato), e senza
            -- questa colonna la stessa giornata rinasceva a ogni giro mentre due giornate
            -- diverse della stessa persona si cancellavano a vicenda (BUG-014, v93).
            -- Resta NULL per tutti gli altri avvisi, che deduplicano per riferimento e basta.
            reference_date DATE NULL,
            project_id INT NULL,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            -- Il dedup («l'ho già segnalata oggi?») cerca per tipo + riferimento + giorno: il
            -- solo notification_type non selezionava niente, perché dentro un controllo è
            -- uguale per tutte le righe. Allineato alla v91 (E2): un database nuovo nasce già
            -- con l'assetto giusto invece di farsi correggere dalla migrazione un attimo dopo.
            INDEX ix_notif_dedup (notification_type, reference_type, reference_id, created_at),
            INDEX idx_created (created_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS notification_recipients (
            id INT AUTO_INCREMENT PRIMARY KEY,
            notification_id INT NOT NULL,
            employee_id INT NOT NULL,
            is_read BOOLEAN NOT NULL DEFAULT FALSE,
            read_at DATETIME NULL,
            FOREIGN KEY (notification_id) REFERENCES notifications(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            INDEX idx_emp_unread (employee_id, is_read),
            INDEX idx_notif (notification_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 2 — Dipendono da livello 1
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_templates (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT '',
            department_id INT NULL,
            cost_section_template_id INT NULL,
            sort_order INT DEFAULT 0,
            is_default BOOLEAN DEFAULT TRUE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
            FOREIGN KEY (cost_section_template_id) REFERENCES cost_section_templates(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Legami fase ↔ sezione di costo (v73). Una fase dell'anagrafica sta su PIÙ sezioni:
        // «Call Cliente» sotto Program Manager e sotto Progettazione insieme, e in commessa
        // nasce una volta per sezione. Prima serviva creare tre fasi con lo stesso nome — da lì
        // vengono i doppioni («Programmazione PLC» / «Programmazione Plc»).
        // `sort_order` e `is_default` stanno QUI e non su phase_templates: l'ordine dentro PM
        // non è quello dentro Progettazione, e la fase può nascere da sola in una sezione sola.
        // CASCADE su entrambi i lati: cancellare una fase o una sezione porta via i legami.
        // Vedi PIANO-FASI-MULTISEZIONE.md.
        c.Execute(PhaseTemplateSectionsDdl);

        // ══════════════════════════════════════════════════════════
        // LIVELLO 3 — projects
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS projects (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(20) NOT NULL,
            title VARCHAR(300) NOT NULL,
            customer_id INT NOT NULL,
            pm_id INT NOT NULL,
            description TEXT,
            start_date DATE,
            end_date_planned DATE,
            end_date_actual DATE NULL,
            budget_total DECIMAL(12,2) DEFAULT 0,
            budget_hours_total DECIMAL(8,1) DEFAULT 0,
            revenue DECIMAL(12,2) DEFAULT 0,
            sale_total DECIMAL(14,2) NULL,
            actual_travel_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(20) DEFAULT 'DRAFT',
            in_dashboard TINYINT(1) NOT NULL DEFAULT 1,
            priority VARCHAR(20) DEFAULT 'MEDIUM',
            server_path VARCHAR(500) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (customer_id) REFERENCES customers(id),
            FOREIGN KEY (pm_id) REFERENCES employees(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 4 — Dipendono da projects
        // ══════════════════════════════════════════════════════════
        // Tabelle offer_* rimosse (legacy — sostituite da quote_*)

        // Righe dell'ordine cliente (Bilancio commessa): un ordine può essere spezzato in
        // N posizioni. `projects.revenue` resta la fonte per SAL/dashboard/cash flow ed è
        // tenuta allineata dal server a COALESCE(SUM(amount),0) ad ogni scrittura.
        // Stessa definizione della migrazione v61.
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_order_lines (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            order_ref VARCHAR(100) NOT NULL DEFAULT '',
            order_position VARCHAR(10) NOT NULL DEFAULT '',
            amount DECIMAL(14,2) NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_pol_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_pol_project (project_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Fogli di calcolo a righe («finestra di calcolo» del Riepilogo Costi, blocco 5).
        // Un foglio per (commessa, calc_key): il dettaglio del calcolo, che prima si perdeva
        // perché a video restava solo il totale digitato. row_version = token di concorrenza
        // dell'INTERO foglio: la conferma riscrive tutte le righe in una volta sola.
        // Stessa definizione della migrazione v62.
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_calc_sheets (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            calc_key VARCHAR(40) NOT NULL,
            row_version INT NOT NULL DEFAULT 0,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            updated_by INT NULL,
            UNIQUE KEY uk_calc_sheet (project_id, calc_key),
            CONSTRAINT fk_pcs_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // `multiplier` = terzo fattore facoltativo della riga (calcolatrice Auto del blocco 6:
        // Km × Rimborso × Numero Tratte). NULL vale 1: le righe che non lo usano non cambiano.
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_calc_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            sheet_id INT NOT NULL,
            group_key VARCHAR(20) NOT NULL DEFAULT '',
            description VARCHAR(500) NOT NULL DEFAULT '',
            quantity DECIMAL(12,3) NULL,
            unit_cost DECIMAL(14,4) NULL,
            multiplier DECIMAL(12,3) NULL,
            amount DECIMAL(14,2) NULL,
            amount_pinned TINYINT(1) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            linked_source VARCHAR(60) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            CONSTRAINT fk_pcr_sheet FOREIGN KEY (sheet_id) REFERENCES project_calc_sheets(id) ON DELETE CASCADE,
            INDEX idx_pcr_sheet (sheet_id, group_key, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ── TRASFERTA (blocco 6) ──────────────────────────────────
        // Una commessa ha N step; ogni step ha N righe-persona. Il DETTAGLIO delle quattro
        // calcolatrici (ore, vitto, indennità, auto) NON sta qui: vive nei fogli del blocco 5,
        // con la chiave che porta l'id della riga (`trasferta.ore:{rowId}`, …).
        c.Execute(@"CREATE TABLE IF NOT EXISTS travel_steps (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            -- Lo step nato dal Timesheet È una fase di commessa (#37/#52, v79); NULL = step
            -- aperto a mano, come si faceva prima.
            project_phase_id INT NULL,
            description VARCHAR(300) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_ts_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_ts_project (project_id, sort_order),
            INDEX idx_ts_phase (project_phase_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // employee_id + person_name: il nominativo si sceglie dall'anagrafica ma resta scritto
        // sulla riga, così una trasferta di due anni fa si legge anche se il dipendente non c'è più.
        c.Execute(@"CREATE TABLE IF NOT EXISTS travel_step_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            step_id INT NOT NULL,
            employee_id INT NULL,
            person_name VARCHAR(200) NOT NULL DEFAULT '',
            -- Righe derivate dal Timesheet (#37/#52, v79): il giorno al posto di inizio/fine,
            -- la provenienza, i giorni imputati (il valore della giornata sulla prima riga:
            -- 0,5 fino a 4 ore scaricate, 1 oltre — #98, v98) e il segnale «le ore dietro
            -- non ci sono più». Vedi migrazioni v79 e v98 per il perché.
            work_date DATE NULL,
            source VARCHAR(20) NOT NULL DEFAULT 'MANUAL',
            travel_days DECIMAL(3,1) NULL,
            hours_missing TINYINT(1) NOT NULL DEFAULT 0,
            start_date DATE NULL,
            end_date DATE NULL,
            exclude_sat TINYINT(1) NOT NULL DEFAULT 0,
            exclude_sun TINYINT(1) NOT NULL DEFAULT 0,
            hours DECIMAL(10,2) NULL,
            hourly_rate DECIMAL(10,3) NULL,
            nights INT NULL,
            night_price DECIMAL(12,2) NULL,
            meal_cost DECIMAL(12,2) NULL,
            allowance_cost DECIMAL(12,2) NULL,
            car_cost DECIMAL(12,2) NULL,
            transport_cost DECIMAL(12,2) NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_tsr_step FOREIGN KEY (step_id) REFERENCES travel_steps(id) ON DELETE CASCADE,
            INDEX idx_tsr_step (step_id, sort_order),
            -- Rende la rigenerazione ripetibile senza doppioni. Le righe manuali hanno
            -- work_date NULL e in MySQL i NULL non collidono: restano fuori dal vincolo.
            UNIQUE KEY uq_tsr_derivata (step_id, employee_id, work_date)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // phase_template_id è NULL-able: le fasi LOCALI (is_local=1, create da PhasesController)
        // non nascono da un template. name/category/cost_section_template_id sono lo snapshot
        // denormalizzato del template (le GET fanno COALESCE tra riga e template).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_phases (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            phase_template_id INT NULL,
            name VARCHAR(100) NULL,
            category VARCHAR(50) NULL,
            cost_section_template_id INT NULL,
            is_local TINYINT(1) NOT NULL DEFAULT 0,
            -- Fase «spenta» (segnalazione #51): sparisce dall'elenco del Bilancio e non
            -- accetta ore nuove dal Timesheet. NON esclude nulla dai conti: le ore già
            -- imputate continuano a contare, ed è una scelta esplicita — vedi v78.
            is_off TINYINT(1) NOT NULL DEFAULT 0,
            department_id INT NULL,
            custom_name VARCHAR(200) DEFAULT '',
            budget_hours DECIMAL(8,1) DEFAULT 0,
            budget_cost DECIMAL(12,2) DEFAULT 0,
            status VARCHAR(20) DEFAULT 'NOT_STARTED',
            progress_pct INT DEFAULT 0,
            sort_order INT DEFAULT 0,
            notes TEXT,
            start_date DATE NULL,
            end_date DATE NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (phase_template_id) REFERENCES phase_templates(id),
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
            INDEX idx_pp_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL UNIQUE,
            payment_amount DECIMAL(12,2) DEFAULT 0,
            month_count INT DEFAULT 13,
            start_date DATE NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow_categories (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            total_amount DECIMAL(12,2) DEFAULT 0,
            notes VARCHAR(500) DEFAULT '',
            sort_order INT DEFAULT 0,
            linked_source VARCHAR(100) NULL,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_pcc_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow_data (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            data_type VARCHAR(20) NOT NULL,
            ref_id INT DEFAULT 0,
            month_number INT NOT NULL,
            num_value DECIMAL(12,2) DEFAULT 0,
            date_value DATE NULL,
            UNIQUE KEY UQ_CfData (project_id, data_type, ref_id, month_number),
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chats (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            title VARCHAR(200) NOT NULL,
            created_by INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (created_by) REFERENCES employees(id),
            INDEX idx_pch_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chat_participants (
            id INT AUTO_INCREMENT PRIMARY KEY,
            chat_id INT NOT NULL,
            employee_id INT NOT NULL,
            last_read_message_id INT DEFAULT 0,
            added_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_ChatPart (chat_id, employee_id),
            FOREIGN KEY (chat_id) REFERENCES project_chats(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chat_messages (
            id INT AUTO_INCREMENT PRIMARY KEY,
            chat_id INT NOT NULL,
            employee_id INT NOT NULL,
            message TEXT NOT NULL,
            has_attachment BOOLEAN DEFAULT FALSE,
            attachment_name VARCHAR(300) DEFAULT '',
            attachment_path VARCHAR(500) DEFAULT '',
            reply_to_message_id INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (chat_id) REFERENCES project_chats(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            INDEX idx_pcm_chat (chat_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS bom_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            catalog_item_id INT NULL,
            part_number VARCHAR(100) DEFAULT '',
            description VARCHAR(300) DEFAULT '',
            unit VARCHAR(50) DEFAULT 'PZ',
            quantity DECIMAL(10,3) DEFAULT 0,
            unit_cost DECIMAL(10,2) DEFAULT 0,
            supplier_id INT NULL,
            manufacturer VARCHAR(200) DEFAULT '',
            item_status VARCHAR(20) DEFAULT 'VER',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            danea_order_iddoc INT NULL,
            purchase_order VARCHAR(100) DEFAULT '',
            date_needed DATE,
            delivered_at DATE NULL,
            date_ordered DATE,
            date_received DATE,
            destination VARCHAR(200) DEFAULT '',
            destination_spec VARCHAR(200) DEFAULT '',
            ddp_type VARCHAR(20) DEFAULT 'COMMERCIAL',
            atec_code VARCHAR(15) NULL,
            notes TEXT,
            created_by INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL,
            FOREIGN KEY (catalog_item_id) REFERENCES catalog_items(id) ON DELETE SET NULL,
            FOREIGN KEY (created_by) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_bom_project (project_id),
            INDEX IX_BomItems_AtecCode (atec_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Distinta DDP Officina (particolari meccanici, template "Mod. DDP - OFFICINA"): tabella dedicata,
        // schema allineato al Codex (codici 101). NESSUNA FK verso codex_items per scelta: codice/
        // descrizione/fornitore/costo sono uno snapshot denormalizzato al momento della scelta dal picker
        // (la riga DDP non deve cambiare se il Codex viene aggiornato). Stati e destinazioni CONDIVISI
        // con la DDP commerciale (ddp_statuses / ddp_destinations). updated_at = token di concorrenza
        // ottimistica (pattern v5).
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_officina_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            part_number VARCHAR(100) DEFAULT '',
            description VARCHAR(300) DEFAULT '',
            quantity DECIMAL(10,3) DEFAULT 0,
            quantity_produced DECIMAL(10,3) NOT NULL DEFAULT 0,
            -- Ore di lavorazione (officine interne): × tariffa oraria = costo unitario.
            -- NULL = non imputate, che è diverso da zero ore. Vedi migrazione v77.
            work_hours DECIMAL(10,2) NULL,
            -- Tariffa oraria con cui è stato fatto quel conto (#87, v95): le tariffe in
            -- anagrafica sono più d'una (meccanica, carpenteria, stampa 3D) e senza questa
            -- colonna non si saprebbe quale è stata scelta. NULL = costo scritto a mano.
            hourly_rate DECIMAL(10,2) NULL,
            material VARCHAR(200) DEFAULT '',
            treatment VARCHAR(200) DEFAULT '',
            supplier_name VARCHAR(200) DEFAULT '',
            unit_cost DECIMAL(10,2) DEFAULT 0,
            item_status VARCHAR(20) DEFAULT 'DO',
            work_type VARCHAR(20) NOT NULL DEFAULT '',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            date_needed DATE,
            order_date DATE NULL,
            -- «Consegnato il» editabile (#82); prima solo dalla cronistoria.
            delivered_at DATE NULL,
            destination VARCHAR(200) DEFAULT '',
            destination_spec VARCHAR(200) DEFAULT '',
            notes TEXT,
            -- Note di gestione della lavorazione (#83): sono di chi produce, non dell'ufficio
            -- tecnico che compila `notes` nella distinta. Due campi apposta, vedi v92.
            workshop_notes TEXT NULL,
            is_ultra_critical TINYINT(1) NOT NULL DEFAULT 0,
            parent_officina_item_id INT NULL,
            composition_qty DECIMAL(10,3) NULL,
            created_by INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (parent_officina_item_id) REFERENCES ddp_officina_items(id) ON DELETE SET NULL,
            FOREIGN KEY (created_by) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_ddpoff_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Backfill: i trattamenti già digitati a mano sulle righe officina entrano in
        // anagrafica ddp_treatments (UNIQUE(name) case-insensitive dedupa via INSERT IGNORE).
        // Deve stare DOPO la CREATE qui sopra: su un database nuovo la tabella non esiste ancora.
        c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active)
            SELECT DISTINCT TRIM(treatment), 100, 1
            FROM ddp_officina_items
            WHERE TRIM(COALESCE(treatment, '')) <> ''");

        c.Execute(@"CREATE TABLE IF NOT EXISTS documents (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            title VARCHAR(300) DEFAULT '',
            file_path VARCHAR(500) DEFAULT '',
            file_url VARCHAR(500) DEFAULT '',
            file_type VARCHAR(50) DEFAULT '',
            category VARCHAR(20) DEFAULT 'OTHER',
            uploaded_by VARCHAR(100) DEFAULT '',
            file_size BIGINT DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_doc_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS extra_costs (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            employee_id INT NULL,
            cost_date DATE,
            category VARCHAR(20) DEFAULT 'OTHER',
            description VARCHAR(300) DEFAULT '',
            amount DECIMAL(10,2) DEFAULT 0,
            receipt_ref VARCHAR(100) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_ec_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_sections (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            template_id INT NULL,
            name VARCHAR(200) NOT NULL,
            section_type VARCHAR(20) NOT NULL DEFAULT 'IN_SEDE',
            group_name VARCHAR(100) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            contingency_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            margin_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            contingency_pinned TINYINT(1) NOT NULL DEFAULT 0,
            margin_pinned TINYINT(1) NOT NULL DEFAULT 0,
            is_shadowed TINYINT(1) NOT NULL DEFAULT 0,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (template_id) REFERENCES cost_section_templates(id) ON DELETE SET NULL,
            INDEX idx_pcs_project (project_id, is_enabled),
            INDEX idx_pcs_template (template_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_section_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_cost_section_id INT NOT NULL,
            department_id INT NOT NULL,
            FOREIGN KEY (project_cost_section_id) REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_resources (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            employee_id INT NULL,
            resource_name VARCHAR(200) NOT NULL DEFAULT '',
            work_days DECIMAL(8,1) NOT NULL DEFAULT 0,
            hours_per_day DECIMAL(4,1) NOT NULL DEFAULT 8,
            hourly_cost DECIMAL(8,2) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            num_trips INT NOT NULL DEFAULT 0,
            km_per_trip DECIMAL(8,1) NOT NULL DEFAULT 0,
            cost_per_km DECIMAL(6,3) NOT NULL DEFAULT 0,
            daily_food DECIMAL(8,2) NOT NULL DEFAULT 0,
            daily_hotel DECIMAL(8,2) NOT NULL DEFAULT 0,
            allowance_days INT NOT NULL DEFAULT 0,
            daily_allowance DECIMAL(8,2) NOT NULL DEFAULT 0,
            sort_order INT NOT NULL DEFAULT 0,
            FOREIGN KEY (section_id) REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_pcr_section (section_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Trasferta a righe della sezione di preventivo (segnalazione #33, migrazione v68).
        // Prende il posto dei 7 campi trasferta di project_cost_resources, che però NON si
        // cancellano: li usa ancora il Commerciale e li interroga la guardia anti-cancellazione
        // delle tariffe. Tipi identici a travel_step_rows, di proposito: stesse formule, stessi
        // componenti. Il dettaglio delle 3 calcolatrici sta nei fogli del blocco 5, con la
        // chiave che porta l'id della riga (`preventivo.vitto:{rowId}`, …).
        // resource_id è FACOLTATIVO: la riga può essere una voce scritta a mano.
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_travel_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            resource_id INT NULL,
            person_name VARCHAR(200) NOT NULL DEFAULT '',
            nights INT NULL,
            night_price DECIMAL(12,2) NULL,
            meal_cost DECIMAL(12,2) NULL,
            allowance_cost DECIMAL(12,2) NULL,
            car_cost DECIMAL(12,2) NULL,
            transport_cost DECIMAL(12,2) NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_pctr_section  FOREIGN KEY (section_id)  REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            CONSTRAINT fk_pctr_resource FOREIGN KEY (resource_id) REFERENCES project_cost_resources(id) ON DELETE SET NULL,
            INDEX idx_pctr_section (section_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_material_sections (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            category_id INT NULL,
            name VARCHAR(200) NOT NULL,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            commission_markup DECIMAL(5,3) NOT NULL DEFAULT 1.100,
            sort_order INT NOT NULL DEFAULT 0,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (category_id) REFERENCES material_categories(id) ON DELETE SET NULL,
            INDEX idx_pms_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_material_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            parent_item_id INT NULL,
            description VARCHAR(500) NOT NULL DEFAULT '',
            quantity DECIMAL(10,3) NOT NULL DEFAULT 0,
            unit_cost DECIMAL(10,4) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            item_type VARCHAR(20) NOT NULL DEFAULT 'MATERIAL',
            sort_order INT NOT NULL DEFAULT 0,
            contingency_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            margin_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            contingency_pinned TINYINT(1) NOT NULL DEFAULT 0,
            margin_pinned TINYINT(1) NOT NULL DEFAULT 0,
            is_shadowed TINYINT(1) NOT NULL DEFAULT 0,
            FOREIGN KEY (section_id) REFERENCES project_material_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (parent_item_id) REFERENCES project_material_items(id) ON DELETE CASCADE,
            INDEX idx_pmi_section (section_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_pricing (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL UNIQUE,
            contingency_pct DECIMAL(7,4) NOT NULL DEFAULT 0.1300,
            negotiation_margin_pct DECIMAL(7,4) NOT NULL DEFAULT 0.0500,
            -- travel_markup: colonna DORMIENTE dal 06/08/2026. Il K di ricarico delle
            -- trasferte è stato rimosso dai calcoli (#34) ma la colonna resta a 1,000 così
            -- riaccenderlo è una riga di codice e i DTO del Commerciale non si rompono.
            travel_markup DECIMAL(5,3) NOT NULL DEFAULT 1.000,
            allowance_markup DECIMAL(5,3) NOT NULL DEFAULT 1.000,
            -- Prezzo offerta finale imputato a mano (#35). NULL = si mostra il calcolato.
            final_price_override DECIMAL(14,2) NULL,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 5 — Dipendono da project_phases
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_assignments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_phase_id INT NOT NULL,
            employee_id INT NOT NULL,
            assign_role VARCHAR(20) DEFAULT 'MEMBER',
            planned_hours DECIMAL(8,1) DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            INDEX idx_pa_phase (project_phase_id),
            INDEX idx_pa_employee (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS timesheet_entries (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            project_phase_id INT NOT NULL,
            work_date DATE NOT NULL,
            hours DECIMAL(4,1) DEFAULT 0,
            entry_type VARCHAR(20) DEFAULT 'REGULAR',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id),
            INDEX idx_te_phase_date (project_phase_id, work_date),
            -- (employee_id, work_date) e non il solo employee_id: le tre query del Timesheet
            -- filtrano la persona E l'intervallo di giorni. ix_te_work_date serve invece agli
            -- aggregati globali (home, anomalie), dove la persona non filtra. Allineati alla
            -- v91 (E2): un database nuovo nasce già con l'assetto giusto.
            INDEX ix_te_employee_date (employee_id, work_date),
            INDEX ix_te_work_date (work_date)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // «Extra Lavoro» (segnalazione #39): tabella LATERALE alle ore, non una colonna.
        // Presenza della riga = il PM l'ha spostata su Extra Lavoro; `counts_in_project`
        // = se quelle ore pesano ancora sui costi della commessa (0 = no, è il default).
        // Sta qui, prima della vista, perché `v_timesheet_with_section` la legge.
        c.Execute(@"CREATE TABLE IF NOT EXISTS timesheet_extra_work (
            entry_id INT NOT NULL PRIMARY KEY,
            counts_in_project TINYINT(1) NOT NULL DEFAULT 0,
            moved_by INT NULL,
            moved_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            note VARCHAR(300) NOT NULL DEFAULT '',
            CONSTRAINT fk_tew_entry FOREIGN KEY (entry_id)
                REFERENCES timesheet_entries(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // STANDALONE — nessuna FK verso tabelle app
        // (eccezione: codex_compositions.child_catalog_id → catalog_items, FK aggiunta in v10)
        // ══════════════════════════════════════════════════════════

        // remote_id NULL = codice generato localmente (ConfirmReservation), non ancora presente sul
        // Codex remoto. Il sync (CodexSyncService) fa UPSERT per remote_id: gli id locali sono stabili
        // tra i sync, quindi le FK di codex_compositions/codex_item_references restano valide.
        // codice_nuovo = NUOVA CODIFICA (ampliamento Codex 21/07/2026): di proprietà di ATEC PM,
        // il sync remoto NON la tocca mai; si compila SOLO a mano (ricodifica manuale dei 201xxx,
        // famiglie nuove 201 generici / 211 elettrici / 221 pneumatici, …). NULL = non ricodificato.
        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            remote_id INT NULL,
            codice VARCHAR(15) NOT NULL DEFAULT '',
            codice_nuovo VARCHAR(15) NULL,
            code_forn VARCHAR(200) NOT NULL DEFAULT '',
            fornitore VARCHAR(40) NOT NULL DEFAULT '',
            prezzo_forn DECIMAL(7,2) NOT NULL DEFAULT 0,
            iva VARCHAR(3) NOT NULL DEFAULT '',
            produttore VARCHAR(100) NOT NULL DEFAULT '',
            data DATE NULL,
            descr VARCHAR(200) NOT NULL DEFAULT '',
            note TEXT,
            categoria VARCHAR(200) NOT NULL DEFAULT '',
            barcode VARCHAR(200) NOT NULL DEFAULT '',
            tipologia VARCHAR(200) NOT NULL DEFAULT '',
            extra1 VARCHAR(200) NOT NULL DEFAULT '',
            extra2 VARCHAR(200) NOT NULL DEFAULT '',
            extra3 VARCHAR(200) NOT NULL DEFAULT '',
            code_prod VARCHAR(200) NOT NULL DEFAULT '',
            spec VARCHAR(200) NOT NULL DEFAULT '',
            oper INT NOT NULL DEFAULT 0,
            um VARCHAR(10) NOT NULL DEFAULT '',
            ubicazione VARCHAR(200) NOT NULL DEFAULT '',
            codexforn VARCHAR(200) NOT NULL DEFAULT '',
            synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            sync_hash CHAR(64) NULL,
            UNIQUE KEY uq_codex_remote_id (remote_id),
            UNIQUE KEY uq_codex_codice_nuovo (codice_nuovo),
            INDEX idx_codice (codice),
            INDEX idx_fornitore (fornitore),
            INDEX idx_categoria (categoria)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_reservations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            prefix VARCHAR(10) NOT NULL,
            reserved_code VARCHAR(50) NOT NULL,
            reserved_by VARCHAR(100) NOT NULL,
            reserved_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at DATETIME NOT NULL,
            status ENUM('RESERVED','CONFIRMED','RELEASED') NOT NULL DEFAULT 'RESERVED',
            INDEX idx_prefix_status (prefix, status),
            INDEX idx_expires (expires_at, status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Figlio = articolo Codex (child_codex_id) OPPURE articolo catalogo (child_catalog_id):
        // esattamente uno dei due è valorizzato. La FK verso catalog_items è aggiunta dalla
        // migrazione v10 perché catalog_items viene creata più avanti (QuoteDbService.InitTables).
        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_compositions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            parent_codex_id INT NOT NULL,
            child_codex_id INT NULL,
            child_catalog_id INT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (parent_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (child_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            INDEX idx_parent (parent_codex_id),
            INDEX idx_child (child_codex_id),
            INDEX idx_cc_catalog (child_catalog_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_item_references (
            id INT AUTO_INCREMENT PRIMARY KEY,
            source_codex_id INT NOT NULL,
            ref_codex_id INT NOT NULL,
            ref_type VARCHAR(10) NOT NULL COMMENT '201 o 401',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (source_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (ref_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            UNIQUE KEY uq_source_ref (source_codex_id, ref_type),
            INDEX idx_source (source_codex_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // TEMPLATE CARTELLE COMMESSE
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_template_folders (
            id INT AUTO_INCREMENT PRIMARY KEY,
            parent_id INT NULL,
            name VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (parent_id) REFERENCES project_template_folders(id) ON DELETE CASCADE,
            INDEX idx_ptf_parent (parent_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_template_files (
            id INT AUTO_INCREMENT PRIMARY KEY,
            folder_id INT NOT NULL,
            file_name VARCHAR(300) NOT NULL,
            disk_path VARCHAR(500) NOT NULL,
            file_size BIGINT NOT NULL DEFAULT 0,
            uploaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            uploaded_by INT NULL,
            FOREIGN KEY (folder_id) REFERENCES project_template_folders(id) ON DELETE CASCADE,
            FOREIGN KEY (uploaded_by) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_ptfi_folder (folder_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // SISTEMA PERMESSI A LIVELLI (stile VisiWin7)
        // ══════════════════════════════════════════════════════════

        // NB: `level_value` NON è più UNIQUE (migrazione v59). La tabella elenca i RUOLI, e
        // un ruolo di reparto (access_mode = 'GRANTS') può stare sullo stesso rango di un
        // altro: l'AMM parte da 0 come un tecnico ma vede solo ciò che gli è concesso in
        // auth_role_features. Unico per definizione resta il nome del ruolo.
        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_levels (
            id INT AUTO_INCREMENT PRIMARY KEY,
            level_value INT NOT NULL,
            role_name VARCHAR(30) NOT NULL UNIQUE,
            display_name VARCHAR(50) NOT NULL,
            sort_order INT DEFAULT 0,
            access_mode VARCHAR(10) NOT NULL DEFAULT 'LEVEL'
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_features (
            id INT AUTO_INCREMENT PRIMARY KEY,
            feature_key VARCHAR(100) NOT NULL UNIQUE,
            display_name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT 'navigation',
            min_level INT NOT NULL DEFAULT 0,
            behavior VARCHAR(20) DEFAULT 'HIDDEN'
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Concessioni per RUOLO: la scala dei livelli è lineare e non sa descrivere un
        // reparto (l'amministrazione ha bisogno del SAL, che sta al livello dei PM, ma non
        // di MoM/DDP/Check list). Qui si elencano le funzioni concesse a un singolo ruolo,
        // in sola lettura ('READ') o piene ('FULL').
        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_role_features (
            id INT AUTO_INCREMENT PRIMARY KEY,
            role_name VARCHAR(30) NOT NULL,
            feature_key VARCHAR(100) NOT NULL,
            access VARCHAR(10) NOT NULL DEFAULT 'FULL',
            UNIQUE KEY uk_role_feature (role_name, feature_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // SEED DATA
        // ══════════════════════════════════════════════════════════

        // Seed livelli autorizzazione
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_levels") == 0)
        {
            // ADMIN è il livello più alto: il vecchio DEVELOPER (4) è stato tolto il
            // 31/07/2026 (migrazione v57) perché non era assegnabile a nessuno.
            c.Execute(@"INSERT INTO auth_levels (level_value, role_name, display_name, sort_order, access_mode) VALUES
                (0, 'TECH',          'Tecnico',          0, 'LEVEL'),
                (1, 'RESP_REPARTO',  'Resp. Reparto',    1, 'LEVEL'),
                (2, 'PM',            'Project Manager',  2, 'LEVEL'),
                (3, 'ADMIN',         'Amministratore',   3, 'LEVEL'),
                (0, 'AMM',           'Amministrazione',  4, 'GRANTS')");
            Console.WriteLine("[DB] Seed auth_levels completato.");
        }

        // Concessioni dell'ufficio amministrazione (ruolo a lista bianca: vede SOLO queste).
        //
        // 🪤 La condizione guarda ANCHE che il ruolo esista ancora: 'AMM' è stato eliminato da
        // auth_levels con la v66 (la Contabilità è gestita per REPARTO dal 04/08/2026), e la v82
        // ne ha ripulito le concessioni rimaste orfane. Senza il secondo controllo la tabella
        // svuotata riattivava questo seed al riavvio successivo e il ruolo morto tornava a
        // popolarla — successo davvero in sviluppo il 13/08/2026, subito dopo la v82.
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_role_features") == 0
            && c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_levels WHERE role_name = 'AMM'") > 0)
        {
            c.Execute(@"INSERT INTO auth_role_features (role_name, feature_key, access) VALUES
                ('AMM', 'nav.bug_reports', 'FULL'),
                ('AMM', 'nav.sal',         'FULL'),
                ('AMM', 'sal.economics',   'FULL'),
                ('AMM', 'nav.clienti',     'READ')");
            Console.WriteLine("[DB] Seed auth_role_features completato.");
        }

        // Seed feature con livello minimo
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_features") == 0)
        {
            c.Execute(@"INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
                ('nav.dashboard',         'Dashboard',               'navigation', 0, 'HIDDEN'),
                ('nav.timesheet',         'Timesheet',               'navigation', 0, 'HIDDEN'),
                ('nav.commesse',          'Commesse',                'navigation', 0, 'HIDDEN'),
                ('nav.preventivi',        'Preventivi',              'navigation', 2, 'HIDDEN'),
                ('nav.cat_preventivi',    'Cat. Preventivi',         'navigation', 2, 'HIDDEN'),
                ('nav.clienti',           'Clienti',                 'navigation', 2, 'HIDDEN'),
                ('nav.fornitori',         'Fornitori',               'navigation', 2, 'HIDDEN'),
                ('nav.catalogo',          'Catalogo Articoli',       'navigation', 1, 'HIDDEN'),
                ('nav.codex',             'Codex Articoli',          'navigation', 1, 'HIDDEN'),
                ('nav.codex_composizione','Composizione Codex',      'navigation', 1, 'HIDDEN'),
                ('nav.utenti',            'Utenti',                  'navigation', 3, 'HIDDEN'),
                ('nav.config_sezioni',    'Configurazione Sezioni',  'navigation', 3, 'HIDDEN'),
                ('nav.ddp_destinazioni',  'Conf. DDP',               'navigation', 2, 'HIDDEN'),
                ('nav.ddp_aggregazioni',  'Aggregazioni DDP',        'navigation', 2, 'HIDDEN'),
                ('nav.mom',               'Verbali e Note MoM',      'navigation', 2, 'HIDDEN'),
                ('nav.gestore_ddp',       'Gestore DDP',             'navigation', 2, 'HIDDEN'),
                ('nav.checklist',         'Check list',              'navigation', 2, 'HIDDEN'),
                ('nav.milestones',        'Milestones',              'navigation', 2, 'HIDDEN'),
                ('nav.work_requests',     'Lavorazioni',             'navigation', 2, 'HIDDEN'),
                ('nav.sal',               'SAL / Fatturazione',      'navigation', 2, 'HIDDEN'),
                ('nav.bilancio',          'Bilancio Commessa',       'navigation', 2, 'HIDDEN'),
                ('nav.ore_commessa',      'Ore Commessa',            'navigation', 2, 'HIDDEN'),
                ('sal.economics',         'SAL — Dati economici',    'data',       2, 'HIDDEN'),
                ('project.dettagli',      'Commessa — Dettagli',     'project',    2, 'HIDDEN'),
                ('project.flusso_cassa',  'Commessa — Flusso di Cassa','project',  2, 'HIDDEN'),
                ('project.chat',          'Commessa — Chat',         'project',    0, 'HIDDEN'),
                ('project.ddp_commerciale','Commessa — DDP Commerciali','project', 0, 'HIDDEN'),
                ('project.ddp_officina',  'Commessa — DDP Officina', 'project',    0, 'HIDDEN'),
                ('project.documenti',     'Commessa — Documenti',    'project',    0, 'HIDDEN'),
                ('nav.project_templates', 'Template Commesse',       'navigation', 2, 'HIDDEN'),
                ('nav.backup',            'Backup DB',               'navigation', 3, 'HIDDEN'),
                ('nav.permessi',          'Gestione Permessi',       'navigation', 3, 'HIDDEN'),
                ('nav.digest_email',      'Digest Email',            'navigation', 3, 'HIDDEN'),
                ('nav.anagrafica_attivita','Anagrafica Attività',    'navigation', 2, 'HIDDEN'),
                ('nav.scadenze',          'Scadenze',                'navigation', 2, 'HIDDEN'),
                ('action.create_project', 'Crea Commessa',           'action',     2, 'DISABLED'),
                ('action.edit_project',   'Modifica Commessa',       'action',     2, 'DISABLED'),
                ('action.delete_project', 'Elimina Commessa',        'action',     3, 'HIDDEN'),
                ('data.budget',           'Dati Budget',             'data',       2, 'HIDDEN'),
                ('data.costs',            'Dati Costi',              'data',       2, 'HIDDEN'),
                ('data.revenue',          'Dati Ricavi',             'data',       2, 'HIDDEN'),
                ('data.hourly_cost',      'Costo Orario',            'data',       3, 'HIDDEN'),
                ('resources.edit',        'Modifica Allocazioni Risorse', 'action', 1, 'DISABLED')");
            Console.WriteLine("[DB] Seed auth_features completato.");
        }

        // Seed tariffe trasferta predefinite
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM tariff_options") == 0)
        {
            c.Execute(@"INSERT INTO tariff_options (tariff_type, label, value) VALUES
                ('COST_PER_KM', '', 0.900), ('COST_PER_KM', '', 1.100),
                ('DAILY_FOOD', '', 25.000), ('DAILY_FOOD', '', 50.000), ('DAILY_FOOD', '', 80.000),
                ('DAILY_HOTEL', '', 80.000), ('DAILY_HOTEL', '', 100.000), ('DAILY_HOTEL', '', 120.000),
                ('DAILY_ALLOWANCE', '', 20.000), ('DAILY_ALLOWANCE', '', 40.000), ('DAILY_ALLOWANCE', '', 60.000),
                -- Officine interne (#87): tre lavorazioni, tre costi orari.
                ('HOURLY_RATE', 'Stampa 3D', 20.000), ('HOURLY_RATE', 'Carpenteria', 35.000),
                ('HOURLY_RATE', 'Meccanica', 50.000)");
            Console.WriteLine("[DB] Seed tariff_options completato.");
        }

        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM app_config") == 0)
        {
            c.Execute(@"INSERT INTO app_config (config_key, config_value, description) VALUES
                ('BasePath', 'C:\\ATEC_Commesse', 'Percorso base cartelle commesse')");
        }

        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees") == 0)
        {
            string adminHash = BCrypt.Net.BCrypt.HashPassword("admin");
            c.Execute(@"INSERT INTO employees (first_name, last_name, email, username, password_hash, user_role, status)
                VALUES ('Admin', 'ATEC', 'admin@atec.it', 'admin', @Hash, 'ADMIN', 'ACTIVE')",
                new { Hash = adminHash });
            Console.WriteLine("[DB] Utente admin di default creato con bcrypt (user: admin / pwd: admin)");
        }

        // (Qui la vista v_timesheet_with_section veniva creata a metà del bootstrap, con l'errore
        // ingoiato da un try/catch: in sviluppo, su database nuovo, scriveva un warning e tirava
        // avanti. Adesso la fa EnsureViews, in fondo e in tutti gli ambienti — vedi lì il perché.)

        // Tabelle di tutti i moduli (idempotenti, nessun dato)
        EnsureModuleTables(c);

        // Seed anagrafiche SAL (idempotenti: solo a tabella vuota)
        SalDbService.SeedConditions(c, _logger);
        SalDbService.SeedSapCausali(c, _logger);
        SalDbService.SeedPaymentStates(c, _logger);

        // Migrazioni versionate (dopo le CREATE TABLE idempotenti).
        //
        // Su un database APPENA CREATO le migrazioni girano su uno schema che le contiene già
        // (l'ha appena scritto il bootstrap qui sopra): passano a vuoto, e se una inciampa non
        // significa che lo schema sia rotto. Lì si resta tolleranti come prima. Su un database
        // esistente, invece, un fallimento è un problema vero e ferma l'avvio.
        HashSet<int> applied = GetAppliedVersions(c);
        BackfillLegacyVersions(c, applied, databasePopolato: !freshDatabase);
        ApplyVersionedMigrations(c, applied, _stopOnMigrationError && !freshDatabase);

        // Le viste per ultime: nominano tabelle che nascono dalle migrazioni.
        EnsureViews(c);
        EnsureCatalogo(c);

        LogTableCount(c);
    }

    /// <summary>
    /// CREATE TABLE IF NOT EXISTS di tutti i moduli, più le ALTER guidate che ciascuno si porta
    /// dietro. Solo DDL: nessun seed di dati, quindi è sicura ad ogni avvio anche in produzione
    /// (non fa riapparire righe cancellate dall'utente). Chiamata da entrambi i percorsi di
    /// InitDatabase, così un modulo nuovo non può più esistere in sviluppo e mancare in produzione.
    /// </summary>
    private void EnsureModuleTables(MySqlConnection c)
    {
        // Modulo Preventivi/Catalogo (ApplyMigrations crea anche le tabelle costing/materiali)
        QuoteDbService quoteDb = new(this);
        quoteDb.InitTables(c);
        quoteDb.ApplyMigrations(c);

        // Modulo Gamma Robot (distinta schede/componenti per robot+quadro)
        new GammaRobotDbService(this).InitTables(c);

        // Modulo MoM (verbali di riunione → action item)
        new MoMDbService(this).InitTables(c);

        // Modulo Check list / Attività (attività per commessa reale o gruppo generico)
        new CheckListDbService(this).InitTables(c);

        // Modulo Segnalazioni (bug e migliorie sul gestionale, con allegati)
        new BugReportsDbService(this).InitTables(c);

        // Moduli Anagrafica attività (catalogo globale) + Milestone (pianificazione per-commessa)
        MilestonesDbService.InitTables(c, _logger);

        // Modulo Gestione Risorse (allocazioni op/flex/ferie su dipendenti)
        new ResourcesDbService(this).InitTables(c);

        // Modulo SAL / Fatturazione a stati d'avanzamento
        SalDbService.InitTables(c, _logger);

        // Modulo Lavorazioni meccaniche (work requests, griglia per commessa + pannello globale)
        EnsureWorkRequestsTable(c);
    }

    // ══════════════════════════════════════════════════════════════
    // IL LOCK DELLE MIGRAZIONI (blocco A2)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Quanto si aspetta il turno prima di rinunciare.
    /// <para><b>Perché trenta secondi e non sessanta</b> (che è il numero scritto nel piano):
    /// aspettare non serve quasi mai a niente. Se il lucchetto è in mano a qualcun altro, quel
    /// qualcuno sta migrando e ci metterà il tempo che ci mette — molto più di un minuto se la
    /// migrazione è grossa. Rinunciare presto è meglio: il servizio Windows si riavvia da solo
    /// dopo 5 secondi e ritenta, mentre ogni secondo passato in coda è un secondo tolto alla
    /// pazienza dello script di aggiornamento, che oltre la sua soglia torna alla versione
    /// precedente.</para>
    /// <para>Si cambia da <c>appsettings.json</c> (<c>Migrations:LockTimeoutSeconds</c>) — che sul
    /// server è protetto dagli aggiornamenti.</para>
    /// </summary>
    private int LockTimeoutSeconds => Math.Max(1, _lockTimeoutSeconds);

    /// <summary>
    /// Il nome del lock <b>contiene il database</b>: <c>GET_LOCK</c> è un lucchetto di tutto il
    /// SERVER MySQL, non dello schema. Con un nome fisso, il database di prova di un test e
    /// quello di lavoro si bloccherebbero a vicenda sulla stessa macchina — e i test, che ne
    /// creano uno per volta e girano in parallelo, si metterebbero in coda tutti dietro allo
    /// stesso lucchetto.
    /// </summary>
    public static string NomeLockMigrazioni(string database)
    {
        string nome = $"atec_pm_migrate:{database}";
        // MySQL rifiuta i nomi oltre 64 caratteri (dalla 5.7 è un errore, non un troncamento).
        return nome.Length <= 64 ? nome : nome[..64];
    }

    private string NomeDelLock() => NomeLockMigrazioni(new MySqlConnectionStringBuilder(_cs).Database);

    /// <summary>
    /// Prende il lock esclusivo sullo schema. Se non ci riesce <b>l'avvio si interrompe</b>: chi
    /// ce l'ha in mano sta migrando lo stesso database, e partire in due vorrebbe dire far girare
    /// la stessa migrazione due volte su uno schema che non sa tornare indietro.
    /// </summary>
    private void PrendiIlLockDelleMigrazioni(MySqlConnection c)
    {
        string nome = NomeDelLock();

        // GET_LOCK: 1 = preso, 0 = scaduto il tempo (ce l'ha un altro), NULL = errore.
        //
        // 🪤 commandTimeout OBBLIGATORIO. Il driver ha un suo tempo massimo per comando (30
        // secondi, default di MySqlConnector): con un'attesa più lunga sarebbe LUI a interrompere
        // la query, e al posto del messaggio qui sotto («un altro processo sta migrando») uscirebbe
        // un generico timeout di connessione, che manda a cercare il guasto nella rete.
        int? esito = c.ExecuteScalar<int?>("SELECT GET_LOCK(@Nome, @Secondi)",
            new { Nome = nome, Secondi = LockTimeoutSeconds },
            commandTimeout: LockTimeoutSeconds + 15);

        if (esito == 1)
        {
            _logger.LogInformation("[Migrations] Lock '{Nome}' acquisito.", nome);
            return;
        }

        throw new InvalidOperationException(
            $"Un altro processo sta già aggiornando lo schema di questo database (lock '{nome}' " +
            $"non ottenuto entro {LockTimeoutSeconds}s" +
            (esito == null ? ", il server MySQL ha risposto NULL" : "") + "). " +
            "L'avvio è interrotto apposta: due processi che migrano insieme farebbero girare la stessa " +
            "migrazione due volte, e il DDL di MySQL non torna indietro. Verificare che non ci sia una " +
            "seconda istanza del servizio AtecPmServer viva, poi riavviare.");
    }

    /// <summary>
    /// Rilascia il lock. <b>Serve davvero</b>: con il pooling acceso (nessuna stringa di
    /// connessione lo disattiva) la <c>Dispose</c> non chiude la sessione MySQL, la restituisce
    /// al pool — quindi non si può contare sulla «fine della sessione» per liberare il lucchetto.
    /// <para>Non solleva mai: se il rilascio fallisce, l'avvio è comunque andato a buon fine e
    /// far fallire il server a quel punto sarebbe peggio del problema. Il lock resterebbe preso
    /// finché il pool non ricicla quella sessione (il reset della connessione libera i lock
    /// nominali) o finché non scade per inattività — nel frattempo un ripristino da backup si
    /// fermerebbe dicendolo, che è il comportamento voluto.</para>
    /// </summary>
    private void RilasciaIlLockDelleMigrazioni(MySqlConnection c)
    {
        try
        {
            int? esito = c.ExecuteScalar<int?>("SELECT RELEASE_LOCK(@Nome)", new { Nome = NomeDelLock() });

            // 1 = rilasciato. 0 = ce l'ha un'altra sessione (non dovrebbe mai capitare).
            // NULL = non esisteva: normale se l'acquisizione era fallita, ed è il motivo per cui
            // questo metodo si può chiamare sempre, anche dal finally di un avvio andato male.
            if (esito == 0)
                _logger.LogWarning("[Migrations] Il lock risulta in mano a un'altra sessione: non l'ho rilasciato io.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Migrations] Rilascio del lock non riuscito: {Messaggio}", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // LE VISTE (blocco A2)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Riallinea le viste alla loro definizione di oggi. Gira a <b>ogni avvio, in tutti gli
    /// ambienti</b>, e <b>dopo</b> le migrazioni.
    ///
    /// <para><b>Perché a ogni avvio.</b> Una vista non ha stato da conservare: <c>CREATE OR
    /// REPLACE</c> la riscrive e basta. Finché è stata una migrazione a crearla, la definizione
    /// in produzione era quella del giorno in cui quella migrazione era passata — ed è così che
    /// per mesi il consuntivo del Bilancio ha girato su una vista con una JOIN INNER che buttava
    /// via le ore delle fasi locali, mentre in sviluppo ne girava un'altra (v69). Adesso la
    /// definizione buona arriva ad ogni riavvio, dovunque.</para>
    ///
    /// <para><b>Perché dopo le migrazioni.</b> La vista nomina tabelle e colonne che nascono
    /// dalle migrazioni (<c>timesheet_extra_work</c> è della v80): su un database indietro,
    /// crearla prima fallirebbe con «Table doesn't exist».</para>
    /// </summary>
    private void EnsureViews(MySqlConnection c)
    {
        try
        {
            c.Execute(TimesheetSectionViewSql);
            _logger.LogInformation("[InitDatabase] Viste riallineate (v_timesheet_with_section).");
        }
        catch (Exception ex)
        {
            // Non è un dettaglio estetico: da questa vista escono i costi consuntivi del Bilancio.
            // Una vista vecchia o assente non dà errore a nessuno — dà numeri sbagliati, che è
            // peggio. Quindi l'avvio si ferma, salvo la manopola d'emergenza.
            _logger.LogError(ex, "[InitDatabase] Vista v_timesheet_with_section NON allineata: {Messaggio}", ex.Message);

            if (_stopOnMigrationError)
                throw new InvalidOperationException(
                    $"Impossibile allineare la vista v_timesheet_with_section: {ex.Message}. " +
                    "L'avvio è interrotto perché da quella vista escono i costi consuntivi del Bilancio: " +
                    "con una definizione vecchia i numeri sarebbero sbagliati senza nessun errore. " +
                    "In emergenza, per ripartire subito, impostare Migrations:StopOnError=false in appsettings.json.", ex);
        }
    }

    /// <summary>
    /// Allinea <c>auth_features</c> al catalogo unico dei permessi (rebuild §12, passo 2):
    /// stesso patto di <see cref="EnsureViews"/> — gira a ogni avvio, in tutti gli ambienti,
    /// dentro il lock delle migrazioni. Il lavoro vero sta in <see cref="CatalogoPermessiSync"/>.
    /// </summary>
    private void EnsureCatalogo(MySqlConnection c)
    {
        try
        {
            CatalogoPermessiSync.Esito esito = CatalogoPermessiSync.Allinea(c, _logger);
            if (esito.NienteDaFare)
                _logger.LogInformation("[InitDatabase] Catalogo permessi già allineato ({Orfane} orfane).", esito.Orfane.Count);
            else
                _logger.LogInformation(
                    "[InitDatabase] Catalogo permessi allineato: {Nuove} nuove, {Rinominate} rinominate, {Ritirate} ritirate, {Ripescate} ripescate, {Etichette} etichette ({Orfane} orfane).",
                    esito.Nuove, esito.Rinominate, esito.Ritirate, esito.Ripescate, esito.EtichetteAggiornate, esito.Orfane.Count);
        }
        catch (Exception ex)
        {
            // Un catalogo non allineato non dà errori a nessuno: dà una scheda permessi che
            // mente e chiavi che il jolly non copre. Quindi l'avvio si ferma, salvo manopola.
            _logger.LogError(ex, "[InitDatabase] Catalogo permessi NON allineato: {Messaggio}", ex.Message);

            if (_stopOnMigrationError)
                throw new InvalidOperationException(
                    $"Impossibile allineare auth_features dal catalogo unico: {ex.Message}. " +
                    "L'avvio è interrotto perché un catalogo disallineato fa mentire la scheda permessi " +
                    "e lascia fuori dal jolly le chiavi nuove. " +
                    "In emergenza, per ripartire subito, impostare Migrations:StopOnError=false in appsettings.json.", ex);
        }
    }

    private void LogTableCount(MySqlConnection c)
    {
        int tableCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()");
        _logger.LogInformation("[InitDatabase] Schema verificato: {TableCount} tabelle presenti", tableCount);
    }

    private static bool TableExists(MySqlConnection c, string tableName)
    {
        return c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @Table", new { Table = tableName }) > 0;
    }

    // ══════════════════════════════════════════════════════════════
    // SCHEMA VERSIONING
    // ══════════════════════════════════════════════════════════════

    // Qui stava `LatestSchemaVersion`, la costante da alzare a mano ad ogni migrazione nuova.
    // Non esiste più: le migrazioni sono classi in Migrations/, il MigrationRunner le scopre
    // dall'assembly e la versione più alta la ricava da loro. È la trappola costata la v66 in
    // produzione (alzata in sviluppo, dimenticata al deploy), e adesso non c'è più niente da
    // ricordarsi: aggiungere una migrazione vuol dire creare un file.

    /// <summary>
    /// Ultima versione esistente quando è stata cambiata la regola delle migrazioni (14/08/2026:
    /// da <c>MAX(version)</c> all'insieme delle versioni registrate). <see cref="BackfillLegacyVersions"/>
    /// sana i buchi solo fino a qui: sotto questa soglia un buco è l'eredità ambigua della vecchia
    /// regola e va timbrato; sopra, è una migrazione fallita davvero e va ritentata.
    /// <b>Non va più alzata</b> — alzarla rimetterebbe in circolo il difetto che A0 ha chiuso.
    /// </summary>
    private const int LegacyCutoffVersion = 87;

    /// <summary>
    /// Il registro delle migrazioni.
    /// <para><b>`success`, `error_text`, `duration_ms` (blocco A2, 15/08/2026)</b>: una migrazione
    /// che fallisce lascia la sua riga con <c>success = 0</c> e il messaggio dell'errore. Prima
    /// l'unica traccia era una riga in un log che si ruota dopo 30 giorni: chi apriva il registro
    /// il giorno dopo vedeva solo un buco, senza sapere se quella versione fosse mai partita.
    /// Adesso il perché resta scritto accanto alla versione, e la durata dice quali migrazioni
    /// stanno diventando lente sul database vero.</para>
    /// <para>⚠️ <c>success = 0</c> vuol dire <b>non applicata</b>: chiunque legga questa tabella
    /// per sapere cosa è già stato fatto deve filtrare (<see cref="GetAppliedVersions"/>).</para>
    /// </summary>
    /// <summary>
    /// Crea (e completa, se nata vecchia) la tabella <c>schema_migrations</c>. È <c>public</c>
    /// perché i test del <see cref="Migrations.MigrationRunner"/> hanno bisogno di questa
    /// tabella e <b>solo</b> di questa: costruire le altre 119 per provare il runner costava
    /// 5 secondi a test.
    /// </summary>
    public static void EnsureSchemaMigrationsTable(MySqlConnection c)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS schema_migrations (
            version INT NOT NULL PRIMARY KEY,
            description VARCHAR(200) NOT NULL DEFAULT '',
            applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            success TINYINT(1) NOT NULL DEFAULT 1,
            error_text TEXT NULL,
            duration_ms INT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // CREATE TABLE IF NOT EXISTS non tocca una tabella che esiste già: su un database nato con
        // una versione più vecchia dell'applicazione le colonne potrebbero mancare, e allora OGNI
        // registrazione fallirebbe («Unknown column») — cioè nessuna migrazione risulterebbe mai
        // applicata. Costa cinque query all'avvio e toglie di mezzo un modo intero di rompersi.
        //
        // Queste colonne NON possono essere aggiunte da una migrazione: sarebbe un cerchio —
        // la migrazione che aggiunge la colonna dovrebbe registrarsi in una colonna che ancora
        // non c'è. Vanno qui, prima di tutto.
        //
        // `success` nasce a 1: le righe scritte prima del 15/08/2026 sono migrazioni riuscite
        // (allora una fallita non lasciava riga per niente), e devono restare tali.
        AddColumnIfMissing(c, "schema_migrations", "description", "VARCHAR(200) NOT NULL DEFAULT ''");
        AddColumnIfMissing(c, "schema_migrations", "applied_at", "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");
        AddColumnIfMissing(c, "schema_migrations", "success", "TINYINT(1) NOT NULL DEFAULT 1");
        AddColumnIfMissing(c, "schema_migrations", "error_text", "TEXT NULL");
        AddColumnIfMissing(c, "schema_migrations", "duration_ms", "INT NULL");
    }

    private static int GetSchemaVersion(MySqlConnection c)
    {
        return c.ExecuteScalar<int>("SELECT COALESCE(MAX(version), 0) FROM schema_migrations WHERE success = 1");
    }

    /// <summary>
    /// Le versioni <b>applicate davvero</b>. È l'unica fonte di verità su cosa è già stato fatto:
    /// <see cref="GetSchemaVersion"/> resta solo per i messaggi di log.
    /// <para><c>WHERE success = 1</c>: una migrazione fallita lascia la sua riga (con l'errore
    /// dentro), ma <b>non</b> è applicata. Senza questo filtro il fallimento verrebbe letto come
    /// una riuscita e la migrazione non sarebbe mai più ritentata — cioè esattamente il difetto
    /// che A0 ha chiuso, rientrato dalla finestra.</para>
    /// </summary>
    private static HashSet<int> GetAppliedVersions(MySqlConnection c) =>
        c.Query<int>("SELECT version FROM schema_migrations WHERE success = 1").ToHashSet();

    /// <summary>
    /// Le versioni che hanno una riga, riuscite o no. Serve al backfill per <b>non</b> timbrare
    /// come «eredità storica» una versione che ha invece un fallimento registrato.
    /// </summary>
    private static HashSet<int> GetRegisteredVersions(MySqlConnection c) =>
        c.Query<int>("SELECT version FROM schema_migrations").ToHashSet();

    /// <summary>
    /// Sana <b>una volta sola</b> i buchi lasciati dalla vecchia regola <c>MAX(version)</c>.
    /// <para>
    /// Su un database già in esercizio, una versione mancante sotto il massimo è <b>ambigua</b>: può
    /// essere una migrazione fallita davvero, oppure una che ha lavorato senza registrarsi. Il
    /// codice non ha modo di distinguerle, ed è per questo che qui non si riesegue nulla: con la
    /// regola vecchia quelle versioni erano comunque considerate superate, e rilanciarle adesso
    /// sarebbe la cosa pericolosa. La v75, per dire, fa <c>DELETE FROM ddp_status_transitions WHERE
    /// ddp_type='OFFICINA'</c> e riscrive la matrice: rieseguirla oggi cancellerebbe le modifiche
    /// fatte a mano da «Conf. DDP» dopo l'08/08.
    /// </para>
    /// <para>
    /// Quindi: le versioni mancanti <b>sotto il massimo</b> vengono marcate come applicate, con una
    /// descrizione che dice a chiare lettere che sono state assunte, non eseguite. Da lì in avanti
    /// vale la regola nuova, e un fallimento non può più sparire. Il log elenca le versioni sanate:
    /// è l'unico posto dove si può leggere che su quel database qualcosa, in passato, potrebbe non
    /// essere passato — vanno verificate a mano.
    /// </para>
    /// </summary>
    /// <param name="databasePopolato">
    /// true quando il database contiene già dati veri (non è il bootstrap di uno vuoto). Serve a
    /// riconoscere il caso del <b>registro azzerato</b>: <c>FullBackupService.RipristinaDatabase</c>
    /// fa TRUNCATE di tutte le tabelle, <c>schema_migrations</c> compresa, e ingoia i fallimenti di
    /// riga. Un ripristino andato storto a metà lascia quindi dati veri e registro vuoto: senza
    /// questo controllo partirebbero tutte e 87 le migrazioni su dati di produzione — la v22
    /// riporterebbe a 22% le righe SAL messe a IVA vuota, la v26 farebbe risorgere lavorazioni
    /// cancellate, la v55 duplicherebbe la cronistoria. Meglio non partire e farsi chiamare.
    /// </param>
    /// <returns>Le versioni sanate (vuoto: nessun buco, il caso normale).</returns>
    private List<int> BackfillLegacyVersions(MySqlConnection c, HashSet<int> applied, bool databasePopolato)
    {
        List<int> sanate = new();

        // Tutte le righe, riuscite o fallite. Il registro è «perso» solo se non c'è NESSUNA riga:
        // un database con soli fallimenti registrati ha un registro sano che racconta un guasto,
        // e fermarsi dicendogli «la tabella è vuota» manderebbe a cercare nel posto sbagliato.
        HashSet<int> conRiga = GetRegisteredVersions(c);

        if (conRiga.Count == 0)
        {
            // Database mai migrato: le migrazioni devono girare davvero.
            if (!databasePopolato) return sanate;

            throw new InvalidOperationException(
                "schema_migrations è vuota ma il database contiene già dati: registro delle migrazioni perso " +
                "(tipicamente un ripristino da backup interrotto a metà). L'avvio è interrotto perché rieseguire " +
                "le migrazioni su dati veri li rovinerebbe. Ripristinare di nuovo il backup completo, oppure — se " +
                "lo schema è notoriamente aggiornato — ripopolare schema_migrations con le versioni 1..N prima di riavviare.");
        }

        // Un registro fatto di sole migrazioni fallite non ha niente da sanare: le versioni sono
        // già tutte registrate e verranno ritentate dal runner.
        if (applied.Count == 0) return sanate;

        // Il backfill vale SOLO per le versioni storiche: una migrazione FUTURA che fallisce (con
        // StopOnError=false) e viene scavalcata da una successiva non deve essere timbrata come
        // applicata al riavvio dopo — sarebbe esattamente il difetto che A0 chiude, tornato dalla
        // finestra. Da qui in avanti un buco resta un buco, e la migrazione viene ritentata.
        //
        // E non è un buco la versione che ha una riga con success = 0: quello è un fallimento
        // REGISTRATO, di cui si conosce data ed errore. Timbrarlo come «eredità della vecchia
        // regola» cancellerebbe un guasto noto e certificherebbe applicata una migrazione che si
        // sa non esserlo.
        int max = Math.Min(applied.Max(), LegacyCutoffVersion);
        for (int v = 1; v < max; v++)
            if (!applied.Contains(v) && !conRiga.Contains(v)) sanate.Add(v);

        if (sanate.Count == 0) return sanate;

        foreach (int v in sanate)
        {
            c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (@V, @D)",
                new { V = v, D = "backfill 14/08/2026: versione assunta applicata dalla vecchia regola MAX(version), non eseguita ora" });
            applied.Add(v);
        }

        _logger.LogWarning(
            "[Migrations] Sanate {Count} versioni mancanti sotto v{Max}: {Elenco}. " +
            "Erano considerate superate dalla vecchia regola MAX(version) e NON sono state rieseguite " +
            "(rilanciarle potrebbe sovrascrivere dati). Verificare a mano che il loro effetto sia presente.",
            sanate.Count, max, string.Join(", ", sanate.Select(v => "v" + v)));

        return sanate;
    }

    /// <summary>
    /// Applica le migrazioni che <b>non risultano registrate</b> in <c>schema_migrations</c>.
    /// <para>
    /// <b>Perché un insieme e non più <c>MAX(version)</c>.</b> Fino al 14/08/2026 il cancello di
    /// ogni blocco era <c>currentVersion &lt; N</c> con <c>currentVersion = MAX(version)</c>, e ogni
    /// blocco ingoiava le proprie eccezioni con un warning. Bastava che la vN fallisse e la vN+1
    /// riuscisse — cosa che accade se si corregge una migrazione mentre se ne aggiunge un'altra —
    /// perché <c>MAX</c> salisse oltre la vN e quella <b>non venisse più ritentata</b>: schema a
    /// metà, e come unica traccia un warning in un log che si ruota dopo 30 giorni.
    /// Non è teorico: la v75 (08/08 14:48, «Illegal mix of collations») e la v80 (08/08 23:11,
    /// «Unknown column») sono fallite davvero. Si sono salvate solo perché nessuna migrazione
    /// successiva è passata nei minuti in cui erano rotte.
    /// </para>
    /// <para>
    /// Ora ogni versione è pendente finché <b>la sua riga</b> non è in <c>schema_migrations</c>, e
    /// un fallimento ferma l'avvio invece di lasciar proseguire il server con lo schema incompleto.
    /// </para>
    /// <para>
    /// <b>Le migrazioni non stanno più qui dentro</b> (blocco A1, 15/08/2026): sono una classe per
    /// versione in <c>Migrations/</c>, che il <see cref="MigrationRunner"/> scopre dall'assembly.
    /// Questo metodo si limita a contare cosa manca, a lanciarlo e a controllare che dopo non
    /// resti nessun buco.
    /// </para>
    /// </summary>
    /// <param name="applied">Versioni già registrate, comprese quelle sanate da
    /// <see cref="BackfillLegacyVersions"/>.</param>
    /// <param name="stopOnError">false solo sul bootstrap di un database vuoto, dove le migrazioni
    /// girano su uno schema che le contiene già e un loro inciampo non significa nulla.</param>
    private void ApplyVersionedMigrations(MySqlConnection c, HashSet<int> applied, bool stopOnError)
    {
        // Il runner scopre le migrazioni dall'assembly: aggiungerne una vuol dire creare un
        // file in Migrations/, non alzare una costante qui.
        MigrationRunner runner = new(_logger);

        // Zero migrazioni trovate non è mai una situazione normale: vorrebbe dire che la
        // riflessione non vede le classi (pubblicazione con trimming, assembly sbagliato). Senza
        // questo controllo il metodo scriverebbe «schema aggiornato» e proseguirebbe su un
        // database dove NON è stato applicato niente — il silenzio più pericoloso possibile.
        if (runner.Migrazioni.Count == 0)
            throw new InvalidOperationException(
                "Nessuna migrazione trovata nell'assembly del server: le classi di Migrations/ non " +
                "sono raggiungibili per riflessione. L'avvio è interrotto perché lo schema " +
                "risulterebbe aggiornato senza che sia stato applicato niente.");

        int obiettivo = runner.VersioneMassima;

        int pendenti = 0;
        for (int v = 1; v <= obiettivo; v++)
            if (!applied.Contains(v)) pendenti++;

        if (pendenti == 0)
        {
            _logger.LogInformation("[Migrations] Schema aggiornato (v{Version}, tutte le {Count} versioni registrate)",
                obiettivo, obiettivo);
            return;
        }

        _logger.LogInformation("[Migrations] {Pendenti} migrazioni da applicare (schema a v{Version}, obiettivo v{Target})",
            pendenti, applied.Count == 0 ? 0 : applied.Max(), obiettivo);

        EsitoMigrazioni esito = runner.Applica(c, applied, stopOnError);

        // Controllo di chiusura: una migrazione che ha lavorato ma non risulta registrata
        // tornerebbe a girare ad OGNI avvio, per sempre, senza che nessuno se ne accorga (con la
        // vecchia regola MAX lo copriva la versione successiva). Qui si vede subito.
        //
        // Le facoltative fallite (le pulizie) sono l'unica assenza ammessa: hanno già il loro
        // warning e la riga con success = 0, e per costruzione non devono fermare l'avvio.
        HashSet<int> dopo = GetAppliedVersions(c);
        List<int> mancanti = new();
        for (int v = 1; v <= obiettivo; v++)
            if (!dopo.Contains(v) && !esito.FacoltativeFallite.Contains(v)) mancanti.Add(v);

        if (esito.FacoltativeFallite.Count > 0)
            _logger.LogWarning(
                "[Migrations] {Count} pulizie facoltative non riuscite ({Elenco}): il gestionale funziona lo stesso, " +
                "si ritentano al prossimo riavvio. Il motivo è in schema_migrations.error_text.",
                esito.FacoltativeFallite.Count,
                string.Join(", ", esito.FacoltativeFallite.Select(v => "v" + v)));

        if (mancanti.Count > 0)
        {
            string elenco = string.Join(", ", mancanti.Select(v => "v" + v));

            // Il log PRIMA del throw: come servizio Windows l'eccezione dell'avvio non arriva a
            // nessuna console, e senza questa riga il motivo resterebbe invisibile in C:\ATEC_PM\Logs.
            _logger.LogError("[Migrations] Versioni non registrate dopo l'esecuzione: {Elenco}", elenco);

            if (stopOnError)
                throw new InvalidOperationException(
                    $"Migrazioni non registrate dopo l'esecuzione: {elenco}. Lo schema NON è allineato: " +
                    "la migrazione corrispondente non risulta applicata. Il motivo del fallimento è scritto " +
                    "in schema_migrations (colonne success ed error_text): " +
                    "SELECT version, description, success, error_text, duration_ms FROM schema_migrations WHERE success = 0; " +
                    "Correggere la migrazione (o registrare la riga a mano se l'effetto è già presente) e riavviare.");

            _logger.LogWarning("[Migrations] Gireranno di nuovo al prossimo avvio (StopOnError=false).");
            return;
        }

        _logger.LogInformation("[Migrations] Migrazioni applicate fino a v{Version}", obiettivo);
    }
}
