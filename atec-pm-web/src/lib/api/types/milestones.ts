/** Milestone e anagrafica attività — allineati a ATEC.PM.Shared/DTOs. */

// Voce del catalogo "Anagrafica attività" (elenco globale delle attività standard di progetto).
export interface ActivityCatalogItem {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

export interface ActivityCatalogSaveRequest {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

// Milestone = riga di pianificazione di una commessa (copia snapshot dal catalogo attività).
export interface Milestone {
  id: number
  projectId: number
  descrizione: string
  dataInizio: string | null
  dataFine: string | null
  avanzamento: number | null
  note: string
  evidenza: boolean
  spento: boolean
  sortOrder: number
  rowVersion: number
  sourceCatalogId: number | null
}

/** Riepilogo per-commessa delle milestone attive (GET /api/milestones/summary):
 *  conteggi di stato calcolati sulle sole righe non spente. Alimenta i pallini +
 *  conteggio della sidebar PM globale senza rompere il lazy-load delle card. */
export interface MilestoneSummary {
  projectId: number
  code: string
  title: string
  active: number
  late: number
  current: number
  done: number
}

export interface MilestoneSaveRequest {
  descrizione: string
  dataInizio: string | null
  dataFine: string | null
  avanzamento: number | null
  note: string
  evidenza: boolean
  spento: boolean
  rowVersion: number | null
}
