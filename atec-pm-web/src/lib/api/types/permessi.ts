/** Tipi della scheda permessi per persona — allineati a ATEC.PM.Shared/DTOs/Permessi_DTOs.cs */

/** `NO` = non abilitato (nessuna riga, o un diniego), `READ` = sola lettura, `FULL` = piena. */
export type StatoCombo = "NO" | "READ" | "FULL"

/** Chi ha scritto la riga: il pacchetto del template, o una mano umana. */
export type OriginPermesso = "CLASSE" | "MANO" | ""

/**
 * Una voce del catalogo sulla scheda di una persona: lo stato EFFETTIVO (jolly già espanso)
 * e chi l'ha deciso. Il vecchio `statoClasse`/`areaId` è uscito col passo 7 del rebuild (§6).
 */
export interface FunzionePermessoDto {
  featureKey: string
  displayName: string
  categoria: string
  stato: StatoCombo
  origin: OriginPermesso
}

export interface StoricoPermessoDto {
  id: number
  featureKey: string
  displayName: string
  accessBefore: string | null
  accessAfter: string | null
  origin: OriginPermesso
  changedBy: string
  changedAt: string
}

export interface SchedaPermessiDto {
  employeeId: number
  nome: string
  username: string
  status: string
  classe: string
  classeDisplay: string
  reparti: string[]
  /** Ha la riga jolly `*`: vede tutto, comprese le funzioni non ancora inventate. */
  jolly: boolean
  funzioni: FunzionePermessoDto[]
  storico: StoricoPermessoDto[]
}

export interface RigaPermessiDto {
  employeeId: number
  nome: string
  username: string
  classe: string
  classeDisplay: string
  reparti: string[]
  funzioni: number
  /** Righe decise a mano (`origin = MANO`): le eccezioni che «Applica template» rispetta. */
  aMano: number
  jolly: boolean
  /**
   * Utenza segnaposto di reparto (`[ACQ] Generico`…): ha i suoi permessi e resta nell'elenco,
   * ma non è una persona e non va offerta come modello nel «Copia da».
   */
  segnaposto: boolean
}

/** Un profilo/template della pagina Master (§5.4 rebuild). */
export interface ClasseDto {
  classe: string
  display: string
  /** Il pacchetto è la riga jolly `*`: si applica com'è, non si configura voce per voce. */
  jolly: boolean
  /** Voci che il pacchetto concede (jolly escluso). */
  voci: number
}

/** Una riga del pacchetto di un template: chiave concessa e a che livello. */
export interface PacchettoRigaDto {
  roleName: string
  featureKey: string
  access: StatoCombo
}

export interface CambioPrevistoDto {
  employeeId: number
  nome: string
  featureKey: string
  displayName: string
  da: StatoCombo
  a: StatoCombo
}

export interface EsitoApplicaClasseDto {
  persone: number
  /** Quante voci cambierebbero (o sono cambiate) in tutto. */
  voci: number
  cambi: CambioPrevistoDto[]
  /** Voci lasciate stare perché decise a mano (`origin = MANO`). */
  rispettateAMano: number
}
