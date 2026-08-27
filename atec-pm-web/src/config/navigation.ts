/**
 * CATALOGO VOCI del menu laterale — file catalogo dei permessi insieme a
 * `features/commesse/commessa-sections.ts` (sezioni dell'albero commessa).
 *
 * DATI SENSIBILI (prezzi/costi/margini) — regola PIANO-PERMESSI-REBUILD.md §4:
 * se una voce espone importi, la sensibilità si dichiara A CATALOGO nello stesso PR,
 * non si «scopre» a runtime cercando € a video. Oggi (motore v87) gli importi sono
 * governati dalle chiavi globali data.costs / data.revenue / sal.economics; col
 * catalogo unico del rebuild la voce dichiarerà `sensitive: ["prices"]`.
 * Dettagli: .cursor/rules/permessi-catalogo-sensitive.mdc
 */
import type { LucideIcon } from "lucide-react"
import type { ChiaveCatalogo } from "./catalogo.gen"
import {
  AlarmClock,
  Archive,
  ArrowRightLeft,
  BookOpen,
  Bot,
  BriefcaseBusiness,
  Bug,
  CalendarCheck,
  CalendarClock,
  ClipboardList,
  Clock,
  Cog,
  Database,
  FileStack,
  FileText,
  Fingerprint,
  FolderKanban,
  History,
  LayoutDashboard,
  ListChecks,
  Mail,
  MessageCircle,
  Milestone,
  NotebookPen,
  Package,
  Plane,
  ReceiptText,
  Puzzle,
  Scale,
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
  /** Chiusa sul catalogo unico: una chiave fuori da catalogo-permessi.json non compila. */
  featureKey: ChiaveCatalogo
  icon: LucideIcon
  status: ModuleStatus
  description?: string
}

