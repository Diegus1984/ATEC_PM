import { apiGet, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  AuthFeatureDto,
  AuthFeaturesContextDto,
  AuthLevelDto,
  FeatureAccess,
} from "@/lib/api/types"

let userLevel = 0
let features = new Map<string, AuthFeatureDto>()
let levels: AuthLevelDto[] = []
let accessMode = "LEVEL"
let grants = new Map<string, FeatureAccess>()
let permissionsEngine = "LEGACY"

/**
 * Esito dell'ultimo tentativo di leggere `/features/my`.
 *
 * Da fuori i due guai si vedono uguali — menu vuoto — ma il rimedio è opposto: se i permessi
 * non sono ARRIVATI si riprova, se sono arrivati VUOTI serve un amministratore. Senza questo
 * stato la schermata di avviso non saprebbe quale delle due cose dire, e finirebbe per
 * mandare dall'amministratore chi ha solo perso la rete per un secondo.
 *
 * `idle` = mai caricati (nessuna sessione, o bootstrap non ancora partito): si tratta come
 * un errore, perché anche lì l'unica mossa sensata è riprovare.
 */
export type AuthFeaturesStatus = "idle" | "ready" | "error"

/**
 * L'istantanea che React confronta (`useSyncExternalStore`). Non è solo lo stato: c'è anche un
 * contatore, perché React ridisegna solo se l'istantanea CAMBIA, e una ricarica riuscita
 * partendo da «ready» lascerebbe lo stato identico pur avendo in mano permessi diversi — il
 * menu resterebbe quello di prima. È il caso del futuro `PermissionsChanged` dall'hub, ma anche
 * di un secondo `loadAuthFeatures()` qualsiasi: meglio che la sveglia funzioni sempre.
 */
export interface AuthFeaturesSnapshot {
  status: AuthFeaturesStatus
  revision: number
}

// Oggetto sostituito (mai mutato) a ogni cambiamento: fra un cambiamento e l'altro
// `getAuthFeaturesSnapshot` deve tornare SEMPRE lo stesso riferimento, o React rientrerebbe
// in un ciclo di render infinito.
let snapshot: AuthFeaturesSnapshot = { status: "idle", revision: 0 }
const statusListeners = new Set<() => void>()

function setStatus(next: AuthFeaturesStatus): void {
  snapshot = { status: next, revision: snapshot.revision + 1 }
  // Copia della lista: un ascoltatore che si disiscrive dentro il proprio callback
  // (è quello che fa React smontando la schermata di avviso) salterebbe il successivo.
  for (const listener of Array.from(statusListeners)) {
    listener()
  }
}

export function getAuthFeaturesSnapshot(): AuthFeaturesSnapshot {
  return snapshot
}

/** Lo stato senza passare da React: per chi legge i permessi fuori da un componente. */
export function getAuthFeaturesStatus(): AuthFeaturesStatus {
  return snapshot.status
}

/**
 * I permessi vivono in variabili di modulo, fuori da React: senza un avviso esplicito il menu
 * resterebbe quello del primo render anche dopo un «Riprova» andato a buon fine.
 */
export function subscribeAuthFeatures(listener: () => void): () => void {
  statusListeners.add(listener)
  return () => {
    statusListeners.delete(listener)
  }
}

export async function loadAuthFeatures(): Promise<void> {
  let context: AuthFeaturesContextDto
  try {
    const response = await apiGet<ApiResponse<AuthFeaturesContextDto>>(
      "/api/auth-levels/features/my"
    )
    context = unwrapApi(response)
  } catch (error) {
    // La chiamata è caduta: si resta senza permessi (nessuna concessione per errore), ma
    // marcati come ERRORE, così l'utente si vede offrire «Riprova» e non un rimprovero.
    // L'errore va comunque rilanciato: chi chiama decide se aspettare o proseguire.
    setStatus("error")
    throw error
  }

  userLevel = context.userLevel
  levels = context.levels
  accessMode = context.accessMode ?? "LEVEL"
  permissionsEngine = context.permissionsEngine ?? "LEGACY"
  grants = new Map(
    Object.entries(context.grants ?? {}).map(([key, access]) => [
      key.toLowerCase(),
      access,
    ])
  )
  features = new Map(
    context.features.map((feature) => [feature.featureKey.toLowerCase(), feature])
  )
  setStatus("ready")
}

export function clearAuthFeatures(): void {
  userLevel = 0
  features = new Map()
  levels = []
  accessMode = "LEVEL"
  grants = new Map()
  permissionsEngine = "LEGACY"
  setStatus("idle")
}

