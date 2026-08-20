/** Gestione Trasferta (blocco 6): step di trasferta e righe-persona. */

/** Le quattro calcolatrici di una riga-persona. */
export const TRAVEL_CALC_KINDS = ["ore", "vitto", "indennita", "auto"] as const
export type TravelCalcKind = (typeof TRAVEL_CALC_KINDS)[number]

export interface TravelRowDto {
  id: number
  stepId: number
  /** Dipendente scelto in anagrafica; null se il nome è solo storico. */
  employeeId: number | null
  /** Nome scritto sulla riga: resta leggibile anche se il dipendente sparisce. */
  personName: string
  /**
   * Giorno della trasferta, sulle righe nate dal Timesheet (#37/#52): sostituisce
   * inizio/fine. Null sulle righe scritte a mano, che usano ancora le due date.
   */
  workDate: string | null
  /** "MANUAL" (scritta a mano) o "TIMESHEET" (derivata dalle ore). */
  source: string
  /**
   * Giorni imputati sulle righe derivate: il valore della giornata sulla prima riga
   * (0,5 fino a 4 ore scaricate, 1 oltre — #98), 0 sulle altre.
   */
  travelDays: number | null
  /** Le ore che avevano generato la riga non ci sono più: la riga resta, con l'avviso. */
  hoursMissing: boolean
  /** Scorciatoia del server: `source === "TIMESHEET"`. */
  isDerived: boolean
  startDate: string | null
  endDate: string | null
  excludeSat: boolean
  excludeSun: boolean
  /** Totale della calcolatrice «Ore». */
  hours: number | null
  hourlyRate: number | null
  nights: number | null
  nightPrice: number | null
  mealCost: number | null
  allowanceCost: number | null
  carCost: number | null
  transportCost: number | null
  sortOrder: number
  rowVersion: number
  /** Calcolati dal server: fine − inizio + 1 (con esclusione sab/dom), tariffa × ore, notti × prezzo. */
  days: number | null
  personnelCost: number | null
  lodgingCost: number | null
  travelCost: number
  totalCost: number
}

export interface TravelTotalsDto {
  days: number
  hours: number
  personnelCost: number
  lodgingCost: number
  mealCost: number
  allowanceCost: number
  carCost: number
  transportCost: number
  /** Tutto tranne il personale. */
  travelCost: number
  totalCost: number
}

export interface TravelStepDto {
  id: number
  /** Fase di commessa da cui nasce lo step (#37/#52). Null = step aperto a mano. */
  projectPhaseId: number | null
  description: string
  sortOrder: number
  rowVersion: number
  rows: TravelRowDto[]
  totals: TravelTotalsDto
}

export interface TravelSummaryRowDto {
  personName: string
  totals: TravelTotalsDto
}

export interface TravelPlanDto {
  projectId: number
  steps: TravelStepDto[]
  /** Una riga per nominativo distinto, in ordine alfabetico italiano. */
  summary: TravelSummaryRowDto[]
  grandTotals: TravelTotalsDto
}

/** Card della pagina «Gestione Trasferta». */
export interface TravelProjectSummaryDto {
  projectId: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  stepCount: number
  days: number
  hours: number
  personnelCost: number
  travelCost: number
  /** Costo delle ore di cantiere dal timesheet (#92): informativo, non va al Bilancio. */
  travelHoursCost: number
  /** Ore di cantiere a consuntivo (#96): stesso perimetro DA_CLIENTE del costo qui sopra. */
  travelHours: number
  /** Monte ore pianificate delle fasi cantiere (#96): assegnazioni delle sezioni DA_CLIENTE. */
  plannedHours: number
  /** Costo preventivato di quelle ore, come lo calcola il Bilancio (tariffa media di sezione). */
  plannedHoursCost: number
  /** «Spese Trasferta» a preventivo: la stessa voce del Riepilogo Costi del Bilancio. */
  plannedTravelCost: number
  /**
   * Giorni di trasferta preventivati (#98): risorse pianificate delle sezioni DA_CLIENTE,
   * GG × il valore della giornata (0,5 fino a 4 Ore/g, 1 oltre).
   */
  plannedDays: number

  // ── Scarico ore da verificare (#102) ────────────────────────
  // Solo ore su fasi «da cliente»: sono le uniche che generano righe di trasferta.

  /** Persone con ore arrivate dopo l'ultima «Verifica effettuata». */
  pendingPeople: number
  /** Ore non ancora verificate. */
  pendingHours: number
  /** Primo giorno dello scarico non verificato (ISO). */
  pendingFrom: string | null
  /** Ultimo giorno dello scarico non verificato (ISO). */
  pendingTo: string | null
  /** Quando è stata data l'ultima verifica. `null` = mai guardata. */
  verifiedAt: string | null
  verifiedByName: string
  /** Card in rosso: c'è scarico che nessuno ha ancora guardato. */
  needsVerification: boolean
}

export interface TravelStepSaveRequest {
  description: string
  rowVersion: number | null
}

export interface TravelRowSaveRequest {
  employeeId: number | null
  personName: string
  startDate: string | null
  endDate: string | null
  excludeSat: boolean
  excludeSun: boolean
  hourlyRate: number | null
  nights: number | null
  nightPrice: number | null
  transportCost: number | null
  rowVersion: number | null
}

/** Aggrega costi Trasferta (#67): campi da copiare sulle righe selezionate. */
export interface TravelApplyCostsRequest {
  rowIds: number[]
  nights: number | null
  nightPrice: number | null
  mealCost: number | null
  allowanceCost: number | null
  carCost: number | null
  transportCost: number | null
  /** Campi da scrivere: nights, nightPrice, mealCost, allowanceCost, carCost, transportCost. */
  fields: string[]
}
