/** Pagina «Ore Commessa» + causale «Extra Lavoro» (segnalazione #39). */
export interface ProjectHourRow {
  entryId: number
  employeeId: number
  employeeName: string
  workDate: string
  hours: number
  entryType: string
  notes: string

  projectPhaseId: number
  phaseName: string
  costSectionName: string
  /** IN_SEDE / DA_CLIENTE: è il tag che decide anche la trasferta. */
  costSectionType: string

  /** Costo orario del reparto principale della persona. */
  hourlyCost: number
  /** Ore × costo orario, calcolato dal server. */
  cost: number

  /** Il PM ha spostato la riga sulla causale «Extra Lavoro». */
  isExtra: boolean
  /** La riga pesa ancora sui costi della commessa. */
  countsInProject: boolean

  movedAt: string | null
  movedByName: string
}

/**
 * Card della pagina «Ore Commessa» (#109): una commessa con ore scaricate, quanto è
 * arrivato, in che finestra di date, e se il PM l'ha già guardato.
 */
export interface ProjectHoursSummary {
  projectId: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string

  /** Ore scaricate in tutto, Extra Lavoro compreso. */
  totalHours: number
  totalCost: number
  peopleCount: number
  /** Primo giorno scaricato (ISO). */
  firstWorkDate: string | null
  /** Ultimo giorno scaricato (ISO). */
  lastWorkDate: string | null

  pendingPeople: number
  pendingHours: number
  pendingFrom: string | null
  pendingTo: string | null
  verifiedAt: string | null
  verifiedByName: string
  /** Card in rosso: ore che nessuno ha ancora guardato. */
  needsVerification: boolean
}
