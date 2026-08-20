using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Gli attrezzi che le migrazioni si passano fra loro, e che <c>DbService</c> usa a sua volta
/// quando costruisce lo schema da zero.
///
/// <para><b>Perché esiste.</b> Fino al 14/08/2026 questa roba stava dentro <c>DbService</c>, in
/// membri <c>private</c>: finché le migrazioni erano blocchi dello stesso file bastava, ma una
/// classe in <c>Migrations/</c> non li vede. Il posto giusto non è però «una copia per chi ne ha
/// bisogno»: le due costanti qui sotto (la vista del timesheet e le sezioni delle fasi) sono
/// nate proprio da quella trappola — esistevano in due copie divergenti, una per
/// <c>InitDatabase</c> e una per la migrazione, e in produzione girava quella sbagliata senza
/// nessun errore (v69). <b>Una definizione sola, in un posto solo, letto da entrambi i
/// percorsi.</b></para>
///
/// <para>Le migrazioni la usano con <c>using static ATEC.PM.Server.Migrations.AiutiMigrazione;</c>,
/// quindi le chiamate restano identiche a com'erano dentro <c>DbService</c>.</para>
/// </summary>
internal static class AiutiMigrazione
{
    /// <summary>
    /// Legami fase di anagrafica ↔ sezioni di costo (v73). **Sta in una costante sola** perché la
    /// creano due percorsi — <c>InitDatabase</c> (database nuovo) e la migrazione v73 (database
    /// esistente): è la stessa trappola che con la vista timesheet ha lasciato in produzione una
    /// definizione diversa da quella di sviluppo fino alla v69.
    /// </summary>
    internal const string PhaseTemplateSectionsDdl = @"
        CREATE TABLE IF NOT EXISTS phase_template_sections (
            id INT AUTO_INCREMENT PRIMARY KEY,
            phase_template_id INT NOT NULL,
            cost_section_template_id INT NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_default TINYINT(1) NOT NULL DEFAULT 0,
            UNIQUE KEY uk_pts (phase_template_id, cost_section_template_id),
            FOREIGN KEY (phase_template_id) REFERENCES phase_templates(id) ON DELETE CASCADE,
            FOREIGN KEY (cost_section_template_id) REFERENCES cost_section_templates(id) ON DELETE CASCADE,
            INDEX idx_pts_section (cost_section_template_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";

    /// <summary>
    /// Vista che lega le ore di timesheet alla sezione di costo e al costo orario: è la fonte
    /// del consuntivo «Risorse Atec» del Bilancio (<c>BudgetVsActualController</c>) e della
    /// pagina <c>/bilancio</c>.
    /// <para>
    /// <b>Sta in una costante sola apposta.</b> Fino alla v69 esisteva in due copie divergenti:
    /// quella di <c>InitDatabase</c> (che gira solo in sviluppo) e quella corretta, che viveva
    /// unicamente nello script <c>migrate_view_timesheet.py</c> lanciato a mano. In produzione
    /// era in vigore la versione sbagliata: <c>JOIN phase_templates</c> <b>INNER</b>, che butta
    /// via tutte le righe delle <b>fasi locali</b> (<c>project_phases.phase_template_id IS
    /// NULL</c>, create dalla commessa e non da template) — quelle ore sparivano dal costo
    /// consuntivo senza un errore. Chi tocca questa vista aggiorna la costante, non una copia.
    /// </para>
    /// <para>
    /// Il nome legge nell'ordine: valore locale della fase → snapshot sulla commessa → template.
    /// `LEFT JOIN` sui template perché una fase locale non ne ha uno.
    /// </para>
    /// <para>
    /// <b>La sezione di costo NON ha più il fallback sul template</b> (v74). Da quando una fase
    /// di anagrafica sta su più sezioni, <c>phase_templates.cost_section_template_id</c> ne
    /// contiene una sola: usarlo come ripiego avrebbe attribuito le ore a una sezione **a caso**,
    /// senza nessun errore. La sezione è quella scritta sulla riga della fase di commessa,
    /// congelata dalla v73c per tutte le righe che prima vivevano di ripiego.
    /// </para>
    /// </summary>
    internal const string TimesheetSectionViewSql = @"CREATE OR REPLACE VIEW v_timesheet_with_section AS
        SELECT
            te.id              AS entry_id,
            te.employee_id,
            te.project_phase_id,
            te.work_date,
            te.hours,
            te.entry_type,
            pp.project_id,
            COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS phase_name,
            pp.cost_section_template_id,
            CONCAT(emp.first_name, ' ', emp.last_name) AS employee_name,
            COALESCE(d.hourly_cost, 0) AS hourly_cost,
            -- EXTRA LAVORO (segnalazione #39). Due informazioni distinte:
            --  · is_extra            = la riga è stata spostata dal PM su «Extra Lavoro»;
            --  · counts_in_project   = quella riga conta ancora nei costi della commessa.
            -- Chi non è mai stato spostato non ha riga nella tabella laterale e conta: è il
            -- COALESCE a dirlo. Una riga spostata parte esclusa (0) e il PM può rimetterla
            -- dentro (1) per vedere quanto pesa sulla redditività.
            -- ⚠️ La vista NON filtra: espone il flag e basta. Filtrano i tre conteggi
            -- economici (Bilancio commessa, /bilancio, ore per fase). La trasferta invece
            -- legge questa stessa vista e deve IGNORARLO: la persona in cantiere c'è stata,
            -- che le sue ore siano contabilizzate o no.
            CASE WHEN ew.entry_id IS NULL THEN 0 ELSE 1 END AS is_extra,
            COALESCE(ew.counts_in_project, 1) AS counts_in_project
        FROM timesheet_entries te
        JOIN project_phases pp        ON pp.id = te.project_phase_id
        LEFT JOIN phase_templates pt  ON pt.id = pp.phase_template_id
        JOIN employees emp            ON emp.id = te.employee_id
        LEFT JOIN timesheet_extra_work ew ON ew.entry_id = te.id
        LEFT JOIN (
            -- Reparto da cui si prende il COSTO ORARIO quando la persona ne ha più di uno.
            -- Fino alla v72 qui c'era MIN(department_id), cioè «il primo per numero»: una
            -- scelta arbitraria, e DIVERSA da quella della dashboard commessa, che usa
            -- `is_primary → is_responsible → id`. Due regole per lo stesso numero.
            -- Allineate alla regola della dashboard, che è l'unica con un significato: il
            -- reparto PRINCIPALE della persona.
            -- Fatto il 06/08/2026, quando in produzione le due regole divergevano su UNA sola
            -- persona (Vinardi: UTE per la vista, ACQ per la dashboard) e i due reparti
            -- costavano entrambi 45,00 €/h — quindi a costo zero. Appena le tariffe si
            -- differenziano, allinearle vorrà dire spostare numeri già visti.
            SELECT employee_id, department_id FROM (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) ranked WHERE rn = 1
        ) ed ON ed.employee_id = emp.id
        LEFT JOIN departments d  ON d.id = ed.department_id";

    /// <summary>
    /// ALTER ... ADD COLUMN condizionata. Restituisce true se la colonna è stata aggiunta ora.
    /// </summary>
    internal static bool AddColumnIfMissing(MySqlConnection c, string table, string column, string definition)
    {
        bool exists = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @Table AND column_name = @Column",
            new { Table = table, Column = column }) > 0;
        if (exists)
            return false;

        c.Execute($"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}", commandTimeout: 600);
        return true;
    }

    /// <summary>
    /// CREATE INDEX condizionata. Restituisce true se l'indice è stato creato ora.
    /// <para>Il nome dell'indice è la chiave: MySQL lascia creare due indici identici con nomi
    /// diversi, e ci si accorge solo dalle scritture che rallentano.</para>
    /// </summary>
    internal static bool CreaIndiceSeManca(MySqlConnection c, string table, string index, string columns)
    {
        if (EsisteIndice(c, table, index))
            return false;

        // commandTimeout lungo: su una tabella grande la creazione dell'indice dura, e il default
        // di 30 s del driver la interromperebbe a metà facendo fallire la migrazione (quindi,
        // con StopOnError, non far partire il server) per un motivo che non è un errore.
        c.Execute($"CREATE INDEX `{index}` ON `{table}` ({columns})", commandTimeout: 600);
        return true;
    }

    /// <summary>
    /// DROP INDEX condizionata (MySQL non ha <c>DROP INDEX IF EXISTS</c>). Serve a togliere gli
    /// indici resi ridondanti da uno nuovo: un indice il cui elenco di colonne è il prefisso di un
    /// altro non fa guadagnare niente in lettura e si paga a ogni scrittura.
    /// <para>🪤 Da chiamare <b>dopo</b> aver creato il sostituto: se la colonna ha una chiave
    /// esterna, MySQL rifiuta di lasciarla senza alcun indice utilizzabile.</para>
    /// </summary>
    internal static bool EliminaIndiceSePresente(MySqlConnection c, string table, string index)
    {
        if (!EsisteIndice(c, table, index))
            return false;

        c.Execute($"DROP INDEX `{index}` ON `{table}`", commandTimeout: 600);
        return true;
    }

    private static bool EsisteIndice(MySqlConnection c, string table, string index) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = @Table AND index_name = @Index",
            new { Table = table, Index = index }) > 0;

    /// <summary>
    /// Tabella del modulo Lavorazioni meccaniche (project_work_requests) — idempotente.
    /// Chiamata sia dal percorso dev (InitDatabase) sia dalla migrazione v20 (produzione).
    /// Le offerte RDO sono denormalizzate in JSON nella colonna `rfqs` (max 4 per riga);
    /// i timestamp delivered_at/treatment_confirmed_at/created_at sono Unix millis (BIGINT).
    /// `row_version` è il concurrency token (pattern MoM/Check list): la PUT lo confronta
    /// e ogni scrittura lo incrementa.
    /// </summary>
    internal static void EnsureWorkRequestsTable(MySqlConnection c)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_work_requests (
            id INT AUTO_INCREMENT PRIMARY KEY,
            -- Facoltativa dalla v92: una riga manuale di Lavorazioni Officine può non
            -- appartenere a nessuna commessa né Altra Attività (#83).
            project_id INT NULL,
            request_date DATE NULL,
            description TEXT NULL,
            -- Campi che sulle righe da DDP arrivano dalla distinta: sulle manuali si scrivono.
            part_number VARCHAR(100) NOT NULL DEFAULT '',
            quantity DECIMAL(10,3) NOT NULL DEFAULT 0,
            quantity_produced DECIMAL(10,3) NOT NULL DEFAULT 0,
            material VARCHAR(200) NOT NULL DEFAULT '',
            treatment VARCHAR(200) NOT NULL DEFAULT '',
            destination VARCHAR(200) NOT NULL DEFAULT '',
            destination_spec VARCHAR(200) NOT NULL DEFAULT '',
            type VARCHAR(20) NOT NULL DEFAULT '',
            priority TINYINT NOT NULL DEFAULT 2,
            availability_date DATE NULL,
            notes TEXT NULL,
            is_ultra_critical TINYINT(1) NOT NULL DEFAULT 0,
            is_delivered TINYINT(1) NOT NULL DEFAULT 0,
            delivered_at BIGINT NULL,
            is_staging TINYINT(1) NOT NULL DEFAULT 0,
            rfqs TEXT NULL,
            po_supplier VARCHAR(200) NOT NULL DEFAULT '',
            po_number VARCHAR(100) NOT NULL DEFAULT '',
            po_date DATE NULL,
            has_treatment TINYINT(1) NOT NULL DEFAULT 0,
            treatment_date DATE NULL,
            treatment_notes TEXT NULL,
            is_treatment_confirmed TINYINT(1) NOT NULL DEFAULT 0,
            treatment_confirmed_at BIGINT NULL,
            row_version INT NOT NULL DEFAULT 0,
            created_at BIGINT NOT NULL DEFAULT 0,
            ddp_officina_item_id INT NULL,
            CONSTRAINT fk_pwr_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            CONSTRAINT fk_pwr_ddp_officina FOREIGN KEY (ddp_officina_item_id)
                REFERENCES ddp_officina_items(id) ON DELETE SET NULL,
            UNIQUE KEY uq_pwr_ddp_officina (ddp_officina_item_id),
            KEY idx_pwr_project (project_id),
            KEY idx_pwr_staging (is_staging),
            KEY idx_pwr_delivered (is_delivered)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Installazioni dove la tabella è nata senza row_version (v20 già applicata): ALTER idempotente.
        int hasRowVersion = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'project_work_requests' AND COLUMN_NAME = 'row_version'");
        if (hasRowVersion == 0)
            c.Execute("ALTER TABLE project_work_requests ADD COLUMN row_version INT NOT NULL DEFAULT 0 AFTER treatment_confirmed_at");
    }

    /// <summary>
    /// Scrive le <paramref name="chiavi"/> su ogni persona che oggi arriva almeno al
    /// <paramref name="livello"/> che quelle chiavi sostituiscono. È il modo in cui la Fase E
    /// resta una sostituzione e non un giro di vite: <c>[RequireLevel(n)]</c> guardava solo il
    /// livello del ruolo, quindi «tutti quelli con livello ≥ n» è esattamente la platea di prima.
    ///
    /// <para>Tre scelte da non cambiare per sbaglio:</para>
    /// <list type="bullet">
    /// <item><c>INSERT IGNORE</c>: una riga già presente non si tocca mai. Se qualcuno ha messo
    /// a mano (<c>origin = MANO</c>) una di queste funzioni, la sua decisione vince.</item>
    /// <item>Chi ha il jolly <c>*</c> viene saltato: passa già da lì, e riempirlo di righe
    /// esplicite renderebbe la sua scheda illeggibile proprio a chi amministra i permessi.</item>
    /// <item>Si guardano tutti i dipendenti con un'utenza, non solo gli <c>ACTIVE</c>: un cessato
    /// riattivato domani deve ritrovare i permessi della sua classe, non una scheda vuota.</item>
    /// </list>
    /// </summary>
    internal static int SeminaChiaviPerLivello(MySqlConnection c, int livello, string[] chiavi) =>
        c.Execute(@"
            INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT e.id, f.feature_key, 'FULL', 'CLASSE'
            FROM employees e
            JOIN auth_features f ON f.feature_key IN @Chiavi
            WHERE COALESCE(e.username, '') <> ''
              AND COALESCE((SELECT MAX(l.level_value) FROM auth_levels l
                            WHERE UPPER(l.role_name) = UPPER(COALESCE(e.user_role, ''))), 0) >= @Livello
              AND NOT EXISTS (SELECT 1 FROM employee_feature_access j
                              WHERE j.employee_id = e.id AND j.feature_key = '*')",
            new { Chiavi = chiavi, Livello = livello });

    // ══════════════════════════════════════════════════════════════
    // v68 — CONVERSIONE DEI 7 CAMPI TRASFERTA DEL PREVENTIVO IN RIGHE
    // ══════════════════════════════════════════════════════════════

    /// <summary>I 7 campi trasferta di una risorsa pianificata, con la sua sezione e commessa.</summary>
    private sealed class LegacyTravelResource
    {
        public int ResourceId { get; set; }
        public int SectionId { get; set; }
        public int ProjectId { get; set; }
        public string ResourceName { get; set; } = "";
        public decimal WorkDays { get; set; }
        public int SortOrder { get; set; }
        public int NumTrips { get; set; }
        public decimal KmPerTrip { get; set; }
        public decimal CostPerKm { get; set; }
        public decimal DailyFood { get; set; }
        public decimal DailyHotel { get; set; }
        public int AllowanceDays { get; set; }
        public decimal DailyAllowance { get; set; }
    }

    /// <summary>
    /// Converte i dati già inseriti a mano nella trasferta del preventivo (i 7 campi di
    /// <c>project_cost_resources</c>) in righe di <c>project_cost_travel_rows</c>: UNA riga per
    /// risorsa, con lo stesso totale al centesimo. Solo le sezioni con Tag Cliente
    /// (<c>section_type = 'DA_CLIENTE'</c>), che sono le uniche dove quei campi si vedevano.
    ///
    /// Idempotente: se la riga della risorsa esiste già non ne crea una seconda. Serve perché
    /// la migrazione è «non bloccante» (se va in errore a metà, al riavvio riparte da capo).
    /// Tutto dentro UNA transazione, così un errore non può lasciare righe a metà: riga e
    /// dettaglio delle calcolatrici o ci sono entrambi o non c'è nessuno dei due.
    ///
    /// Scrive anche il DETTAGLIO delle tre calcolatrici, e non solo i totali: senza dettaglio,
    /// il primo che apre la finestra e conferma a vuoto azzererebbe il valore appena migrato
    /// (foglio vuoto → colonna a NULL, TravelController.SaveRowCalc).
    ///
    /// Restituisce (righe create, fogli scritti, righe con le notti arrotondate).
    /// </summary>
    internal static (int Converted, int Sheets, int Rounded) ConvertLegacyTravelFields(MySqlConnection c)
    {
        List<LegacyTravelResource> legacy = c.Query<LegacyTravelResource>(@"
            SELECT r.id AS ResourceId, r.section_id AS SectionId, s.project_id AS ProjectId,
                   r.resource_name AS ResourceName, r.work_days AS WorkDays, r.sort_order AS SortOrder,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.section_type = 'DA_CLIENTE'
              -- almeno un costo di trasferta diverso da zero: una risorsa senza trasferta
              -- non deve ritrovarsi una riga vuota da cancellare a mano
              AND (r.work_days * r.daily_hotel <> 0
                   OR r.work_days * r.daily_food <> 0
                   OR r.allowance_days * r.daily_allowance <> 0
                   OR r.num_trips * r.km_per_trip * r.cost_per_km <> 0)
              AND NOT EXISTS (
                  SELECT 1 FROM project_cost_travel_rows t
                  WHERE t.section_id = r.section_id AND t.resource_id = r.id)
            ORDER BY r.section_id, r.sort_order, r.id").ToList();

        int converted = 0, sheets = 0, rounded = 0;
        if (legacy.Count == 0) return (0, 0, 0);

        using MySqlTransaction tx = c.BeginTransaction();

        foreach (LegacyTravelResource r in legacy)
        {
            // Il vecchio AccommodationCost (giorni × (vitto + hotel)) non ha il concetto di
            // notte: la conversione usa notti = giorni lavorativi. Approssimazione DICHIARATA
            // (§5 del piano): con giorni frazionari (work_days è DECIMAL(8,1)) l'arrotondamento
            // sposta il costo alloggio di mezza notte, e l'utente corregge le notti a mano.
            bool hasLodging = r.DailyHotel > 0 && r.WorkDays > 0;
            int? nights = hasLodging ? (int)Math.Round(r.WorkDays, MidpointRounding.AwayFromZero) : null;
            decimal? nightPrice = hasLodging ? r.DailyHotel : null;
            if (hasLodging && nights != r.WorkDays) rounded++;

            decimal meal = Math.Round(r.WorkDays * r.DailyFood, 2);
            decimal allowance = Math.Round(r.AllowanceDays * r.DailyAllowance, 2);
            decimal car = Math.Round(r.NumTrips * r.KmPerTrip * r.CostPerKm, 2);

            int rowId = c.ExecuteScalar<int>(@"
                INSERT INTO project_cost_travel_rows
                    (section_id, resource_id, person_name, nights, night_price,
                     meal_cost, allowance_cost, car_cost, transport_cost, sort_order)
                VALUES (@Sid, @Rid, @Name, @Nights, @NightPrice,
                        @Meal, @Allowance, @Car, NULL, @Sort);
                SELECT LAST_INSERT_ID()",
                new
                {
                    Sid = r.SectionId,
                    Rid = r.ResourceId,
                    Name = r.ResourceName,
                    Nights = nights,
                    NightPrice = nightPrice,
                    // «—» ≠ 0,00 €: la colonna resta NULL se quel costo non c'era.
                    Meal = meal == 0 ? (decimal?)null : meal,
                    Allowance = allowance == 0 ? (decimal?)null : allowance,
                    Car = car == 0 ? (decimal?)null : car,
                    Sort = r.SortOrder,
                }, tx);
            converted++;

            // Il dettaglio ha la stessa forma delle tre calcolatrici: Giorni × Diaria,
            // Giorni × Indennità, Km × Rimborso × Numero Tratte.
            if (meal != 0)
            {
                InsertMigratedCalcRow(c, tx, r.ProjectId,
                    ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.SheetKey(
                        ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.Meal, rowId),
                    "Vitto (dai campi del preventivo)", r.WorkDays, r.DailyFood, null, meal);
                sheets++;
            }
            if (allowance != 0)
            {
                InsertMigratedCalcRow(c, tx, r.ProjectId,
                    ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.SheetKey(
                        ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.Allowance, rowId),
                    "Indennità (dai campi del preventivo)", r.AllowanceDays, r.DailyAllowance, null, allowance);
                sheets++;
            }
            if (car != 0)
            {
                InsertMigratedCalcRow(c, tx, r.ProjectId,
                    ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.SheetKey(
                        ATEC.PM.Shared.DTOs.ProjectCostTravelCalcKinds.Car, rowId),
                    "Auto (dai campi del preventivo)", r.KmPerTrip, r.CostPerKm, r.NumTrips, car);
                sheets++;
            }
        }

        tx.Commit();
        return (converted, sheets, rounded);
    }

    /// <summary>
    /// Foglio di calcolo con una riga sola, generato dalla conversione. <c>linked_source</c>
    /// = 'migrazione:v68' per poter riconoscere (e all'occorrenza rifare) quello che ha scritto
    /// la migrazione; markup 1,000 perché il K non entra nella finestra di calcolo (si applica
    /// dopo, sul totale). <c>amount</c> è già calcolato: le letture successive lo ricalcolano
    /// comunque da quantità × costo unitario × moltiplicatore.
    /// </summary>
    private static void InsertMigratedCalcRow(
        MySqlConnection c, MySqlTransaction tx, int projectId, string calcKey, string description,
        decimal quantity, decimal unitCost, decimal? multiplier, decimal amount)
    {
        int sheetId = c.ExecuteScalar<int>(@"
            INSERT INTO project_calc_sheets (project_id, calc_key, row_version)
            VALUES (@Pid, @Key, 1);
            SELECT LAST_INSERT_ID()",
            new { Pid = projectId, Key = calcKey }, tx);

        c.Execute(@"
            INSERT INTO project_calc_rows
                (sheet_id, group_key, description, quantity, unit_cost, multiplier, amount,
                 amount_pinned, markup_value, linked_source, sort_order)
            VALUES (@Sid, '', @Desc, @Qty, @Unit, @Mult, @Amount, 0, 1.000, 'migrazione:v68', 1)",
            new
            {
                Sid = sheetId,
                Desc = description,
                Qty = quantity,
                Unit = unitCost,
                Mult = multiplier,
                Amount = amount,
            }, tx);
    }
}
