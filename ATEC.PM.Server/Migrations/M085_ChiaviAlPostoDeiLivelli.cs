using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v85 — LA SCALA DI LIVELLO SPARISCE PER SOSTITUZIONE (PIANO-PERMESSI.md, Fase E).
//
// Fin qui i permessi si decidevano in DUE modi insieme: le chiavi sulla persona (Fase A) e
// una scala di livello — 51 `[RequireLevel]` sul server, 24 controlli sul client — che
// apriva e chiudeva pulsanti senza passare da nessuna chiave. Girare una manopola nella
// pagina «Permessi» nascondeva la voce di menu ma non chiudeva l'API, e rimettere una
// persona su una classe più bassa le spegneva cose che nessuna riga di permesso poteva
// ridarle. Da qui in avanti c'è un solo modo: la chiave sulla persona.
//
// ⚠️ Questa migrazione NON toglie niente a nessuno, ed è l'unica cosa che glielo impedisce.
// Col fallback invertito una chiave nuova nasce invisibile a CHIUNQUE: se il codice della
// Fase E arrivasse in produzione senza queste righe, ricodifica Codex, eliminazione
// commessa, soglia del Bilancio, import SAL e il resto sparirebbero tutti insieme, per
// tutti. Per questo ogni chiave nuova viene scritta sulle persone che oggi arrivano al
// livello che essa sostituisce: il perimetro si conserva seminando, non sperando.
// Elenco e motivazioni in FASE-E-SOSTITUZIONE-LIVELLI.md.
public sealed class M085_ChiaviAlPostoDeiLivelli : IMigrazione
{
    public int Versione => 85;

    public string Descrizione => "Fase E: 21 chiavi al posto della scala di livello, seminate a parità di accesso";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
            ('action.recode_codex',           'Ricodifica Codex',                  'action', 1, 'DISABLED'),
            ('action.assign_atec_code',       'Assegna Codice ATEC (Danea)',       'action', 1, 'DISABLED'),
            ('action.timesheet_for_others',   'Ore per altri (proprio reparto)',   'action', 1, 'DISABLED'),
            ('action.delete_ddp_row',         'Elimina definitivamente riga DDP',  'action', 2, 'DISABLED'),
            ('action.toggle_dashboard_folder','Cartelle in Dashboard',             'action', 2, 'DISABLED'),
            ('action.moderate_chat',          'Modera le chat di commessa',        'action', 2, 'DISABLED'),
            ('action.sync_project_phases',    'Allinea fasi dall''anagrafica',     'action', 2, 'DISABLED'),
            ('action.import_sal',             'Import SAL da backup',              'action', 2, 'DISABLED'),
            ('action.timesheet_any_employee', 'Ore per chiunque',                  'action', 2, 'DISABLED'),
            ('action.import_easyfatt',        'Import da Easyfatt',                'action', 2, 'DISABLED'),
            ('data.danea_explore',            'Esplora archivio Danea',            'data',   2, 'HIDDEN'),
            ('action.app_config',             'Configurazione di sistema',         'action', 3, 'DISABLED'),
            ('action.manage_codex',           'Gestione articoli Codex',           'action', 3, 'DISABLED'),
            ('action.edit_codex_composition', 'Modifica composizione Codex',       'action', 3, 'DISABLED'),
            ('action.edit_gamma_robot',       'Modifica distinta Gamma Robot',     'action', 3, 'DISABLED'),
            ('action.manage_bug_reports',     'Gestione segnalazioni',             'action', 3, 'DISABLED'),
            ('action.edit_dashboard_settings','Impostazioni Dashboard',            'action', 3, 'DISABLED'),
            ('action.edit_bilancio_settings', 'Soglia redditività Bilancio',       'action', 3, 'DISABLED'),
            ('action.import_project_phases',  'Importa fasi in commessa',          'action', 3, 'DISABLED'),
            ('action.sal_edit_closed',        'Modifica SAL di commessa chiusa',   'action', 3, 'DISABLED'),
            ('data.timesheet_all_phases',     'Timesheet su tutte le fasi',        'data',   3, 'HIDDEN')");

        // Catalogo e codice dicevano cose diverse: la chiave è registrata a livello 3, ma
        // il cancello vero (`[RequireLevel(2)]` sulla DELETE hard) è sempre stato 2 — cioè
        // i PM eliminano le commesse. Vince il codice, o la Fase E toglierebbe ai PM una
        // cosa che hanno tutti i giorni.
        c.Execute("UPDATE auth_features SET min_level = 2 WHERE feature_key = 'action.delete_project'");

        int righe = 0;
        righe += SeminaChiaviPerLivello(c, 1, new[]
        {
            "action.recode_codex", "action.assign_atec_code", "action.timesheet_for_others",
        });
        righe += SeminaChiaviPerLivello(c, 2, new[]
        {
            "action.delete_project", "action.delete_ddp_row", "action.toggle_dashboard_folder",
            "action.moderate_chat", "action.sync_project_phases", "action.import_sal",
            "action.timesheet_any_employee", "action.import_easyfatt", "data.danea_explore",
        });
        righe += SeminaChiaviPerLivello(c, 3, new[]
        {
            "action.app_config", "action.manage_codex", "action.edit_codex_composition",
            "action.edit_gamma_robot", "action.manage_bug_reports", "action.edit_dashboard_settings",
            "action.edit_bilancio_settings", "action.import_project_phases",
            "action.sal_edit_closed", "data.timesheet_all_phases",
        });

        log.LogInformation(
            "[Migration v85] Fase E: 21 funzioni registrate al posto dei controlli per livello, {Righe} concessioni scritte. Nessuno perde accesso.",
            righe);
    }
}
