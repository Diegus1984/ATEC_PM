/** Amministrazione (backup) — allineati a ATEC.PM.Shared/DTOs. */

export interface BackupFileInfo {
  fileName: string
  sizeMB: number
  date: string
}

/** Una cartella inclusa nel pacchetto completo. */
export interface FullBackupFolder {
  nome: string
  percorso: string
  esiste: boolean
  file: number
  dimensioneMB: number
}

/** Anteprima di cosa finirebbe nel pacchetto, prima di crearlo. */
export interface FullBackupEstimate {
  cartelle: FullBackupFolder[]
  totaleFileMB: number
  spazioLiberoDestinazioneGB: number
  destinazione: string
}

/** Avanzamento di un backup/ripristino completo (girano in background). */
export interface FullBackupJob {
  id: string
  tipo: "backup" | "ripristino"
  stato: "in_corso" | "completato" | "errore"
  passo: string
  percentuale: number
  messaggio: string
  fileName: string
  dimensioneMB: number
  fileSaltati: number
  inizio: string
  fine: string | null
  /** Diario dell'operazione, una riga per passo: mostrato a video come una console. */
  righe: string[]
}

export interface FullBackupPackage {
  fileName: string
  sizeMB: number
  date: string
  contenuto: {
    creato?: string
    macchina?: string
    database?: { tabelle: number; righe: number; schema: number }
    file?: { totali: number; saltati: number }
    cartelle?: { nome: string; percorso: string }[]
  } | null
}
