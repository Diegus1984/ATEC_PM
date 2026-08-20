// GENERATO da scripts/genera-catalogo.mjs — NON MODIFICARE A MANO.
// Fonte unica: ATEC.PM.Shared/catalogo-permessi.json (PIANO-PERMESSI-REBUILD.md §12.2).
// Rigenerato in automatico da predev/prebuild; si committa (§12.8.8).

export type ChiaveCatalogo =
  | "action.app_config"
  | "action.assign_atec_code"
  | "action.create_project"
  | "action.delete_ddp_row"
  | "action.delete_project"
  | "action.edit_bilancio_settings"
  | "action.edit_codex_composition"
  | "action.edit_dashboard_settings"
  | "action.edit_gamma_robot"
  | "action.edit_project"
  | "action.import_easyfatt"
  | "action.import_project_phases"
  | "action.manage_bug_reports"
  | "action.manage_codex"
  | "action.moderate_chat"
  | "action.project_locked_write"
  | "action.recode_codex"
  | "action.sal_edit_closed"
  | "action.sync_project_phases"
  | "action.timesheet_any_employee"
  | "action.timesheet_for_others"
  | "action.toggle_dashboard_folder"
  | "data.budget"
  | "data.bug_reports_all"
  | "data.costs"
  | "data.danea_explore"
  | "data.hourly_cost"
  | "data.project_drafts"
  | "data.revenue"
  | "data.timesheet_all_phases"
  | "nav.acquisti_inbox"
  | "nav.anagrafica_attivita"
  | "nav.backup"
  | "nav.bilancio"
  | "nav.bug_reports"
  | "nav.cat_preventivi"
  | "nav.catalogo"
  | "nav.checklist"
  | "nav.clienti"
  | "nav.codex"
  | "nav.codex_composizione"
  | "nav.commesse"
  | "nav.config_sezioni"
  | "nav.danea_migration"
  | "nav.dashboard"
  | "nav.ddp_aggregazioni"
  | "nav.ddp_destinazioni"
  | "nav.digest_email"
  | "nav.fornitori"
  | "nav.gamma_robot"
  | "nav.gestore_ddp"
  | "nav.milestones"
  | "nav.mom"
  | "nav.officina_inbox"
  | "nav.ore_commessa"
  | "nav.permessi"
  | "nav.preventivi"
  | "nav.project_templates"
  | "nav.risorse"
  | "nav.sal"
  | "nav.sal_condizioni"
  | "nav.scadenze"
  | "nav.timesheet"
  | "nav.trasferta"
  | "nav.utenti"
  | "nav.work_requests"
  | "project.chat"
  | "project.checklist"
  | "project.ddp_commerciale"
  | "project.ddp_officina"
  | "project.dettagli"
  | "project.documenti"
  | "project.flusso_cassa"
  | "project.milestones"
  | "project.mom"
  | "project.sal"
  | "project.work_requests"
  | "resources.edit"
  | "sal.economics"

export type KindCatalogo = "sezione" | "voce" | "sezione-commessa" | "azione" | "ambito"

export interface VoceCatalogoGen {
  kind: KindCatalogo
  chiave: ChiaveCatalogo | null
  label: string
  micros?: readonly string[]
  soloClient?: boolean
  eredita?: ChiaveCatalogo
  ritirata?: boolean
  chiaveCondivisa?: boolean
  figli?: readonly VoceCatalogoGen[]
}

