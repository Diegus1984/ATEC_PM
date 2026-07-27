import type { LucideIcon } from "lucide-react"
import {
  AlarmClock,
  Archive,
  ArrowRightLeft,
  BookOpen,
  Bot,
  BriefcaseBusiness,
  CalendarClock,
  ClipboardList,
  Cog,
  Database,
  FileStack,
  Factory,
  FileText,
  FolderKanban,
  LayoutDashboard,
  ListChecks,
  Mail,
  Milestone,
  NotebookPen,
  Package,
  ReceiptText,
  Puzzle,
  Shield,
  ShoppingCart,
  Truck,
  Users,
  Wrench,
} from "lucide-react"

export type ModuleStatus = "live" | "partial" | "planned"

export interface NavItemConfig {
  id: string
  label: string
  path: string
  featureKey: string
  icon: LucideIcon
  status: ModuleStatus
  description?: string
}

export interface NavGroupConfig {
  id: string
  label: string
  items: NavItemConfig[]
}

export const NAV_GROUPS: NavGroupConfig[] = [
  {
    id: "principale",
    label: "Principale",
    items: [
      {
        id: "dashboard",
        label: "Dashboard",
        path: "/",
        featureKey: "nav.dashboard",
        icon: LayoutDashboard,
        status: "live",
      },
      {
        id: "commesse",
        label: "Commesse",
        path: "/commesse",
        featureKey: "nav.commesse",
        icon: BriefcaseBusiness,
        status: "live",
        description:
          "Elenco commesse: ricerca, «Colonne», crea/modifica, annulla/elimina; dettaglio con panoramica, documenti e DDP commerciale.",
      },
      {
        id: "timesheet",
        label: "Timesheet",
        path: "/timesheet",
        featureKey: "nav.timesheet",
        icon: CalendarClock,
        status: "live",
      },
      {
        id: "risorse",
        label: "Risorse",
        path: "/risorse",
        featureKey: "nav.risorse",
        icon: FolderKanban,
        status: "live",
        description:
          "Pianificazione risorse (Gantt allocazioni op/flex/ferie) con conflitti, filtri, realtime; CRUD via dialogo.",
      },
    ],
  },
  {
    id: "pm",
    label: "PM",
    items: [
      {
        id: "mom-note",
        label: "Note MoM",
        path: "/mom/note",
        featureKey: "nav.mom",
        icon: NotebookPen,
        status: "live",
        description:
          "Acquisizione rapida (gestione v9): note personali con MoM di destinazione, «Assegna» porta il testo nel campo Azione della prima riga vuota del verbale.",
      },
      {
        id: "mom",
        label: "Verbali (MoM)",
        path: "/mom",
        featureKey: "nav.mom",
        icon: FileText,
        status: "live",
        description:
          "Lista verbali con revisioni; dettaglio a foglio editabile stile v9: celle inline con autosave, autocomplete definizioni, riordino drag&drop, giorno settimana/festivi sulle date, righe colorate, stampa/Word/Excel/CSV, realtime.",
      },
      {
        id: "gestore-ddp",
        label: "Gestore DDP",
        path: "/gestore-ddp",
        featureKey: "nav.gestore_ddp",
        icon: ClipboardList,
        status: "live",
        description:
          "Riepilogo DDP per commessa (cartelle hover-popover come MoM, card Commerciale/Officina, realtime) + sintesi completa: KPI, stati avanzamento, ripartizione/consegne/top10/destinazioni/mancanti/feedback, stampa ed Excel.",
      },
      {
        id: "checklist",
        label: "Check list",
        path: "/checklist",
        featureKey: "nav.checklist",
        icon: ListChecks,
        status: "live",
        description:
          "Raccoglitore e generatore di check list: attività per commessa (agganciate al tab della commessa) e gruppi generici, priorità P0–P3 con scadenze e badge giorni, inbox «Fissa attività» personale, realtime.",
      },
      {
        id: "milestones-summary",
        label: "Milestones",
        path: "/milestones",
        featureKey: "nav.milestones",
        icon: Milestone,
        status: "live",
        description:
          "Panoramica globale delle milestones di tutte le commesse attive, raggruppate per commessa con supporto a tabelle interattive e visualizzazione Gantt.",
      },
      {
        id: "sal",
        label: "SAL / Fatturazione",
        path: "/sal",
        featureKey: "nav.sal",
        icon: ReceiptText,
        status: "live",
        description:
          "Piani di fatturazione SAL di tutte le commesse + prospetto delle ipotesi di fatturazione aperte.",
      },
      {
        id: "sal-prospetto",
        label: "Prospetto SAL",
        path: "/sal/prospetto",
        featureKey: "nav.sal",
        icon: CalendarClock,
        status: "live",
        description:
          "Tutte le ipotesi di fatturazione aperte e le fatture emesse non incassate, con semaforo scadenze, controllo periodico, ordinamento ed export.",
      },
      {
        id: "work-requests",
        label: "Lavorazioni",
        path: "/work-requests",
        featureKey: "nav.work_requests",
        icon: Wrench,
        status: "live",
        description:
          "Pianifica, coordina e monitora le lavorazioni meccaniche interne ed esterne (Bozze, Per Commessa, Priorità, Consegne, Trattamenti).",
      },
      {
        id: "scadenze",
        label: "Scadenze",
        path: "/scadenze",
        featureKey: "nav.scadenze",
        icon: AlarmClock,
        status: "live",
        description:
          "Cruscotto unificato di tutte le scadenze (SAL, commesse, check list, MoM, DDP): elenco a sinistra, dettaglio del 'colpevole' a destra.",
      },
    ],
  },
  {
    id: "officina",
    label: "Officina",
    items: [
      {
        id: "officina-inbox",
        label: "Inbox Officina",
        path: "/officina",
        featureKey: "nav.officina_inbox",
        icon: Factory,
        status: "live",
        description:
          "Coda operativa DDP Officina cross-commessa: da fare, ritardi, in ordine, interna, trattamenti e per commessa.",
      },
    ],
  },
  {
    id: "acquisti",
    label: "Acquisti",
    items: [
      {
        id: "acquisti-inbox",
        label: "Inbox Acquisti",
        path: "/acquisti",
        featureKey: "nav.acquisti_inbox",
        icon: ShoppingCart,
        status: "live",
        description:
          "Inbox Acquisti raggruppata per commessa con card e griglie dedicate: fabbisogni, RDO, ordini Danea.",
      },
    ],
  },
  {
    id: "commerciale",
    label: "Commerciale",
    items: [
      {
        id: "preventivi",
        label: "Preventivi",
        path: "/preventivi",
        featureKey: "nav.preventivi",
        icon: FileStack,
        status: "live",
        description:
          "Elenco preventivi con catene di revisione (master + revisioni espandibili), filtri (tipo/stato/ricerca/colonne), vista griglia o per cliente, cambio stato inline, PDF, duplica, revisione, converti in commessa, elimina.",
      },
      {
        id: "catalogo-preventivi",
        label: "Cat. Preventivi",
        path: "/catalogo-preventivi",
        featureKey: "nav.cat_preventivi",
        icon: BookOpen,
        status: "live",
        description:
          "Catalogo preventivi: albero listini→gruppi→categorie→prodotti→varianti, crea/modifica/elimina, sposta (drag&drop), editor descrizione (TinyMCE) con immagini.",
      },
      {
        id: "gamma-robot",
        label: "Gamma Robot",
        path: "/gamma-robot",
        featureKey: "nav.gamma_robot",
        icon: Bot,
        status: "live",
        description:
          "Distinta Gamma Robot: Per Robot, Magazzino, Composizione (ADMIN) con drag&drop.",
      },
    ],
  },
  {
    id: "gestione",
    label: "Gestione",
    items: [
      {
        id: "clienti",
        label: "Clienti",
        path: "/clienti",
        featureKey: "nav.clienti",
        icon: Users,
        status: "live",
      },
      {
        id: "fornitori",
        label: "Fornitori",
        path: "/fornitori",
        featureKey: "nav.fornitori",
        icon: Truck,
        status: "live",
      },
      {
        id: "catalogo",
        label: "Catalogo Articoli",
        path: "/catalogo",
        featureKey: "nav.catalogo",
        icon: Package,
        status: "live",
        description:
          "Articoli di catalogo: ricerca, «Colonne», crea/modifica, elimina.",
      },
      {
        id: "codex",
        label: "Codex Articoli",
        path: "/codex",
        featureKey: "nav.codex",
        icon: Database,
        status: "live",
        description:
          "Articoli Codex: ricerca, «Colonne», sincronizzazione, genera codice, modifica/elimina (admin).",
      },
      {
        id: "codex-composizione",
        label: "Composizione Codex",
        path: "/codex-composizione",
        featureKey: "nav.codex_composizione",
        icon: Puzzle,
        status: "live",
        description:
          "Distinta compositi 5xx/6xx/7xx: albero composizione, aggiungi/rimuovi componenti (Codex o Catalogo) con quantità.",
      },
      {
        id: "danea-migrazione",
        label: "Trasferimento Danea",
        path: "/danea-migrazione",
        featureKey: "nav.danea_migration",
        icon: ArrowRightLeft,
        status: "live",
        description:
          "Migrazione al nuovo archivio Danea «Atec_PM»: trasferimento selettivo articoli (fornitore, IVA, prezzi, immagini) dal vecchio catalogo.",
      },
    ],
  },
  {
    id: "admin",
    label: "Amministrazione",
    items: [
      {
        id: "utenti",
        label: "Utenti",
        path: "/utenti",
        featureKey: "nav.utenti",
        icon: Users,
        status: "live",
      },
      {
        id: "permessi",
        label: "Permessi",
        path: "/permessi",
        featureKey: "nav.permessi",
        icon: Shield,
        status: "live",
        description:
          "Matrice funzioni × livelli: livello minimo, comportamento, crea/elimina funzione.",
      },
    ],
  },
  {
    id: "avanzata",
    label: "Gestione avanzata",
    items: [
      {
        id: "config-sezioni",
        label: "Config. Sezioni",
        path: "/config-sezioni",
        featureKey: "nav.config_sezioni",
        icon: Cog,
        status: "live",
      },
      {
        id: "anagrafica-attivita",
        label: "Anagrafica attività",
        path: "/anagrafica-attivita",
        featureKey: "nav.anagrafica_attivita",
        icon: ListChecks,
        status: "live",
        description:
          "Catalogo delle voci-attività standard precaricate alla creazione di una commessa (aggiungi, rinomina, riordina, ripristina).",
      },
      {
        id: "sal-conditions",
        label: "Condizioni pagamento SAL",
        path: "/admin/sal-conditions",
        featureKey: "nav.sal_condizioni",
        icon: ListChecks,
        status: "live",
        description:
          "Catalogo delle condizioni di pagamento utilizzabili negli step SAL delle commesse (aggiungi, rinomina, riordina, ripristina).",
      },
      {
        id: "ddp-destinazioni",
        label: "Conf. DDP",
        path: "/ddp-destinazioni",
        featureKey: "nav.ddp_destinazioni",
        icon: Wrench,
        status: "live",
      },
      {
        id: "ddp-aggregazioni",
        label: "Aggregazioni DDP",
        path: "/ddp-aggregazioni",
        featureKey: "nav.ddp_aggregazioni",
        icon: Archive,
        status: "live",
      },
      {
        id: "backup",
        label: "Backup DB",
        path: "/backup",
        featureKey: "nav.backup",
        icon: Archive,
        status: "live",
      },
      {
        id: "template-commesse",
        label: "Template Commesse",
        path: "/template-commesse",
        featureKey: "nav.project_templates",
        icon: FileStack,
        status: "live",
        description:
          "Cartelle e file template: nuova cartella, upload multiplo, rinomina (F2), taglia/copia/incolla, elimina (Canc).",
      },
      {
        id: "digest-email",
        label: "Digest Email",
        path: "/digest-email",
        featureKey: "nav.digest_email",
        icon: Mail,
        status: "live",
        description:
          "Configurazione SMTP + digest giornaliero automatico delle modifiche al piano risorse (dipendenti, responsabili, PM).",
      },
    ],
  },
]

export const ALL_NAV_ITEMS: NavItemConfig[] = NAV_GROUPS.flatMap(
  (group) => group.items
)

export function findNavItemByPath(path: string): NavItemConfig | undefined {
  if (path === "/") {
    return ALL_NAV_ITEMS.find((item) => item.path === "/")
  }

  return ALL_NAV_ITEMS.find(
    (item) => item.path !== "/" && path.startsWith(item.path)
  )
}
