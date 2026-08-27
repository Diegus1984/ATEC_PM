/** Modulo HR presenze (PIANO-HR-PRESENZE.md) — speculari a Hr_DTOs.cs. */

export interface HrTimbratura {
  id: number
  orario: string
  verso: string
  origine: string
  motivo?: string | null
  creataDa?: string | null
}

export interface HrGiornata {
  giorno: string
  festivo: boolean
  haDati: boolean
  entrata1: string
  uscita1: string
  entrata2: string
  uscita2: string
  /** «8h 0m», oppure «---» quando la giornata non è calcolabile. */
  oreOrdinarie: string
  straordinario: string
  pausa: string
  /** Solo le fasce CCNL diverse da zero (chiave = lettera della circolare). */
  fasce: Record<string, string>
  nota: string
  anomalia: boolean
  timbrature: HrTimbratura[]
}

export interface HrCartellinoMese {
  employeeId: number
  employeeName: string
  anno: number
  mese: number
  ecosCollegato: boolean
  giornate: HrGiornata[]
}

export interface HrStato {
  configurato: boolean
  importInCorso: boolean
  ultimoImport?: string | null
  ultimoEsito: string
  timbratureTotali: number
  giornateTotali: number
  dipendentiCollegati: number
  dipendentiAttivi: number
}

export interface HrImportEsito {
  successo: boolean
  messaggio: string
  timbratureNuove: number
  timbratureAggiornate: number
  giornateRicalcolate: number
  nonAbbinati: string[]
}

export interface HrMappaturaRiga {
  employeeId: number
  nome: string
  ecosEmplCode?: string | null
}

export interface HrBadge {
  emplCode: string
  nome: string
  inForza: boolean
}

export interface HrBadges {
  configurato: boolean
  badges: HrBadge[]
}
