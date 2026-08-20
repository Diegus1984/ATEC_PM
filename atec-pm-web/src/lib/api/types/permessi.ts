/** Tipi della scheda permessi per persona — allineati a ATEC.PM.Shared/DTOs/Permessi_DTOs.cs */

/** `NO` = non abilitato (nessuna riga), `READ` = sola lettura, `FULL` = lettura e scrittura. */
export type StatoCombo = "NO" | "READ" | "FULL"

/** Chi ha scritto la riga: il pacchetto della classe, o una mano umana. */
export type OriginPermesso = "CLASSE" | "MANO" | ""

/** Una delle 9 aree della maschera: una combo che può comandare più chiavi. */
export interface AreaPermessoDto {
  id: string
  titolo: string
  chiavi: string[]
  chiaviSoloScrittura: string[]
  /** Solo due stati (no / sì): Commesse, TimeSheet, Chat, Documenti. */
  dueStati: boolean
  /** Come si chiama lo stato «acceso» qui: «vede l'elenco», «carica le ore»… */
  etichettaAcceso: string
  stato: StatoCombo
  /** Cosa direbbe la sua classe: se diverge, la combo è «diversa dalla classe». */
  statoClasse: StatoCombo
  aMano: boolean
  /** Le chiavi dell'area non sono tutte allo stesso stato: la combo mostra il minimo. */
  incoerente: boolean
}

export interface FunzionePermessoDto {
  featureKey: string
  displayName: string
  categoria: string
  stato: StatoCombo
  statoClasse: StatoCombo
  origin: OriginPermesso
  /** Se valorizzato, la funzione è già governata da una delle 9 aree. */
  areaId: string | null
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
  aree: AreaPermessoDto[]
  funzioni: FunzionePermessoDto[]
  storico: StoricoPermessoDto[]
  diverseDallaClasse: number
}

export interface RigaPermessiDto {
  employeeId: number
  nome: string
  username: string
  classe: string
  classeDisplay: string
  reparti: string[]
  funzioni: number
  diverseDallaClasse: number
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
  combo: number
  cambi: CambioPrevistoDto[]
  /** Combo lasciate stare perché decise a mano (`origin = MANO`). */
  rispettateAMano: number
}