export const CATALOGO_PERMESSI: readonly VoceCatalogoGen[] = [
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Principale",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.dashboard",
        "label": "Dashboard",
        "soloClient": true,
        "figli": [
          {
            "kind": "azione",
            "chiave": "data.revenue",
            "label": "Vede il fatturato (globale)"
          },
          {
            "kind": "azione",
            "chiave": "action.edit_dashboard_settings",
            "label": "Modifica impostazioni Dashboard"
          },
          {
            "kind": "azione",
            "chiave": "action.toggle_dashboard_folder",
            "label": "Aggiunge/toglie cartelle Dashboard"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.commesse",
        "label": "Commesse",
        "soloClient": true,
        "figli": [
          {
            "kind": "sezione-commessa",
            "chiave": "project.dettagli",
            "label": "Dashboard Commessa",
            "soloClient": true
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.flusso_cassa",
            "label": "Flusso di Cassa",
            "soloClient": true
          },
          {
            "kind": "sezione-commessa",
            "chiave": null,
            "label": "Preventivo vs Consuntivo"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.chat",
            "label": "Chat",
            "chiaveCondivisa": true
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.mom",
            "label": "Verbali (MoM)"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.checklist",
            "label": "Check list"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.milestones",
            "label": "Milestone"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.sal",
            "label": "SAL / Fatturazione"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.ddp_commerciale",
            "label": "DDP Commerciali",
            "micros": [
              "prices"
            ]
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.ddp_officina",
            "label": "DDP Officina",
            "micros": [
              "prices"
            ]
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.work_requests",
            "label": "Lavorazioni"
          },
          {
            "kind": "sezione-commessa",
            "chiave": "project.documenti",
            "label": "Documenti"
          },
          {
            "kind": "azione",
            "chiave": "action.create_project",
            "label": "Crea commessa",
            "ritirata": true
          },
          {
            "kind": "azione",
            "chiave": "action.edit_project",
            "label": "Modifica commessa"
          },
          {
            "kind": "azione",
            "chiave": "action.delete_project",
            "label": "Elimina commessa"
          },
          {
            "kind": "azione",
            "chiave": "action.project_locked_write",
            "label": "Scrive su commesse non attive"
          },
          {
            "kind": "azione",
            "chiave": "data.project_drafts",
            "label": "Vede le bozze di commessa"
          },
          {
            "kind": "azione",
            "chiave": "data.costs",
            "label": "Vede i costi (globale)"
          },
          {
            "kind": "azione",
            "chiave": "data.budget",
            "label": "Preventivo vs Consuntivo (dati)"
          },
          {
            "kind": "azione",
            "chiave": "action.import_project_phases",
            "label": "Importa fasi da libreria",
            "soloClient": true
          },
          {
            "kind": "azione",
            "chiave": "action.sync_project_phases",
            "label": "Sincronizza fasi da libreria",
            "soloClient": true
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.timesheet",
        "label": "Timesheet",
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.timesheet_for_others",
            "label": "Compila il timesheet di altri"
          },
          {
            "kind": "azione",
            "chiave": "action.timesheet_any_employee",
            "label": "Sceglie qualunque dipendente"
          },
          {
            "kind": "azione",
            "chiave": "data.timesheet_all_phases",
            "label": "Vede tutte le fasi (vista timesheet)"
          },
          {
            "kind": "azione",
            "chiave": "data.hourly_cost",
            "label": "Vede il costo orario",
            "ritirata": true
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "project.chat",
        "label": "Chat",
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.moderate_chat",
            "label": "Modera le chat (elimina messaggi altrui)"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.risorse",
        "label": "Risorse",
        "soloClient": true,
        "figli": [
          {
            "kind": "azione",
            "chiave": "resources.edit",
            "label": "Modifica il planner risorse"
          }
        ]
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "PM",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.mom",
        "label": "Verbali (MoM)"
      },
      {
        "kind": "voce",
        "chiave": "nav.gestore_ddp",
        "label": "Gestore DDP",
        "micros": [
          "prices"
        ],
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.delete_ddp_row",
            "label": "Elimina righe DDP"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.checklist",
        "label": "Check list"
      },
      {
        "kind": "voce",
        "chiave": "nav.milestones",
        "label": "Milestones"
      },
      {
        "kind": "voce",
        "chiave": "nav.sal",
        "label": "SAL / Fatturazione",
        "figli": [
          {
            "kind": "azione",
            "chiave": "sal.economics",
            "label": "Vede gli importi SAL (globale)"
          },
          {
            "kind": "azione",
            "chiave": "action.sal_edit_closed",
            "label": "Modifica SAL chiusi"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.trasferta",
        "label": "Trasferta"
      },
      {
        "kind": "voce",
        "chiave": "nav.ore_commessa",
        "label": "Ore Commessa"
      },
      {
        "kind": "voce",
        "chiave": "nav.bilancio",
        "label": "Bilancio",
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.edit_bilancio_settings",
            "label": "Modifica soglia/impostazioni Bilancio"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.work_requests",
        "label": "Lavorazioni Officine",
        "micros": [
          "prices"
        ],
        "figli": [
          {
            "kind": "azione",
            "chiave": "nav.officina_inbox",
            "label": "Inbox Officina (endpoint)",
            "micros": [
              "prices"
            ]
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.scadenze",
        "label": "Scadenze"
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Acquisti",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.acquisti_inbox",
        "label": "Inbox Acquisti",
        "micros": [
          "prices"
        ]
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Commerciale",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.preventivi",
        "label": "Preventivi"
      },
      {
        "kind": "voce",
        "chiave": "nav.cat_preventivi",
        "label": "Cat. Preventivi"
      },
      {
        "kind": "voce",
        "chiave": "nav.gamma_robot",
        "label": "Gamma Robot",
        "soloClient": true,
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.edit_gamma_robot",
            "label": "Modifica Gamma Robot"
          }
        ]
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Gestione",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.clienti",
        "label": "Clienti"
      },
      {
        "kind": "voce",
        "chiave": "nav.fornitori",
        "label": "Fornitori"
      },
      {
        "kind": "voce",
        "chiave": "nav.catalogo",
        "label": "Catalogo Articoli"
      },
      {
        "kind": "voce",
        "chiave": "nav.codex",
        "label": "Codex Articoli",
        "soloClient": true,
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.manage_codex",
            "label": "Gestisce il Codex (sync, genera codice)"
          },
          {
            "kind": "azione",
            "chiave": "action.recode_codex",
            "label": "Ricodifica Codex"
          },
          {
            "kind": "azione",
            "chiave": "action.assign_atec_code",
            "label": "Assegna il Codice ATEC"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.codex_composizione",
        "label": "Composizione Codex",
        "soloClient": true,
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.edit_codex_composition",
            "label": "Modifica composizione Codex"
          }
        ]
      },
      {
        "kind": "voce",
        "chiave": "nav.danea_migration",
        "label": "Trasferimento Danea",
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.import_easyfatt",
            "label": "Importa da Easyfatt"
          },
          {
            "kind": "azione",
            "chiave": "data.danea_explore",
            "label": "Esplora l'archivio Danea"
          }
        ]
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Amministrazione",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.utenti",
        "label": "Utenti"
      },
      {
        "kind": "voce",
        "chiave": "nav.permessi",
        "label": "Permessi"
      },
      {
        "kind": "azione",
        "chiave": "action.app_config",
        "label": "Modifica configurazione applicativa"
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Gestione avanzata",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.config_sezioni",
        "label": "Config. Sezioni di costo"
      },
      {
        "kind": "voce",
        "chiave": "nav.anagrafica_attivita",
        "label": "Anagrafica attività"
      },
      {
        "kind": "voce",
        "chiave": "nav.sal_condizioni",
        "label": "Condizioni pagamento SAL",
        "soloClient": true
      },
      {
        "kind": "voce",
        "chiave": "nav.ddp_destinazioni",
        "label": "Conf. DDP"
      },
      {
        "kind": "voce",
        "chiave": "nav.ddp_aggregazioni",
        "label": "Aggregazioni DDP"
      },
      {
        "kind": "voce",
        "chiave": "nav.backup",
        "label": "Backup DB"
      },
      {
        "kind": "voce",
        "chiave": "nav.project_templates",
        "label": "Template Commesse"
      },
      {
        "kind": "voce",
        "chiave": "nav.digest_email",
        "label": "Digest Email"
      }
    ]
  },
  {
    "kind": "sezione",
    "chiave": null,
    "label": "Supporto",
    "figli": [
      {
        "kind": "voce",
        "chiave": "nav.bug_reports",
        "label": "Segnalazioni",
        "figli": [
          {
            "kind": "azione",
            "chiave": "action.manage_bug_reports",
            "label": "Gestisce le segnalazioni (stati, risposte)"
          },
          {
            "kind": "azione",
            "chiave": "data.bug_reports_all",
            "label": "Vede le segnalazioni di tutti"
          }
        ]
      }
    ]
  }
]

