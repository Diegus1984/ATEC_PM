using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Fase D della #63 — i pacchetti definitivi delle classi Tecnico e Responsabile, e le eccezioni
/// vere messe al sicuro.
///
/// <para>⚠️ <b>Questa migrazione NON tocca i permessi di nessuno</b>: riscrive i PACCHETTI delle
/// classi e marca le eccezioni. Il cambio arriva alle persone solo quando qualcuno preme
/// «Applica classe» e conferma l'anteprima — che è il gesto che il piano vuole (§4.4: «si conferma
/// quello, non Applica»). Una migrazione che riscrive in silenzio i permessi di 34 persone sarebbe
/// esattamente la cosa che quel piano esiste per evitare.</para>
/// </summary>
public sealed class M087_ClassiDefinitive : IMigrazione
{
    public int Versione => 87;

    public string Descrizione =>
        "Fase D: classi definitive (Tecnico e Responsabile senza Dashboard, MoM/Milestone/DDP in lettura) " +
        "+ eccezioni Contabilita e Acquisti marcate MANO";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. Le eccezioni vere diventano MANO, PRIMA di toccare i pacchetti ────────
        //
        // Sono decisioni di qualcuno, non conseguenze di una classe, e «Applica classe»
        // non deve poterle cancellare. Vanno marcate ADESSO: dopo la riscrittura dei
        // pacchetti sarebbero indistinguibili da righe di classe rimaste indietro.

