/**
 * Registro Offerte — la serie commerciale PARALLELA ai preventivi.
 * Allineato a `ATEC.PM.Shared/DTOs/SalesOffer_DTOs.cs` e `SalesOfferImport_DTOs.cs`.
 */

/** Gli stati ammessi. `bozza` = numero riservato, dati da completare. */
export type SalesOfferStatus = "bozza" | "aperta" | "presa" | "persa" | "sospesa"

/** Categorie dei tipi impianto: è ciò su cui si regge l'Analisi di mercato. */
export type SalesOfferCategory = "Impianto" | "Intervento" | "Ricambio" | "Altro"

export interface SalesOffer {
  id: number

  year: number
  /** Progressivo dell'anno. `null` solo sulle righe storiche migrate senza numero. */
  number: number | null
  /** Numero composto `S097-2026-EC`: lo calcola il server, non è persistito. */
  composed: string

  typeCode: string
  typeName: string
  typeCategory: string

  sellerCode: string
  sellerName: string
  sellerEmployeeId: number | null

  offerDate: string | null

  customerId: number | null
  /** Nome cliente come scritto sull'offerta: non si riscrive mai collegando la rubrica. */
  customerName: string
  /** Ragione sociale del cliente collegato (vuota se il nome non è agganciato). */
  customerCompanyName: string
  contactName: string

  description: string
  notes: string
  tag: string

  amount: number | null
  /** Estremo alto della forbice di prezzo («fino a»). Le statistiche usano `amount`. */
  amountMax: number | null

  status: string
  /** 0–100 a passi di 10. `null` = non valutata. */
  chance: number | null

  orderAmount: number | null
  isTimeMaterial: boolean

  advancePct: number | null
  advanceAmount: number | null
  advanceNotes: string

  postponedYear: number | null

  offerPath: string
  calcPath: string

  nextContact: string | null

  legacyNumber: string
  isLegacySeries: boolean

  quoteId: number | null
  quoteNumber: string
  projectId: number | null
  projectCode: string

  rowVersion: number
  createdAt: string | null
  updatedAt: string | null
  createdByName: string
  updatedByName: string

  followups: SalesOfferFollowup[]
  log: SalesOfferLogEntry[]
}

export interface SalesOfferSaveRequest {
  year: number
  number: number | null
  typeCode: string
  sellerCode: string
  offerDate: string | null
  customerId: number | null
  customerName: string
  contactName: string
  description: string
  notes: string
  tag: string
  amount: number | null
  amountMax: number | null
  status: string
  chance: number | null
  orderAmount: number | null
  isTimeMaterial: boolean
  advancePct: number | null
  advanceAmount: number | null
  advanceNotes: string
  postponedYear: number | null
  offerPath: string
  calcPath: string
  nextContact: string | null
  quoteId: number | null
  projectId: number | null
  /** Concorrenza ottimistica (solo modifica): `null` = scrivi comunque. */
  rowVersion: number | null
}

export interface SalesOfferCreated {
  id: number
  year: number
  number: number
  composed: string
  /** Il numero proposto era stato preso nel frattempo: ne è stato assegnato un altro. */
  renumbered: boolean
}

export interface SalesOfferFollowup {
  id: number
  offerId: number
  contactDate: string | null
  notes: string
  createdByName: string
  createdAt: string | null
}

export interface SalesOfferFollowupSaveRequest {
  contactDate: string | null
  notes: string
  /** Se valorizzata aggiorna anche il prossimo contatto sull'offerta. */
  nextContact: string | null
}

export interface SalesOfferLogEntry {
  id: number
  field: string
  oldValue: string
  newValue: string
  changedByName: string
  changedAt: string | null
}

export interface SalesOfferListResponse {
  items: SalesOffer[]
  total: number
}

export interface SalesOfferType {
  code: string
  name: string
  category: string
  sortOrder: number
  isActive: boolean
}

export interface SalesOfferSeller {
  code: string
  name: string
  employeeId: number | null
  isActive: boolean
}

export interface SalesOfferNextNumber {
  year: number
  number: number
  composed: string
}

export interface SalesOfferBulkCloseRequest {
  untilYear: number
  status: string
}

export interface SalesOfferUnlinkedName {
  nome: string
  offerte: number
}

export interface SalesOfferImportReport {
  simulazione: boolean
  clientiLetti: number
  clientiCreati: number
  clientiArricchiti: number
  clientiInvariati: number
  offerteLette: number
  offerteInserite: number
  offerteGiaPresenti: number
  contattiInseriti: number
  collegateACliente: number
  collegateACommessa: number
  senzaCliente: number
  nomiNonCollegati: SalesOfferUnlinkedName[]
  avvisi: string[]
  eseguitoIl: string
  durataMs: number
}

/** Evento dell'hub `sales-offers-all`. */
export interface SalesOffersChange {
  action: string
  offerId: number
}