/** Tutte le chiavi del catalogo, ordinate (duplicati condivisi esclusi). */
export const CHIAVI_CATALOGO: readonly ChiaveCatalogo[] = [
  "action.app_config",
  "action.assign_atec_code",
  "action.create_project",
  "action.delete_ddp_row",
  "action.delete_project",
  "action.edit_bilancio_settings",
  "action.edit_codex_composition",
  "action.edit_dashboard_settings",
  "action.edit_gamma_robot",
  "action.edit_project",
  "action.import_easyfatt",
  "action.import_project_phases",
  "action.manage_bug_reports",
  "action.manage_codex",
  "action.moderate_chat",
  "action.project_locked_write",
  "action.recode_codex",
  "action.sal_edit_closed",
  "action.sync_project_phases",
  "action.timesheet_any_employee",
  "action.timesheet_for_others",
  "action.toggle_dashboard_folder",
  "data.budget",
  "data.bug_reports_all",
  "data.costs",
  "data.danea_explore",
  "data.hourly_cost",
  "data.project_drafts",
  "data.revenue",
  "data.timesheet_all_phases",
  "nav.acquisti_inbox",
  "nav.anagrafica_attivita",
  "nav.backup",
  "nav.bilancio",
  "nav.bug_reports",
  "nav.cat_preventivi",
  "nav.catalogo",
  "nav.checklist",
  "nav.clienti",
  "nav.codex",
  "nav.codex_composizione",
  "nav.commesse",
  "nav.config_sezioni",
  "nav.danea_migration",
  "nav.dashboard",
  "nav.ddp_aggregazioni",
  "nav.ddp_destinazioni",
  "nav.digest_email",
  "nav.fornitori",
  "nav.gamma_robot",
  "nav.gestore_ddp",
  "nav.milestones",
  "nav.mom",
  "nav.officina_inbox",
  "nav.ore_commessa",
  "nav.permessi",
  "nav.preventivi",
  "nav.project_templates",
  "nav.risorse",
  "nav.sal",
  "nav.sal_condizioni",
  "nav.scadenze",
  "nav.timesheet",
  "nav.trasferta",
  "nav.utenti",
  "nav.work_requests",
  "project.chat",
  "project.checklist",
  "project.ddp_commerciale",
  "project.ddp_officina",
  "project.dettagli",
  "project.documenti",
  "project.flusso_cassa",
  "project.milestones",
  "project.mom",
  "project.sal",
  "project.work_requests",
  "resources.edit",
  "sal.economics",
]