        // Contabilità: vede SAL, dati economici, Clienti e Segnalazioni — non le commesse,
        // non il Timesheet, non la Chat. Oggi ce l'ha per via dell'elenco cablato del
        // vecchio motore, con origin CLASSE: senza questo passaggio la prima applicazione
        // gli ridarebbe mezzo gestionale.
        int contabilita = c.Execute(@"
            UPDATE employee_feature_access a
            JOIN employee_departments ed ON ed.employee_id = a.employee_id
            JOIN departments d ON d.id = ed.department_id
            SET a.origin = 'MANO'
            WHERE (d.name LIKE '%Contabil%' OR d.id = 9) AND a.origin <> 'MANO'");

        // 🪤 Marcare le righe che HA non basta: la sua è una lista ESCLUSIVA — «vede
        // queste quattro cose e basta» — e tutto il resto è oggi un'ASSENZA di riga, che
        // «Applica classe» riempirebbe col pacchetto del Tecnico. Provato a runtime: la
        // Contabilità si ritrovava Commesse e Timesheet, cioè esattamente il mezzo
        // gestionale che il 04/08 le era stato tolto.
        // Quindi le assenze diventano DINIEGHI espliciti: la lista resta chiusa anche
        // dopo un timbro di massa.
        contabilita += c.Execute(@"
            INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT DISTINCT e.id, f.feature_key, 'NO', 'MANO'
            FROM employees e
            JOIN employee_departments ed ON ed.employee_id = e.id
            JOIN departments d ON d.id = ed.department_id
            CROSS JOIN auth_features f
            WHERE (d.name LIKE '%Contabil%' OR d.id = 9)
              AND COALESCE(e.username,'') <> ''
              AND NOT EXISTS (SELECT 1 FROM employee_feature_access x
                              WHERE x.employee_id = e.id AND x.feature_key = f.feature_key)");

        // Ufficio Acquisti: il Timesheet è spento. Oggi è l'ASSENZA di una riga, che
        // nessuna applicazione di classe rispetterebbe: diventa un DINIEGO esplicito.
        int acquisti = c.Execute(@"
            INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
            SELECT DISTINCT e.id, 'nav.timesheet', 'NO', 'MANO'
            FROM employees e
            JOIN employee_departments ed ON ed.employee_id = e.id
            JOIN departments d ON d.id = ed.department_id
            WHERE d.name LIKE '%cquisti%' AND COALESCE(e.username,'') <> ''
            ON DUPLICATE KEY UPDATE access = 'NO', origin = 'MANO'");

        // ── 2. I pacchetti definitivi ───────────────────────────────────────────────
        //
        // Cambiano solo Tecnico e Responsabile. PM resta il set di oggi, Admin resta il
        // jolly. Il taglio è quello dell'Excel di Paolo, letto colonna per colonna.
        c.Execute("DELETE FROM auth_class_features WHERE class_name IN ('TECH','RESP_REPARTO')");

        // Tecnico: lavora sulle commesse e carica le ore; guarda Risorse, Verbali,
        // Milestone e DDP senza toccarli. Niente Dashboard (richiesta esplicita: «solo il
        // menu principale») e niente Lavorazioni.
        //   MoM e Milestone in lettura sono l'unico punto della #63 che ALLARGA: 27
        //   persone in più vedono verbali e scadenze. Va detto a Paolo.
        c.Execute(@"INSERT INTO auth_class_features (class_name, feature_key, access) VALUES
            ('TECH', 'nav.commesse',             'FULL'),
            ('TECH', 'nav.timesheet',            'FULL'),
            ('TECH', 'project.chat',             'FULL'),
            ('TECH', 'project.documenti',        'FULL'),
            ('TECH', 'nav.bug_reports',          'FULL'),
            ('TECH', 'nav.risorse',              'READ'),
            ('TECH', 'nav.mom',                  'READ'),
            ('TECH', 'nav.milestones',           'READ'),
            ('TECH', 'project.ddp_commerciale',  'READ'),
            ('TECH', 'project.ddp_officina',     'READ')");

        // Responsabile: come il Tecnico sulle 9 aree, ma le DDP le scrive.
        //   Gli attrezzi del mestiere (Catalogo, Codex, Inbox, Clienti, Fornitori,
        //   ricodifica, Codice ATEC, ore per altri, planner) RESTANO: non sono nell'Excel
        //   di Paolo, che parla solo delle 9 aree, e toglierli spegnerebbe il lavoro
        //   quotidiano dei responsabili di Acquisti e Officina.
        c.Execute(@"INSERT INTO auth_class_features (class_name, feature_key, access)
            SELECT 'RESP_REPARTO', feature_key, access FROM auth_class_features WHERE class_name='TECH'");
        c.Execute(@"UPDATE auth_class_features SET access='FULL'
            WHERE class_name='RESP_REPARTO'
              AND feature_key IN ('project.ddp_commerciale','project.ddp_officina')");
        c.Execute(@"INSERT IGNORE INTO auth_class_features (class_name, feature_key, access) VALUES
            ('RESP_REPARTO', 'nav.catalogo',                'FULL'),
            ('RESP_REPARTO', 'nav.codex',                   'FULL'),
            ('RESP_REPARTO', 'nav.codex_composizione',      'FULL'),
            ('RESP_REPARTO', 'nav.clienti',                 'FULL'),
            ('RESP_REPARTO', 'nav.fornitori',               'FULL'),
            ('RESP_REPARTO', 'nav.acquisti_inbox',          'FULL'),
            ('RESP_REPARTO', 'nav.officina_inbox',          'FULL'),
            ('RESP_REPARTO', 'nav.danea_migration',         'FULL'),
            ('RESP_REPARTO', 'resources.edit',              'FULL'),
            ('RESP_REPARTO', 'action.recode_codex',         'FULL'),
            ('RESP_REPARTO', 'action.assign_atec_code',     'FULL'),
            ('RESP_REPARTO', 'action.timesheet_for_others', 'FULL')");
        // Il Responsabile vede Risorse per intero: ha `resources.edit`, quindi la sua
        // combo Risorse non deve restare a «lettura» come quella del Tecnico.
        c.Execute("UPDATE auth_class_features SET access='FULL' WHERE class_name='RESP_REPARTO' AND feature_key='nav.risorse'");

        log.LogInformation(
            "[Migration v87] Fase D: pacchetti di classe riscritti. Eccezioni messe al sicuro: {Contabilita} righe Contabilità, {Acquisti} dinieghi Timesheet in Acquisti. " +
            "⚠️ Nessun permesso è ancora cambiato: il taglio arriva quando un amministratore applica le classi dalla pagina «Permessi», confermando l'anteprima.",
            contabilita, acquisti);
    }
}
