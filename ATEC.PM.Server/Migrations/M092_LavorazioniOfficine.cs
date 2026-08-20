using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #83 — «Lavorazioni Officine»: la pagina smette di essere un elenco di righe
/// copiate dalla DDP e diventa una <b>vista</b> della DDP Officina. Le righe non si duplicano
/// più: si guardano dove già vivono, filtrate per Tipo (Interna/Esterna) e Stato.
///
/// <para>Perché servono colonne nuove sulla riga officina: la vista è di sola lettura tranne
/// tre campi, che la segnalazione vuole scrivibili <b>solo</b> da Lavorazioni —
/// «Data Richiesta» (<c>date_needed</c>, esisteva già), le <b>Note</b> di gestione lavorazione
/// e la spunta <b>ultra critica</b> che alimenta la vista Urgenze. Le ultime due stavano su
/// <c>project_work_requests</c>, cioè sulla copia che stiamo togliendo di mezzo: qui vengono
/// portate sulla riga vera, col loro contenuto.</para>
///
/// <para>⚠️ <b>Le Note non sono le note della DDP.</b> <c>ddp_officina_items.notes</c> è il campo
/// che l'ufficio tecnico compila nella distinta e che la segnalazione vuole esplicitamente
/// fuori («Nessun riporto di note dal campo specifico presente in DDP»). Perciò una colonna
/// separata, <c>workshop_notes</c>, e non un riuso.</para>
///
/// <para><b>Il backfill di <c>work_type</c> è la parte che conta.</b> Le due viste principali
/// filtrano per Tipo, e il Tipo veniva congelato sulla riga solo dalla v63 in avanti
/// (<c>FreezeOfficinaWorkType</c>): tutto il pregresso ce l'ha vuoto. Senza questo backfill
/// la pagina nuova si aprirebbe mezza vuota — con le righe ancora da costruire invisibili a
/// chi le deve costruire. Si ricostruisce dallo stato attuale e, per gli stati che non dicono
/// la natura del lavoro (PAR, MIT), dalla cronistoria: l'ultimo stato attraversato che la
/// dice. Solo in mancanza di tutto si guarda il fornitore.</para>
/// </summary>
public sealed class M092_LavorazioniOfficine : IMigrazione
{
    public int Versione => 92;

    public string Descrizione =>
        "#83 Lavorazioni Officine: note+urgenza sulla riga officina, backfill Tipo, righe manuali";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. I due campi che la vista scrive sulla riga officina ────────────────
        if (AiutiMigrazione.AddColumnIfMissing(c, "ddp_officina_items", "workshop_notes",
                "TEXT NULL AFTER notes"))
            log.LogInformation("[v92] ddp_officina_items.workshop_notes aggiunta.");

        if (AiutiMigrazione.AddColumnIfMissing(c, "ddp_officina_items", "is_ultra_critical",
                "TINYINT(1) NOT NULL DEFAULT 0 AFTER work_type"))
            log.LogInformation("[v92] ddp_officina_items.is_ultra_critical aggiunta.");

