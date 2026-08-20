import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  ClasseDto,
  EsitoApplicaClasseDto,
  PacchettoRigaDto,
  RigaPermessiDto,
  SchedaPermessiDto,
  StatoCombo,
} from "@/lib/api/types"

/** Elenco delle persone attive con un'utenza. */
export async function fetchElencoPermessi(): Promise<RigaPermessiDto[]> {
  const response = await apiGet<ApiResponse<RigaPermessiDto[]>>("/api/permessi")
  return unwrapApi(response)
}

/** La scheda di una persona: catalogo intero con lo stato, ultime 20 modifiche. */
export async function fetchSchedaPermessi(
  employeeId: number
): Promise<SchedaPermessiDto> {
  const response = await apiGet<ApiResponse<SchedaPermessiDto>>(
    `/api/permessi/${employeeId}`
  )
  return unwrapApi(response)
}

/**
 * Cambia UNA voce del catalogo sulla persona. Quello che si tocca qui diventa `MANO`:
 * «Applica template» smetterà di sovrascriverlo finché non si preme «Torna al template».
 * `NO` scrive un diniego, non cancella la riga (§3.7: spegnere non è cancellare).
 */
export async function impostaPermesso(request: {
  employeeId: number
  featureKey: string
  stato: StatoCombo
}): Promise<void> {
  const response = await apiPut<ApiResponse<null>>("/api/permessi/voce", request)
  unwrapApi(response)
}

/**
 * Applica il pacchetto del template.
 *
 * ⚠️ Con `anteprima: true` **non scrive niente** e torna l'elenco esatto dei cambi: è quello
 * che l'utente conferma. Chiamarla direttamente con `anteprima: false` senza aver mostrato
 * l'anteprima è esattamente il gesto che il piano vieta (§4.4), perché un timbro di massa può
 * cancellare in silenzio le eccezioni messe apposta sulle singole persone.
 */
export async function applicaClasse(request: {
  employeeIds: number[]
  anteprima: boolean
  /** Template esplicito (pagina Master): vuoto = la classe di ciascuno. */
  classe?: string
}): Promise<EsitoApplicaClasseDto> {
  const response = await apiPost<ApiResponse<EsitoApplicaClasseDto>>(
    "/api/permessi/applica-classe",
    request
  )
  return unwrapApi(response)
}

/**
 * Copia la scheda di un collega: un CLONE, origin compresi (§3.6) — le righe da template
 * restano CLASSE, le eccezioni restano MANO, e i futuri «Applica template» sul clonato
 * funzionano come sull'originale. Con `anteprima: true` non scrive niente.
 */
export async function copiaPermessi(request: {
  daEmployeeId: number
  aEmployeeId: number
  anteprima: boolean
}): Promise<EsitoApplicaClasseDto> {
  const response = await apiPost<ApiResponse<EsitoApplicaClasseDto>>(
    "/api/permessi/copia",
    request
  )
  return unwrapApi(response)
}

/** I profili della pagina Master: classi con pacchetto riassunto. */
export async function fetchClassi(): Promise<ClasseDto[]> {
  const response = await apiGet<ApiResponse<ClasseDto[]>>("/api/permessi/classi")
  return unwrapApi(response)
}

/** Il pacchetto di un template, riga per riga. */
export async function fetchPacchetto(classe: string): Promise<PacchettoRigaDto[]> {
  const response = await apiGet<ApiResponse<PacchettoRigaDto[]>>(
    `/api/permessi/classe/${encodeURIComponent(classe)}`
  )
  return unwrapApi(response)
}

/**
 * Scrive una voce del template (pagina Master). `NO` = la voce esce dal pacchetto (§3.7).
 * Salvare il master non cambia nessuno: i grant si muovono solo con «Applica template».
 */
export async function impostaPacchetto(request: {
  classe: string
  featureKey: string
  stato: StatoCombo
}): Promise<void> {
  const response = await apiPut<ApiResponse<null>>("/api/permessi/pacchetto", request)
  unwrapApi(response)
}

/** «Torna al template» su una voce, o su tutte se `featureKey` è omesso. */
export async function riallineaAllaClasse(request: {
  employeeId: number
  featureKey?: string
}): Promise<EsitoApplicaClasseDto> {
  const response = await apiPost<ApiResponse<EsitoApplicaClasseDto>>(
    "/api/permessi/riallinea",
    request
  )
  return unwrapApi(response)
}
