/**
 * SEZIONI DELL'ALBERO COMMESSA — file catalogo dei permessi insieme a
 * `config/navigation.ts` (voci del menu laterale).
 *
 * DATI SENSIBILI (prezzi/costi/margini) — regola PIANO-PERMESSI-REBUILD.md §4:
 * se una sezione espone importi, la sensibilità si dichiara A CATALOGO nello stesso PR,
 * non si «scopre» a runtime cercando € a video. Oggi (motore v87) gli importi sono
 * governati dalle chiavi globali data.costs / data.revenue / sal.economics; col
 * catalogo unico del rebuild la sezione dichiarerà `sensitive: ["prices"]`.
 * Dettagli: .cursor/rules/permessi-catalogo-sensitive.mdc
 */
import type { ChiaveCatalogo } from "@/config/catalogo.gen"

export interface CommessaSection {
  key: string
  label: string
  /** Emoji come nel WPF (le altre sezioni); Dashboard Commessa usa 📈 in web. */
  icon?: string
  /** Visibile solo a PM/ADMIN (Prev vs Consuntivo). */
  economicsOnly?: boolean
  /**
   * Permesso richiesto per vedere la sezione, stesse chiavi del menu principale
   * (pagina «Permessi»): chi non ha il livello minimo non la vede nell'albero e
   * non può aprirla nemmeno scrivendo l'indirizzo a mano.
   * Chiusa sul catalogo unico: una chiave fuori da catalogo-permessi.json non compila.
   */
  featureKey?: ChiaveCatalogo
}

/**
 * Le sezioni fisse di ogni commessa, NELLO STESSO ORDINE del WPF
 * (`ProjectsPage.BuildTree`). Non riordinare: è la fedeltà richiesta.
 * La prima voce si chiama «Dashboard Commessa» (segnalazione #46; prima «Dettagli»).
 */
export const COMMESSA_SECTIONS: CommessaSection[] = [
  { key: "details", label: "Dashboard Commessa", icon: "📈", featureKey: "project.dettagli" },
  {
    key: "cashflow",
    label: "Flusso di Cassa",
    icon: "💰",
    featureKey: "project.flusso_cassa",
  },
  {
    key: "budget_vs_actual",
    label: "Preventivo vs Consuntivo",
    icon: "📊",
    economicsOnly: true,
  },
  { key: "chat", label: "Chat", icon: "💬", featureKey: "project.chat" },
  // Dal passo 3 dello split (20/08/2026) l'albero ha chiavi proprie `project.*`,
  // fotografate dalle `nav.*` del menu PM dalla M103: da qui in poi menu e albero
  // si concedono separatamente (caso #77).
  { key: "mom", label: "Verbali (MoM)", icon: "📝", featureKey: "project.mom" },
  {
    key: "checklist",
    label: "Check list",
    icon: "✅",
    featureKey: "project.checklist",
  },
  {
    key: "milestones",
    label: "Milestone",
    icon: "📅",
    featureKey: "project.milestones",
  },
  { key: "sal", label: "SAL / Fatturazione", icon: "💶", featureKey: "project.sal" },
  {
    key: "ddp_commercial",
    label: "DDP Commerciali",
    icon: "📋",
    featureKey: "project.ddp_commerciale",
  },
  {
    key: "ddp_officina",
    label: "DDP Officina",
    icon: "🔧",
    featureKey: "project.ddp_officina",
  },
  {
    key: "work_requests",
    label: "Lavorazioni",
    icon: "⚙️",
    featureKey: "project.work_requests",
  },
  { key: "documents", label: "Documenti", icon: "📁", featureKey: "project.documenti" },
]

/** Sezioni che l'utente collegato può vedere (albero commessa e apertura diretta). */
export function sezioniVisibili(
  canSeeEconomics: boolean,
  canAccessFeature: (featureKey: string) => boolean
): CommessaSection[] {
  return COMMESSA_SECTIONS.filter(
    (section) =>
      (canSeeEconomics || !section.economicsOnly) &&
      (!section.featureKey || canAccessFeature(section.featureKey))
  )
}