/**
 * I permessi arrivano dalle righe della PERSONA (`employee_feature_access`) e non più dai
 * livelli e dalle liste di ruolo? Non serve a decidere chi vede cosa — a quello rispondono
 * `canAccessFeature` / `canWriteFeature` — ma a far dire la verità alla pagina «Permessi»:
 * con questo motore acceso la matrice funzioni × ruoli scrive su tabelle che nessuno legge
 * più, quindi salvarci sopra non cambia niente per nessuno.
 */
export function isPersonPermissionEngine(): boolean {
  return permissionsEngine === "NEW"
}

/** Ruolo di reparto (es. AMM): niente eredità dal livello, vale solo la lista bianca. */
function isGrantsRole(): boolean {
  return accessMode === "GRANTS"
}

/** Stessa logica di FeatureAccessService.CanAccess in C#. */
export function canAccessFeature(featureKey: string): boolean {
  // ⚠️ Permessi non ancora arrivati (o `/features/my` fallito): NON si concede niente.
  //
  // Fin qui qui si rispondeva `true`, col ragionamento «tanto poi chiude l'API». Ma il risultato
  // era che un errore di rete al login mostrava a chiunque il menu INTERO, e siccome parecchi
  // endpoint non hanno un controllo di funzione, buona parte di quello che l'utente cliccava
  // funzionava davvero. Con i permessi sulla persona quel fallback diventa il caso peggiore:
  // la persona più limitata vedrebbe tutto proprio quando il sistema è in difficoltà.
  //
  // Ora il menu resta VUOTO e l'utente vede un avviso. È scomodo ed è giusto: è l'unico
  // comportamento che non concede per errore. (PIANO-PERMESSI.md §7.1 — l'inversione va fatta
  // qui E in FeatureAccessService, o metà del sistema continua a dire di sì.)
  if (features.size === 0) {
    return false
  }

  // Concessione esplicita al ruolo: vale sempre, si somma al livello.
  if (grants.has(featureKey.toLowerCase())) {
    return true
  }

  // Ruolo di reparto senza concessione: negato anche se la funzione non è registrata
  // (per i ruoli a livello vale invece il contrario — vedi sotto).
  if (isGrantsRole()) {
    return false
  }

  // Funzione che non esiste nel catalogo: negata. Prima era consentita a tutti, ed è il motivo
  // per cui intere sezioni (Chat, DDP, Documenti) sono rimaste aperte per mesi senza che nessuno
  // se ne accorgesse: dimenticare di registrare una chiave non dava nessun segnale.
  // Prima di invertire è stato verificato che nessuna chiave usata nel codice mancasse dal
  // catalogo, altrimenti qui sparirebbero pagine vere.
  const feature = features.get(featureKey.toLowerCase())
  if (!feature) {
    return false
  }

  return userLevel >= feature.minLevel
}

/**
 * Funzione concessa in SOLA LETTURA: la pagina si apre, i comandi di modifica no.
 * A chiudere davvero ci pensa l'API (`RequireFeature` respinge i verbi di scrittura).
 */
export function isFeatureReadOnly(featureKey: string): boolean {
  return grants.get(featureKey.toLowerCase()) === "READ"
}

/** Può modificare i dati di questa funzione? */
export function canWriteFeature(featureKey: string): boolean {
  return canAccessFeature(featureKey) && !isFeatureReadOnly(featureKey)
}

// ── La scala di livello non esiste più (PIANO-PERMESSI.md, Fase E) ─────────────────────
//
// Fin qui, accanto alle chiavi, viveva una seconda regola: `isPmLevel()`, `isAdminLevel()`,
// `isResponsibleLevel()`, `hasLevel(n)`. Erano 24 punti sparsi nelle pagine — elimina
// commessa, conto economico, soglia del Bilancio, import SAL, ricodifica Codex — che
// decidevano da soli, senza passare da nessuna funzione. Il risultato era che girare una
// manopola in «Permessi» nascondeva la voce di menu ma non toccava quei pulsanti, e
// spostare una persona su una classe più bassa le spegneva cose che nessuna concessione
// poteva ridarle.
//
// Ora ogni punto ha la sua chiave e si legge con `canAccessFeature` / `canWriteFeature`.
// Le funzioni per livello sono state tolte apposta: finché esistono, la prima pagina
// scritta di fretta le riusa e la scala ricomincia a crescere.
//
// `userLevel` resta, ma solo come dato del motore VECCHIO (ramo `min_level` qui sopra),
// che deve continuare a funzionare finché l'interruttore `PermissionsEngine` può tornare
// indietro. Non è un permesso: non usarlo per decidere niente nelle pagine.

export function getAuthLevels(): AuthLevelDto[] {
  return levels
}

export function getAuthFeatures(): AuthFeatureDto[] {
  return Array.from(features.values())
}