        // Contenuto scritto a mano nel vecchio pannello: si trasferisce, non si butta.
        // Solo dove la riga officina è ancora vergine, così una ri-esecuzione non
        // sovrascrive quello che nel frattempo è stato scritto nella pagina nuova.
        int noteTravasate = c.Execute(@"
            UPDATE ddp_officina_items o
            JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            SET o.workshop_notes = wr.notes
            WHERE TRIM(COALESCE(o.workshop_notes, '')) = ''
              AND TRIM(COALESCE(wr.notes, '')) <> ''");
        log.LogInformation("[v92] Note lavorazione travasate dal vecchio pannello: {N}.", noteTravasate);

        int urgenzeTravasate = c.Execute(@"
            UPDATE ddp_officina_items o
            JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            SET o.is_ultra_critical = 1
            WHERE o.is_ultra_critical = 0 AND wr.is_ultra_critical = 1");
        log.LogInformation("[v92] Segnalazioni ultra critiche travasate: {N}.", urgenzeTravasate);

        // ── 2. Backfill del Tipo, senza il quale la pagina nuova nasce mezza cieca ──
        // (a) Lo stato attuale, quando è uno di quelli che dicono la natura del lavoro.
        int daStato = c.Execute(@"
            UPDATE ddp_officina_items
            SET work_type = CASE UPPER(TRIM(COALESCE(item_status, '')))
                WHEN 'DC'  THEN 'Internal'
                WHEN 'COS' THEN 'Internal'
                WHEN 'DO'  THEN 'External'
                WHEN 'RO'  THEN 'External'
                WHEN 'IO'  THEN 'External'
                WHEN 'CON' THEN 'External'
                ELSE ''
            END
            WHERE TRIM(COALESCE(work_type, '')) = ''
              AND UPPER(TRIM(COALESCE(item_status, ''))) IN ('DC','COS','DO','RO','IO','CON')");
        log.LogInformation("[v92] Tipo dedotto dallo stato: {N} righe.", daStato);

        // (b) PAR / MIT / DISP e simili non dicono se il pezzo si costruisce o si compra:
        // lo dice l'ultimo stato attraversato che lo diceva. La cronistoria esiste dalla v55.
        // 🪤 Si guardano SIA lo stato di arrivo SIA quello di partenza di ogni passaggio.
        // Guardare solo `to_status` sembra ovvio e sbaglia il caso più comune: la riga passata
        // da «Da costruire» a «Materiale in trattamento» ha un solo evento, DC → MIT, e
        // l'informazione buona («si costruisce in casa») sta nella colonna da cui esce, non in
        // quella in cui entra. Sono proprio le righe della vista Trattamenti.
        //
        // L'ultimo stato utile per riga si prende con GROUP_CONCAT ordinato + il primo pezzo:
        // è il modo che MySQL 8.0 accetta dentro una UPDATE senza funzioni finestra. Il limite
        // di lunghezza del GROUP_CONCAT non dà fastidio: di quella lista serve solo il primo.
        int daCronistoria = c.Execute(@"
            UPDATE ddp_officina_items o
            JOIN (
                SELECT x.item_id AS ItemId,
                       SUBSTRING_INDEX(
                           GROUP_CONCAT(x.stato ORDER BY x.changed_at DESC, x.id DESC, x.peso DESC),
                           ',', 1) AS UltimoStato
                FROM (
                    SELECT ev.item_id, UPPER(TRIM(ev.to_status)) AS stato,
                           ev.changed_at, ev.id, 2 AS peso
                    FROM ddp_item_events ev
                    WHERE ev.item_type = 'OFFICINA'
                      AND UPPER(TRIM(COALESCE(ev.to_status, ''))) IN ('DC','COS','DO','RO','IO','CON')
                    UNION ALL
                    SELECT ev.item_id, UPPER(TRIM(ev.from_status)) AS stato,
                           ev.changed_at, ev.id, 1 AS peso
                    FROM ddp_item_events ev
                    WHERE ev.item_type = 'OFFICINA'
                      AND UPPER(TRIM(COALESCE(ev.from_status, ''))) IN ('DC','COS','DO','RO','IO','CON')
                ) x
                GROUP BY x.item_id
            ) storia ON storia.ItemId = o.id
            SET o.work_type = CASE WHEN storia.UltimoStato IN ('DC','COS') THEN 'Internal' ELSE 'External' END
            WHERE TRIM(COALESCE(o.work_type, '')) = ''");
        log.LogInformation("[v92] Tipo dedotto dalla cronistoria: {N} righe.", daCronistoria);

        // (c) Ultima spiaggia: un fornitore scritto sulla riga vuol dire che il pezzo si compra.
        // Le righe che restano senza Tipo non spariscono — le raccoglie la vista Trattamenti se
        // hanno un trattamento, e restano sempre visibili nella DDP della loro commessa.
        int daFornitore = c.Execute(@"
            UPDATE ddp_officina_items
            SET work_type = 'External'
            WHERE TRIM(COALESCE(work_type, '')) = ''
              AND TRIM(COALESCE(supplier_name, '')) <> ''");
        log.LogInformation("[v92] Tipo dedotto dal fornitore: {N} righe.", daFornitore);

        int senzaTipo = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM ddp_officina_items
            WHERE TRIM(COALESCE(work_type, '')) = ''
              AND UPPER(TRIM(COALESCE(item_status, ''))) IN ('DC','DO','PAR','MIT')");
        if (senzaTipo > 0)
            log.LogWarning("[v92] {N} righe officina ancora aperte restano senza Tipo: " +
                "si assegna a mano dalla colonna Tipo della DDP.", senzaTipo);

        // ── 3. Righe manuali: restano in project_work_requests, che da qui in poi tiene
        // SOLO quelle. La segnalazione le vuole «non collegate a una DDP di una specifica
        // commessa o Altra Attività»: quindi la commessa diventa facoltativa, e servono i
        // campi che prima arrivavano dalla riga officina.
        int projectIdObbligatorio = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'project_work_requests'
              AND COLUMN_NAME = 'project_id' AND IS_NULLABLE = 'NO'");
        if (projectIdObbligatorio > 0)
        {
            // La FK non va toccata: ON DELETE CASCADE resta giusta per le righe che una
            // commessa ce l'hanno, e una FK su colonna NULL semplicemente non vincola.
            c.Execute("ALTER TABLE project_work_requests MODIFY COLUMN project_id INT NULL");
            log.LogInformation("[v92] project_work_requests.project_id ora facoltativo (righe manuali).");
        }

        foreach ((string colonna, string tipo) in new[]
        {
            ("part_number",       "VARCHAR(100) NOT NULL DEFAULT '' AFTER description"),
            ("quantity",          "DECIMAL(10,3) NOT NULL DEFAULT 0 AFTER part_number"),
            ("quantity_produced", "DECIMAL(10,3) NOT NULL DEFAULT 0 AFTER quantity"),
            ("material",          "VARCHAR(200) NOT NULL DEFAULT '' AFTER quantity_produced"),
            ("treatment",         "VARCHAR(200) NOT NULL DEFAULT '' AFTER material"),
            ("destination",       "VARCHAR(200) NOT NULL DEFAULT '' AFTER treatment"),
            ("destination_spec",  "VARCHAR(200) NOT NULL DEFAULT '' AFTER destination"),
        })
        {
            if (AiutiMigrazione.AddColumnIfMissing(c, "project_work_requests", colonna, tipo))
                log.LogInformation("[v92] project_work_requests.{Colonna} aggiunta.", colonna);
        }

        // ── 4. Chi lavorava nella Inbox Officina non deve restare fuori ──────────
        // La pagina Inbox Officina sparisce: quello che faceva lì si fa in Lavorazioni
        // Officine. Senza questo travaso il responsabile officina si troverebbe il menu
        // vuoto al primo accesso dopo l'aggiornamento — e i permessi, dalla v87, non hanno
        // più un livello che rimedi da sé.
        //
        // La chiave `nav.officina_inbox` NON viene ritirata: protegge anche le scritture
        // sulle righe di distinta officina (cinque [RequireFeature] in ProjectsController),
        // e toglierla spegnerebbe il lavoro vero lasciando accesa solo la pagina. Cambia
        // il nome che si legge nella scheda dei permessi, perché la Inbox non c'è più.
        int personeTravasate = c.Execute(@"
            INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT employee_id, 'nav.work_requests', access, 'CLASSE'
            FROM employee_feature_access
            WHERE feature_key = 'nav.officina_inbox'");
        log.LogInformation("[v92] Persone che passano da Inbox Officina a Lavorazioni Officine: {N}.",
            personeTravasate);

        int classiTravasate = c.Execute(@"
            INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
            SELECT class_name, 'nav.work_requests', access
            FROM auth_class_features
            WHERE feature_key = 'nav.officina_inbox'");
        log.LogInformation("[v92] Pacchetti di classe aggiornati: {N}.", classiTravasate);

        c.Execute(@"UPDATE auth_features
            SET display_name = 'Officina — righe di distinta'
            WHERE feature_key = 'nav.officina_inbox'");
        c.Execute(@"UPDATE auth_features
            SET display_name = 'Lavorazioni Officine'
            WHERE feature_key = 'nav.work_requests'");

        // ── 5. Le bozze non esistono più ─────────────────────────────────────────
        // Erano copie automatiche di righe DDP in attesa di essere «promosse»: la vista nuova
        // legge la riga vera, quindi la copia non ha più senso. Si cancellano DOPO i travasi
        // qui sopra, che è l'unica roba scritta a mano che potevano contenere.
        int bozze = c.Execute("DELETE FROM project_work_requests WHERE is_staging = 1");
        log.LogInformation("[v92] Bozze in staging eliminate: {N}.", bozze);
    }
}