export interface NavGroupConfig {
  id: string
  label: string
  items: NavItemConfig[]
  /** Gruppo ancorato in fondo alla sidebar (sopra la scheda utente). */
  pinBottom?: boolean
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
        // Voce di tutti: la chiave `project.chat` è concessa a chiunque entri nel
        // gestionale (migrazione v88, #78). Il pallino rosso accanto alla voce conta i
        // messaggi non letti di tutte le conversazioni.
        id: "chat",
        label: "Chat",
        path: "/chat",
        featureKey: "project.chat",
        icon: MessageCircle,
        status: "live",
        description:
          "Tutte le chat di cui fai parte, divise per commessa, altra attività e senza commessa: pallino arancione sulle conversazioni con messaggi da leggere, «@» per tirare dentro un collega.",
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
          "Piani di fatturazione SAL di tutte le commesse + prospetto delle ipotesi di fatturazione aperte (vista rapida «Prospetto SAL»): semaforo scadenze, ordinamento ed export.",
      },
      {
        id: "trasferta",
        label: "Trasferta",
        path: "/trasferta",
        featureKey: "nav.trasferta",
        icon: Plane,
        status: "live",
        description:
          "Trasferte di tutte le commesse: card con Giorni/Costo Ore Trasferta/Spese Trasferta, step collassabili con la griglia riga-persona a 14 colonne, le 4 calcolatrici (ore, vitto, indennità, auto) e il Riepilogo Trasferta. Le spese confluiscono nella voce «Spese Trasferta» del Bilancio.",
      },
      {
        id: "ore-commessa",
        label: "Ore Commessa",
        path: "/ore-commessa",
        featureKey: "nav.ore_commessa",
        icon: Clock,
        status: "live",
        description:
          "Tutte le ore imputate su una commessa, riga per riga (persona, giorno, fase, sezione, costo). Il PM può spostarne una parte sulla causale «Extra Lavoro», che le toglie dai costi della commessa, e rimetterle dentro una per una per misurarne il peso sulla redditività.",
      },
      {
        id: "bilancio",
        label: "Bilancio",
        path: "/bilancio",
        featureKey: "nav.bilancio",
        icon: Scale,
        status: "live",
        description:
          "Redditività a consuntivo di tutte le commesse: una card per commessa con Consuntivo Redditività in € e in %, rossa sotto la soglia impostata, e ingresso al conto economico completo.",
      },
      {
        id: "work-requests",
        label: "Lavorazioni Officine",
        path: "/work-requests",
        featureKey: "nav.work_requests",
        icon: Wrench,
        status: "live",
        description:
          "Le righe della DDP Officina di tutte le commesse nelle quattro viste dell'officina (Interne, Esterne, Urgenze, Trattamenti), più le righe inserite a mano. Data richiesta, note e urgenza si scrivono qui; tutto il resto segue la distinta.",
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
  // Il gruppo «Officina» con la Inbox è stato tolto con la segnalazione #83: quella coda è
  // diventata la pagina «Lavorazioni Officine» nel gruppo PM, che fa le stesse cose divise
  // per come si lavora davvero (interne, esterne, urgenze, trattamenti).
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
    // Sezione HR — presenze, ferie e permessi (piano: PIANO-HR-PRESENZE.md).
    // Le voci nascono `planned`: senza rotta in LIVE_ROUTES mostrano ModulePlaceholder,
    // e le chiavi partono spente (EnsureCatalogo le registra a livello 3, solo Admin).
    id: "hr",
    label: "HR",
    items: [
      {
        id: "hr-timbrature",
        label: "Timbrature",
        path: "/hr/timbrature",
        featureKey: "nav.hr_timbrature",
        icon: Fingerprint,
        status: "planned",
        description:
          "Cartellino presenze: timbrature importate da EcosAgile, calcolo ore, pausa e straordinari per fascia CCNL.",
      },
      {
        id: "hr-richieste",
        label: "Ferie e permessi",
        path: "/hr/richieste",
        featureKey: "nav.hr_richieste",
        icon: CalendarCheck,
        status: "planned",
        description:
          "Richieste di ferie e permessi con approvazione del responsabile di reparto.",
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
        label: "Config. Sezioni di costo",
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
  {
    // Ancorato in fondo alla sidebar: è assistenza, non lavoro quotidiano.
    id: "supporto",
    label: "Supporto",
    pinBottom: true,
    items: [
      {
        id: "bug-reports",
        label: "Segnalazioni",
        path: "/segnalazioni",
        featureKey: "nav.bug_reports",
        icon: Bug,
        status: "live",
        description:
          "Bug e richieste di miglioramento su ATEC PM, con allegati (screenshot): ognuno vede e gestisce le proprie (#93, elenco completo solo a chi ha la vista), l'ADMIN cambia stato e risponde. Notifica agli ADMIN a ogni nuova segnalazione.",
      },
      {
        id: "changelog",
        label: "Changelog versioni",
        path: "/changelog",
        featureKey: "nav.changelog",
        icon: History,
        status: "live",
        description:
          "Storia delle versioni pubblicate: per ogni build le modifiche (dai commit del deploy) e le segnalazioni chiuse.",
      },
    ],
  },
]

export const ALL_NAV_ITEMS: NavItemConfig[] = NAV_GROUPS.flatMap(
  (group) => group.items
)

/**
 * Prima voce di menu visibile all'utente, esclusa la Dashboard. È la home di ripiego dei
 * ruoli di reparto (es. AMM), che la Dashboard non la vedono: senza questo, entrando in
 * «/» si troverebbero l'«Accesso negato» al posto della pagina iniziale.
 *
 * I gruppi ancorati in fondo (Supporto) restano fuori: sono pagine di assistenza, non la
 * pagina di lavoro con cui si apre la giornata. Per l'AMM la home è quindi SAL / Fatturazione.
 */
export function firstAccessibleNavPath(
  canAccess: (featureKey: string) => boolean
): string | undefined {
  return NAV_GROUPS.filter((group) => !group.pinBottom)
    .flatMap((group) => group.items)
    .find((item) => item.path !== "/" && canAccess(item.featureKey))?.path
}

export function findNavItemByPath(path: string): NavItemConfig | undefined {
  if (path === "/") {
    return ALL_NAV_ITEMS.find((item) => item.path === "/")
  }

  return ALL_NAV_ITEMS.find(
    (item) => item.path !== "/" && path.startsWith(item.path)
  )
}
